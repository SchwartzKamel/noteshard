using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.Widgets.Providers;
using ObsidianQuickNoteWidget.Core.AdaptiveCards;
using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Concurrency;
using ObsidianQuickNoteWidget.Core.Logging;
using ObsidianQuickNoteWidget.Core.Notes;
using ObsidianQuickNoteWidget.Core.Runner;
using ObsidianQuickNoteWidget.Core.State;

namespace ObsidianQuickNoteWidget.Providers;

/// <summary>
/// COM-exposed widget provider. One instance serves all widgets pinned from the
/// Widget Board; per-widget data is kept in <see cref="JsonStateStore"/>.
/// </summary>
[ComVisible(true)]
[Guid(WidgetIdentifiers.ProviderClsid)]
public sealed partial class ObsidianWidgetProvider : IWidgetProvider, IWidgetProvider2
{
    private readonly ILog _log;
    private readonly IStateStore _store;
    private readonly IObsidianCli _cli;
    private readonly IObsidianLauncher _launcher;
    private readonly NoteCreationService _notes;
    private readonly PluginRunnerHandler _pluginRunner;
    private readonly ConcurrentDictionary<string, WidgetSession> _active = new();

    // Per-widget async mutex. Every Get → mutate → Save sequence on the store
    // runs under the gate for that widget id, which serializes concurrent COM
    // callbacks, timer ticks, and fire-and-forget refresh tasks within this
    // process. Cross-process coordination with the tray is still last-write-wins
    // (see JsonStateStore xmldoc).
    private readonly AsyncKeyedLock<string> _gate =
        new(StringComparer.OrdinalIgnoreCase);

    // Lifetime: tied to COM host process — no explicit dispose. The Widget Host
    // terminates this process when all widgets go away, so the Timer is reclaimed
    // with the process.
    private readonly Timer _folderRefreshTimer;
    private static readonly TimeSpan FolderRefreshInterval = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan RecentNotesCacheTtl = TimeSpan.FromSeconds(30);

    public ObsidianWidgetProvider()
        : this(new FileLog(), new JsonStateStore(), null, null, null) { }

    internal ObsidianWidgetProvider(
        ILog log,
        IStateStore store,
        IObsidianCli? cli,
        IActionCatalogStore? catalog = null,
        IObsidianLauncher? launcher = null)
    {
        _log = log;
        _store = store;
        _cli = cli ?? new ObsidianCli(log);
        _launcher = launcher ?? new ObsidianLauncher(log);
        _notes = new NoteCreationService(_cli, log);
        var invoker = new ObsidianCommandInvoker(_cli, log);
        _pluginRunner = new PluginRunnerHandler(
            catalog ?? new JsonActionCatalogStore(),
            invoker,
            log);
        _folderRefreshTimer = new Timer(
            _ => FireAndLog(RefreshAllActiveAsync, "timer", "refreshAll"),
            state: null,
            dueTime: FolderRefreshInterval,
            period: FolderRefreshInterval);
    }

    // ---- IWidgetProvider ----

    public void CreateWidget(WidgetContext widgetContext)
    {
        var id = widgetContext.Id;
        var definitionId = widgetContext.DefinitionId;
        var sizeLabel = widgetContext.Size.ToString().ToLowerInvariant();
        _log.Info($"CreateWidget id={id} kind={definitionId} size={widgetContext.Size}");

        FireAndLog(() => _gate.WithLockAsync(id, async () =>
        {
            var state = _store.Get(id);
            state.WidgetId = id;
            state.Size = sizeLabel;
            _store.Save(state);
            _active[id] = new WidgetSession(id, definitionId, state.Size);
            await Task.CompletedTask.ConfigureAwait(false);
        }), id, "createWidget", pushUpdateOnCompletion: true);

        FireAndLog(() => RefreshFolderCacheAsync(id), id, "refreshFolderCache");
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        _log.Info($"DeleteWidget id={widgetId}");
        // Acquire the gate before mutating _active + store so any in-flight
        // refresh/action for this id either finished first or will observe the
        // removal and skip its save.
        FireAndLog(() => _gate.WithLockAsync(widgetId, async () =>
        {
            _active.TryRemove(widgetId, out _);
            _store.Delete(widgetId);
            await Task.CompletedTask.ConfigureAwait(false);
        }), widgetId, "deleteWidget");
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var id = actionInvokedArgs.WidgetContext.Id;
        var verb = actionInvokedArgs.Verb ?? string.Empty;
        var definitionId = actionInvokedArgs.WidgetContext.DefinitionId;
        var data = actionInvokedArgs.Data;
        _log.Info($"OnActionInvoked id={id} verb={verb}");

        FireAndLog(async () =>
        {
            var session = _active.GetOrAdd(id, _ => new WidgetSession(id, definitionId, _store.Get(id).Size));
            await HandleVerbAsync(session, verb, data).ConfigureAwait(false);
        }, id, $"action:{verb}", pushUpdateOnCompletion: true);
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var ctx = contextChangedArgs.WidgetContext;
        var id = ctx.Id;
        var newSize = ctx.Size.ToString().ToLowerInvariant();
        _log.Info($"OnWidgetContextChanged id={id} newSize={ctx.Size}");

        FireAndLog(() => _gate.WithLockAsync(id, async () =>
        {
            if (!_active.ContainsKey(id)) return;
            var state = _store.Get(id);
            state.Size = newSize;
            _store.Save(state);
            if (_active.TryGetValue(id, out var session)) session.Size = newSize;
            await Task.CompletedTask.ConfigureAwait(false);
        }), id, "contextChanged", pushUpdateOnCompletion: true);
    }

