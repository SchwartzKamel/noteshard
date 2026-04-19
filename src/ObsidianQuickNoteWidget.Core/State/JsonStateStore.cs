using System.Text.Json;
using ObsidianQuickNoteWidget.Core.IO;
using ObsidianQuickNoteWidget.Core.Logging;

namespace ObsidianQuickNoteWidget.Core.State;

/// <summary>
/// JSON-backed store at %LocalAppData%\ObsidianQuickNoteWidget\state.json.
/// Holds a dictionary of widgetId → <see cref="WidgetState"/>.
/// <para>
/// Thread-safety: individual <see cref="Get"/>/<see cref="Save"/>/<see cref="Delete"/>
/// calls are serialized with an in-process lock. The read-modify-write sequence
/// (<c>Get → mutate → Save</c>) is <b>not</b> atomic at this layer; callers that
/// need atomicity must wrap the sequence in their own per-widget mutex (see
/// <c>AsyncKeyedLock</c>).
/// </para>
/// <para>
/// Cross-process coordination: when multiple processes (e.g. the widget COM
/// server and the tray app) open the same <c>state.json</c>, they each maintain
/// an independent in-memory cache. Writes are last-write-wins at the file level
/// — there is no cross-process lock. Consumers that care about cross-process
/// safety must either partition widget ids or layer an external mutex on top.
/// </para>
/// </summary>
public sealed class JsonStateStore : IStateStore
{
    // F-06: cap state.json at 1 MB. Anything larger is either corruption or a
    // (future) DoS vector; rename with timestamp and degrade to empty state.
    private const long MaxStateFileBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly ILog _log;
    private readonly Lock _gate = new();
    private Dictionary<string, WidgetState> _cache;

    public JsonStateStore(string? path = null, ILog? log = null)
    {
        _path = path ?? DefaultPath();
        _log = log ?? NullLog.Instance;
        // F-08: create the state directory with owner-only ACLs on Windows.
        DirectorySecurityHelper.CreateWithOwnerOnlyAcl(Path.GetDirectoryName(_path)!, _log);
        _cache = Load();
    }

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "ObsidianQuickNoteWidget", "state.json");

    public WidgetState Get(string widgetId)
    {
        ArgumentException.ThrowIfNullOrEmpty(widgetId);
        lock (_gate)
        {
            if (_cache.TryGetValue(widgetId, out var s)) return Clone(s);
            return new WidgetState { WidgetId = widgetId };
        }
    }

    public void Save(WidgetState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrEmpty(state.WidgetId);
        lock (_gate)
        {
            _cache[state.WidgetId] = Clone(state);
            Persist();
        }
    }

    public void Delete(string widgetId)
    {
        lock (_gate)
        {
            if (_cache.Remove(widgetId)) Persist();
        }
    }

    private Dictionary<string, WidgetState> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, WidgetState>();

            // F-06: size-guard before read. A pathologically large state file
            // is quarantined (renamed) and treated as empty so the widget
            // doesn't spend memory deserializing an attacker-planted blob.
            var info = new FileInfo(_path);
            if (info.Length > MaxStateFileBytes)
            {
                _log.Warn($"state file oversized ({info.Length} bytes > {MaxStateFileBytes}); quarantining");
                QuarantineBadFile("oversized");
                return new Dictionary<string, WidgetState>();
            }

            var json = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, WidgetState>>(json, JsonOpts);
            return parsed ?? new Dictionary<string, WidgetState>();
        }
        catch (JsonException ex)
        {
            // F-07: corruption gets a sidecar copy so the user has a recovery
            // path without blocking the widget from starting.
            _log.Error("state load failed (corrupt json); quarantining", ex);
            QuarantineBadFile("corrupt");
            return new Dictionary<string, WidgetState>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("state load failed", ex);
            return new Dictionary<string, WidgetState>();
        }
    }

    private void QuarantineBadFile(string reason)
    {
        try
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var dest = $"{_path}.{reason}.{stamp}";
            File.Move(_path, dest, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"state quarantine failed: {FileLog.SanitizeForLogLine(ex.Message)}");
        }
    }

    private void Persist()
    {
        try
        {
            var tmp = _path + ".tmp";
            var json = JsonSerializer.Serialize(_cache, JsonOpts);
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Best-effort: widget must never crash over state persistence.
            // Log-only (no rename) — transient IO errors shouldn't trash the
            // on-disk file.
            _log.Error("state persist failed", ex);
        }
    }

    private static WidgetState Clone(WidgetState s) =>
        JsonSerializer.Deserialize<WidgetState>(JsonSerializer.Serialize(s, JsonOpts), JsonOpts)!;
}