    public void Activate(WidgetContext widgetContext)
    {
        var id = widgetContext.Id;
        var definitionId = widgetContext.DefinitionId;
        _log.Info($"Activate id={id} size={widgetContext.Size}");

        FireAndLog(() => _gate.WithLockAsync(id, async () =>
        {
            _active.TryAdd(id, new WidgetSession(id, definitionId, _store.Get(id).Size));
            await Task.CompletedTask.ConfigureAwait(false);
        }), id, "activate", pushUpdateOnCompletion: true);

        FireAndLog(() => RefreshFolderCacheAsync(id), id, "refreshFolderCache");
    }

    public void Deactivate(string widgetId) { _log.Info($"Deactivate id={widgetId}"); _active.TryRemove(widgetId, out _); }

    // Test-only hook: register a widget in the active map without going
    // through the COM Create/Activate path. Internal + guarded by
    // InternalsVisibleTo to the test assembly.
    internal void RegisterActiveForTest(string widgetId, string definitionId)
        => _active[widgetId] = new WidgetSession(widgetId, definitionId, _store.Get(widgetId).Size);

    // Test-only hook to exercise the verb dispatcher without going through
    // the COM OnActionInvoked entry point (which requires a real
    // WidgetActionInvokedArgs). Creates a session ad-hoc if the widget isn't
    // already registered.
    internal Task InvokeVerbForTest(string widgetId, string verb, string? data)
    {
        var session = _active.GetOrAdd(widgetId, _ => new WidgetSession(widgetId, string.Empty, _store.Get(widgetId).Size));
        return HandleVerbAsync(session, verb, data);
    }

    // ---- IWidgetProvider2 ----

    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs customizationRequestedArgs)
    {
        // v1 has no customization UI; we just acknowledge.
        _log.Info($"OnCustomizationRequested id={customizationRequestedArgs.WidgetContext.Id}");
    }

    // ---- internal ----

    /// <summary>
    /// Awaits <paramref name="work"/> inside a try/catch, logs any failure, and
    /// optionally writes the error to the widget's state + refreshes the card.
    /// Returns a Task so tests can observe completion; the returned Task never
    /// faults.
    /// </summary>
    internal Task FireAndLog(Func<Task> work, string widgetId, string context, bool pushUpdateOnCompletion = false)
    {
        return AsyncSafe.RunAsync(
            work,
            _log,
            $"[{widgetId}] {context}",
            onError: async ex =>
            {
                // Surface the failure to the user. Use the gate so we don't
                // clobber a concurrent writer.
                await _gate.WithLockAsync(widgetId, () =>
                {
                    if (_active.ContainsKey(widgetId))
                    {
                        var s = _store.Get(widgetId);
                        s.LastStatus = null;
                        s.LastError = ex.Message;
                        _store.Save(s);
                    }
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
                SafePushUpdate(widgetId);
            }).ContinueWith(_ =>
            {
                if (pushUpdateOnCompletion) SafePushUpdate(widgetId);
            }, TaskScheduler.Default);
    }

    private void SafePushUpdate(string widgetId)
    {
        try { PushUpdate(widgetId); }
        catch (Exception ex) { _log.Warn($"SafePushUpdate({widgetId}): {ex.Message}"); }
    }

    private async Task HandleVerbAsync(WidgetSession session, string verb, string? data)
    {
        if (string.Equals(session.DefinitionId, WidgetIdentifiers.PluginRunnerDefinitionId, StringComparison.Ordinal))
        {
            await _gate.WithLockAsync(session.Id, async () =>
            {
                if (!_active.ContainsKey(session.Id)) return;
                var state = _store.Get(session.Id);
                await _pluginRunner.HandleVerbAsync(verb, data, state).ConfigureAwait(false);
                _store.Save(state);
            }).ConfigureAwait(false);
            return;
        }

        // Persist the user's in-progress "new folder" text so that any
        // re-render (status swap, toggleAdvanced, periodic push) echoes it
        // back into the Input.Text via ${$root.inputs.folderNew} instead of
        // wiping it. CreateNoteAsync handles its own persistence + clear.
        if (verb != "createNote")
        {
            var preInputs = ParseInputs(data);
            if (preInputs.TryGetValue("folderNew", out var typed))
            {
                await _gate.WithLockAsync(session.Id, () =>
                {
                    if (_active.ContainsKey(session.Id))
                    {
                        var s = _store.Get(session.Id);
                        s.LastFolderNew = typed ?? string.Empty;
                        _store.Save(s);
                    }
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }
        }

        switch (verb)
        {
            case "createNote":
                await CreateNoteAsync(session, data).ConfigureAwait(false);
                break;
            case "pasteClipboard":
                session.PendingBodyPaste = TryReadClipboardText();
                await _gate.WithLockAsync(session.Id, () =>
                {
                    if (_active.ContainsKey(session.Id))
                    {
                        var s = _store.Get(session.Id);
                        s.LastStatus = session.PendingBodyPaste is null ? "Clipboard was empty" : "Clipboard pasted — review and Create";
                        s.LastError = null;
                        _store.Save(s);
                    }
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
                break;
            case "toggleAdvanced":
                session.ShowAdvanced = !session.ShowAdvanced;
                break;
            case "recheckCli":
                break; // next PushUpdate will re-evaluate CLI availability
            case "openRecent":
                await HandleOpenRecentAsync(session, data).ConfigureAwait(false);
                break;
            case "openVault":
                await AsyncSafe.RunAsync(
                    () => _launcher.LaunchVaultAsync(),
                    _log,
                    $"[{session.Id}] openVault").ConfigureAwait(false);
                break;
            default:
                _log.Warn($"Unknown verb: {verb}");
                break;
        }
    }

    private async Task CreateNoteAsync(WidgetSession session, string? actionData)
    {
        var inputs = ParseInputs(actionData);

        // The whole Get → CLI → mutate → Save round-trip runs under the per-id
        // gate so timer ticks and other actions for the same widget cannot
        // clobber the result.
        await _gate.WithLockAsync(session.Id, async () =>
        {
            if (!_active.ContainsKey(session.Id)) return;
            var state = _store.Get(session.Id);

            var title = inputs.GetValueOrDefault("title") ?? string.Empty;
            var folderNew = inputs.GetValueOrDefault("folderNew")?.Trim();
            var folder = ResolveFolder(folderNew, inputs.GetValueOrDefault("folder"), state.LastFolder);
            var body = inputs.GetValueOrDefault("body") ?? string.Empty;
            var tagsCsv = inputs.GetValueOrDefault("tagsCsv") ?? state.TagsCsv;
            var template = inputs.GetValueOrDefault("template") ?? state.Template;
            var autoDate = ParseBool(inputs.GetValueOrDefault("autoDatePrefix"), state.AutoDatePrefix);
            var openAfter = ParseBool(inputs.GetValueOrDefault("openAfterCreate"), state.OpenAfterCreate);
            var appendDaily = ParseBool(inputs.GetValueOrDefault("appendToDaily"), state.AppendToDaily);

            if (session.PendingBodyPaste is not null)
            {
                body = string.IsNullOrEmpty(body)
                    ? session.PendingBodyPaste
                    : body + "\n\n" + session.PendingBodyPaste;
                session.PendingBodyPaste = null;
            }

            var req = new NoteRequest(
                Title: title,
                Folder: folder,
                Body: body,
                TagsCsv: tagsCsv,
                Template: Enum.TryParse<NoteTemplate>(template, ignoreCase: true, out var t) ? t : NoteTemplate.Blank,
                AutoDatePrefix: autoDate,
                OpenAfterCreate: openAfter,
                AppendToDaily: appendDaily);

            var result = await _notes.CreateAsync(req).ConfigureAwait(false);

            state.AutoDatePrefix = autoDate;
            state.OpenAfterCreate = openAfter;
            state.AppendToDaily = appendDaily;
            state.TagsCsv = tagsCsv ?? string.Empty;
            state.Template = template ?? "Blank";

            if (result.Status == NoteCreationStatus.Created && !string.IsNullOrEmpty(result.VaultRelativePath))
            {
                // N15: Only persist LastFolder on successful create so a validation
                // rejection or CLI error cannot poison the default on next render.
                state.LastFolder = folder ?? string.Empty;

                RememberRecent(state.RecentNotes, result.VaultRelativePath!, max: 16);
                if (!string.IsNullOrEmpty(folder)) RememberRecent(state.RecentFolders, folder!, max: 8);

                // N14: Optimistically add a freshly-typed folder to CachedFolders so
                // BuildFolderChoices surfaces it on the very next render; the async
                // CLI refresh below only lands on a subsequent render cycle.
                if (!string.IsNullOrEmpty(folderNew))
                {
                    var check = FolderPathValidator.Validate(folderNew);
                    if (check.IsValid && !string.IsNullOrEmpty(check.NormalizedPath) &&
                        !state.CachedFolders.Contains(check.NormalizedPath!, StringComparer.OrdinalIgnoreCase))
                    {
                        state.CachedFolders.Add(check.NormalizedPath!);
                    }
                }

                state.LastStatus = result.Message;
                state.LastError = null;
                state.LastCreatedPath = result.VaultRelativePath;
                state.LastFolderNew = string.Empty;
            }
            else if (result.Status == NoteCreationStatus.AppendedToDaily)
            {
                state.LastStatus = result.Message;
                state.LastError = null;
                state.LastFolderNew = string.Empty;
            }
            else
            {
                state.LastStatus = null;
                state.LastError = result.Message;
                state.LastFolderNew = folderNew ?? string.Empty;
            }

            _store.Save(state);
        }).ConfigureAwait(false);

        // Auto-refresh folder cache after a successful note creation so that a
        // newly created folder becomes selectable immediately. The check is
        // best-effort: re-read state under the gate would block us, so trust
        // the just-written state.
        var latest = _store.Get(session.Id);
        if (string.IsNullOrEmpty(latest.LastError))
        {
            _ = FireAndLog(() => RefreshFolderCacheAsync(session.Id), session.Id, "refreshFolderCache");
        }
    }

    private async Task HandleOpenRecentAsync(WidgetSession session, string? data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data ?? "{}");
            if (doc.RootElement.TryGetProperty("path", out var p))
            {
                var path = p.GetString();
                if (string.IsNullOrWhiteSpace(path))
                {
                    _log.Warn($"[{session.Id}] openRecent: missing or empty path");
                    return;
                }
                // URI scheme launches Obsidian regardless of whether it's
                // already running, unlike the bundled CLI (which requires a
                // running instance).
                await _launcher.LaunchNoteAsync(path!).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { _log.Warn("openRecent parse failed: " + ex.Message); }
    }

    private async Task RefreshFolderCacheAsync(string widgetId)
    {
        if (!_cli.IsAvailable) return;
        // CLI call happens outside the gate so refreshes for different widgets
        // (and other actions for this widget) aren't blocked on it.
        var folders = (await _cli.ListFoldersAsync().ConfigureAwait(false)).ToList();
        var now = DateTimeOffset.Now;
        await _gate.WithLockAsync(widgetId, () =>
        {
            if (!_active.ContainsKey(widgetId)) return Task.CompletedTask;
            var state = _store.Get(widgetId);
            state.CachedFolders = folders;
            state.CachedFoldersAt = now;
            _store.Save(state);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        SafePushUpdate(widgetId);
    }

    /// <summary>
    /// Pure policy helper: should a RecentNotes-definition widget refresh its
    /// recents list? True when <paramref name="now"/> is past
    /// <see cref="WidgetState.RecentNotesRefreshedAt"/> + <paramref name="ttl"/>.
    /// Extracted for unit testability.
    /// </summary>
    internal static bool ShouldRefreshRecents(WidgetState state, DateTimeOffset now, TimeSpan ttl)
        => now - state.RecentNotesRefreshedAt > ttl;

    /// <summary>
    /// Refreshes the <see cref="WidgetState.RecentNotes"/> list for a
    /// RecentNotes-definition widget from the native <c>obsidian recents</c>
    /// CLI verb. The CLI call happens outside the per-widget gate; the state
    /// mutation (replace list, stamp <see cref="WidgetState.RecentNotesRefreshedAt"/>,
    /// save) happens under the gate so a concurrent note-create cannot
    /// interleave.
    /// </summary>
    internal async Task RefreshRecentNotesAsync(string widgetId)
    {
        if (!_cli.IsAvailable) return;
        // `obsidian recents` returns historical paths (including files that
        // have since been deleted), so we intersect with the live `obsidian
        // files` list to drop ghost entries. Run both concurrently — the
        // second call adds no wall-clock cost on top of the recents call.
        var recentsTask = _cli.ListRecentsAsync(max: 16);
        var filesTask = _cli.ListFilesAsync();
        await Task.WhenAll(recentsTask, filesTask).ConfigureAwait(false);
        var recents = recentsTask.Result;
        var files = filesTask.Result;
        var now = DateTimeOffset.Now;
        await _gate.WithLockAsync(widgetId, () =>
        {
            if (!_active.ContainsKey(widgetId)) return Task.CompletedTask;
            var state = _store.Get(widgetId);
            var filtered = IntersectRecentsWithFiles(recents, files, _log);
            state.RecentNotes = filtered;
            state.RecentNotesRefreshedAt = now;
            _store.Save(state);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        SafePushUpdate(widgetId);
    }

    /// <summary>
    /// Intersects <paramref name="recents"/> with <paramref name="files"/>,
    /// preserving recents' original order (which reflects recency), deduping
    /// case-insensitively, trimming, and capping to 16. Defensive failure
    /// modes:
    /// <list type="bullet">
    ///   <item>Both empty → empty (nothing to render).</item>
    ///   <item>Recents non-empty, files empty → return recents as-is and warn.
    ///     Treating an empty <c>files</c> response as "vault has no files"
    ///     would wipe the list on any CLI hiccup; showing a possibly-stale
    ///     entry is less bad than a blank widget.</item>
    ///   <item>Both non-empty → intersect case-insensitively.</item>
    /// </list>
    /// </summary>
    internal static List<string> IntersectRecentsWithFiles(
        IReadOnlyList<string> recents,
        IReadOnlyList<string> files,
        ILog log)
    {
        var trimmed = new List<string>(recents.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in recents)
        {
            var t = (p ?? string.Empty).Trim();
            if (t.Length == 0) continue;
            if (seen.Add(t)) trimmed.Add(t);
        }

        if (trimmed.Count == 0) return trimmed;

        if (files.Count == 0)
        {
            log.Warn($"RefreshRecentNotes: 'obsidian files' returned 0 entries while recents returned {trimmed.Count}; skipping intersection (possible CLI hiccup).");
            if (trimmed.Count > 16) trimmed.RemoveRange(16, trimmed.Count - 16);
            return trimmed;
        }

        var liveSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var t = (f ?? string.Empty).Trim();
            if (t.Length > 0) liveSet.Add(t);
        }

        var result = new List<string>(trimmed.Count);
        foreach (var p in trimmed)
        {
            if (!liveSet.Contains(p)) continue;
            result.Add(p);
            if (result.Count >= 16) break;
        }
        return result;
    }

    /// <summary>
    /// Periodic-timer callback: refreshes the folder cache for every currently
    /// active widget so the dropdown picks up vault changes made in Obsidian
    /// without the user having to deactivate/reactivate the widget.
    /// </summary>
    private async Task RefreshAllActiveAsync()
    {
        if (_active.IsEmpty || !_cli.IsAvailable) return;
        // One CLI call shared across all widgets — then write to each state.
        var folders = (await _cli.ListFoldersAsync().ConfigureAwait(false)).ToList();
        var now = DateTimeOffset.Now;
        foreach (var id in _active.Keys.ToArray())
        {
            await FireAndLog(() => _gate.WithLockAsync(id, () =>
            {
                // Re-check under the gate: the widget may have been deleted
                // between the snapshot and our turn. If so, skip the save so
                // we don't resurrect a deleted entry.
                if (!_active.ContainsKey(id)) return Task.CompletedTask;
                var state = _store.Get(id);
                state.CachedFolders = folders;
                state.CachedFoldersAt = now;
                _store.Save(state);
                return Task.CompletedTask;
            }), id, "refreshAllActive", pushUpdateOnCompletion: true).ConfigureAwait(false);
        }
    }

    private void PushUpdate(string widgetId)
    {
        try
        {
            if (!_active.TryGetValue(widgetId, out var session)) return;
            var state = _store.Get(widgetId);

            string template;
            string data;

            if (!_cli.IsAvailable)
            {
                template = CardTemplates.Load(CardTemplates.CliMissingTemplate);
                data = CardDataBuilder.BuildCliMissingData("`obsidian` was not found on PATH.");
            }
            else if (string.Equals(session.DefinitionId, WidgetIdentifiers.PluginRunnerDefinitionId, StringComparison.Ordinal))
            {
                var size = ParseWidgetSize(session.Size);
                // Synchronously await the handler: we're on the thread pool via
                // FireAndLog's ContinueWith, so blocking briefly on the catalog
                // semaphore is safe.
                var (t, d) = _pluginRunner.BuildCardAsync(state, size)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                template = t;
                data = d;
            }
            else if (string.Equals(session.DefinitionId, WidgetIdentifiers.RecentNotesWidgetId, StringComparison.Ordinal))
            {
                // Fire-and-log a refresh when the 30s TTL has expired. The CLI
                // call runs outside the gate; on completion it re-saves state
                // and triggers another PushUpdate, at which point this branch
                // renders the fresh list from state.
                if (_cli.IsAvailable && ShouldRefreshRecents(state, DateTimeOffset.Now, RecentNotesCacheTtl))
                {
                    FireAndLog(() => RefreshRecentNotesAsync(widgetId), widgetId, "refreshRecentNotes");
                }
                template = CardTemplates.Load(CardTemplates.RecentNotesTemplate);
                data = CardDataBuilder.BuildQuickNoteData(state, session.ShowAdvanced);
            }
            else
            {
                template = CardTemplates.LoadForSize(session.Size);
                data = CardDataBuilder.BuildQuickNoteData(state, session.ShowAdvanced);
            }

            _log.Info($"PushUpdate id={widgetId} def={session.DefinitionId} size={session.Size} templateLen={template?.Length ?? 0} dataLen={data?.Length ?? 0}");
            var options = new WidgetUpdateRequestOptions(widgetId)
            {
                Template = template,
                Data = data,
                CustomState = string.Empty,
            };
            WidgetManager.GetDefault().UpdateWidget(options);
        }
        catch (Exception ex)
        {
            _log.Error("PushUpdate failed", ex);
        }
    }

    private static void RememberRecent(List<string> list, string entry, int max)
    {
        list.RemoveAll(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, entry);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
    }

    private static Dictionary<string, string> ParseInputs(string? json)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    map[prop.Name] = prop.Value.GetString() ?? string.Empty;
                else if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    map[prop.Name] = prop.Value.GetBoolean() ? "true" : "false";
                else
                    map[prop.Name] = prop.Value.ToString();
            }
        }
        catch { /* ignore malformed */ }
        return map;
    }

    private static bool ParseBool(string? s, bool fallback) =>
        s is null ? fallback : string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);

    private static WidgetSize ParseWidgetSize(string? size) => size?.ToLowerInvariant() switch
    {
        "small" => WidgetSize.Small,
        "large" => WidgetSize.Large,
        _ => WidgetSize.Medium,
    };

    /// <summary>
    /// Resolves the folder for a new note using the documented precedence:
    /// trimmed <paramref name="folderNew"/> (if non-empty) wins, else the
    /// picker-selected <paramref name="picker"/>, else <paramref name="lastFolder"/>.
    /// </summary>
    internal static string? ResolveFolder(string? folderNew, string? picker, string? lastFolder)
    {
        var trimmed = folderNew?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            ? trimmed
            : picker ?? lastFolder;
    }

    private static string? TryReadClipboardText()
    {
        try
        {
            var t = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (t.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                // GetTextAsync returns IAsyncOperation<string>; block briefly.
                var op = t.GetTextAsync();
                return op.AsTask().GetAwaiter().GetResult();
            }
        }
        catch { /* ignore */ }
        return null;
    }

    internal sealed class WidgetSession
    {
        public string Id { get; }
        public string DefinitionId { get; }
        public string Size { get; set; }
        public bool ShowAdvanced { get; set; }
        public string? PendingBodyPaste { get; set; }

        public WidgetSession(string id, string definitionId, string size)
        {
            Id = id; DefinitionId = definitionId; Size = size;
        }
    }
}
