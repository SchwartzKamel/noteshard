using System.Text.Json;
using ObsidianQuickNoteWidget.Core.AdaptiveCards;
using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Logging;
using ObsidianQuickNoteWidget.Core.Models;
using ObsidianQuickNoteWidget.Core.Runner;
using ObsidianQuickNoteWidget.Core.State;

namespace ObsidianQuickNoteWidget.Providers;

/// <summary>
/// Plugin Runner widget handler: renders the grid / customize / confirm-remove
/// card variants and dispatches the runner verbs.
/// <para>
/// The handler mutates the <see cref="WidgetState"/> passed in-place; callers
/// own the gate and persistence lifecycle. The handler never throws across
/// the COM boundary: all verb paths trap their own failures and surface them
/// on <see cref="WidgetState.LastError"/> or
/// <see cref="WidgetState.LastRunResult"/>.
/// </para>
/// </summary>
internal sealed class PluginRunnerHandler
{
    private readonly IActionCatalogStore _catalog;
    private readonly IObsidianCommandInvoker _invoker;
    private readonly ILog _log;

    public PluginRunnerHandler(
        IActionCatalogStore catalog,
        IObsidianCommandInvoker invoker,
        ILog log)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Builds the (template, data) pair for the current state: customize card
    /// when <see cref="WidgetState.IsCustomizing"/> is true, confirm-remove
    /// card when <see cref="WidgetState.PendingRemoveId"/> is set, else the
    /// size-appropriate grid.
    /// </summary>
    public async Task<(string Template, string Data)> BuildCardAsync(
        WidgetState state, WidgetSize size, CancellationToken ct = default)
    {
        var catalog = await _catalog.ListAsync(ct).ConfigureAwait(false);

        if (state.PendingRemoveId is not null)
        {
            return (
                CardTemplates.LoadPluginRunnerConfirmRemove(),
                CardDataBuilder.BuildPluginRunnerConfirmData(state, catalog));
        }

        if (state.IsCustomizing)
        {
            return (
                CardTemplates.LoadPluginRunnerCustomize(),
                CardDataBuilder.BuildPluginRunnerCustomizeData(state, catalog));
        }

        return (
            CardTemplates.LoadPluginRunner(size),
            CardDataBuilder.BuildPluginRunnerData(state, catalog, size));
    }

    /// <summary>
    /// Dispatches a single widget verb, mutating <paramref name="state"/>
    /// in-place. Catalog + CLI calls are awaited inline — callers should
    /// hold the per-widget gate around this call to serialize state mutation.
    /// </summary>
    public async Task HandleVerbAsync(
        string verb, string? actionData, WidgetState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var inputs = ParseInputs(actionData);
        try
        {
            switch (verb)
            {
                case "runAction":
                    await RunActionAsync(inputs, state, ct).ConfigureAwait(false);
                    break;
                case "openCustomize":
                    state.IsCustomizing = true;
                    state.PendingRemoveId = null;
                    ClearTransientStatus(state);
                    break;
                case "cancelCustomize":
                    state.IsCustomizing = false;
                    ClearTransientStatus(state);
                    break;
                case "addAction":
                    await AddActionAsync(inputs, state, ct).ConfigureAwait(false);
                    break;
                case "removeActionConfirm":
                    if (TryParseActionId(inputs, out var confirmId))
                    {
                        state.PendingRemoveId = confirmId;
                        ClearTransientStatus(state);
                    }
                    break;
                case "removeAction":
                    await RemoveActionAsync(inputs, state, ct).ConfigureAwait(false);
                    break;
                case "cancelRemove":
                    state.PendingRemoveId = null;
                    ClearTransientStatus(state);
                    break;
                case "pinAction":
                    if (TryParseActionId(inputs, out var pinId) && !state.PinnedActionIds.Contains(pinId))
                        state.PinnedActionIds.Add(pinId);
                    break;
                case "unpinAction":
                    if (TryParseActionId(inputs, out var unpinId))
                        state.PinnedActionIds.RemoveAll(g => g == unpinId);
                    break;
                default:
                    _log.Warn($"PluginRunner: unknown verb '{verb}'");
                    break;
            }
        }
        catch (Exception ex)
        {
            // COM boundary — never throw. Surface the error on the widget.
            _log.Error($"PluginRunner verb '{verb}' failed", ex);
            state.LastError = ex.Message;
            state.LastStatus = null;
        }
    }

    private async Task RunActionAsync(
        IReadOnlyDictionary<string, string> inputs, WidgetState state, CancellationToken ct)
    {
        if (!TryParseActionId(inputs, out var actionId))
        {
            state.LastRunResult = new RunnerActionResult
            {
                ActionId = Guid.Empty,
                Success = false,
                Error = "Missing actionId",
                At = DateTimeOffset.UtcNow,
            };
            return;
        }

        var action = await _catalog.GetAsync(actionId, ct).ConfigureAwait(false);
        if (action is null)
        {
            state.LastRunResult = new RunnerActionResult
            {
                ActionId = actionId,
                Success = false,
                Error = "Action not found",
                At = DateTimeOffset.UtcNow,
            };
            return;
        }

        try
        {
            var result = await _invoker.RunCommandAsync(action.CommandId, ct).ConfigureAwait(false);
            state.LastRunResult = new RunnerActionResult
            {
                ActionId = actionId,
                Success = result.Success,
                Error = result.Success ? null : result.ErrorMessage,
                At = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex)
        {
            _log.Warn($"PluginRunner runAction '{action.CommandId}' threw: {ex.Message}");
            state.LastRunResult = new RunnerActionResult
            {
                ActionId = actionId,
                Success = false,
                Error = ex.Message,
                At = DateTimeOffset.UtcNow,
            };
        }
    }

    private async Task AddActionAsync(
        IReadOnlyDictionary<string, string> inputs, WidgetState state, CancellationToken ct)
    {
        var label = inputs.GetValueOrDefault("newLabel") ?? string.Empty;
        var commandId = inputs.GetValueOrDefault("newCommandId") ?? string.Empty;

        try
        {
            RunnerActionValidator.Normalize(label, commandId);
        }
        catch (ArgumentException ex)
        {
            state.LastError = ex.Message;
            state.LastStatus = null;
            // Stay on the customize card so the user can fix input.
            state.IsCustomizing = true;
            return;
        }

        RunnerAction added;
        try
        {
            added = await _catalog.AddAsync(label, commandId, icon: null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            state.LastError = ex.Message;
            state.LastStatus = null;
            state.IsCustomizing = true;
            return;
        }

        if (!state.PinnedActionIds.Contains(added.Id))
            state.PinnedActionIds.Add(added.Id);

        state.IsCustomizing = false;
        state.LastStatus = $"Added '{added.Label}'";
        state.LastError = null;
    }

    private async Task RemoveActionAsync(
        IReadOnlyDictionary<string, string> inputs, WidgetState state, CancellationToken ct)
    {
        if (!TryParseActionId(inputs, out var id))
        {
            state.PendingRemoveId = null;
            return;
        }

        try
        {
            await _catalog.RemoveAsync(id, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            state.LastError = ex.Message;
            state.LastStatus = null;
            state.PendingRemoveId = null;
            return;
        }

        state.PinnedActionIds.RemoveAll(g => g == id);
        state.PendingRemoveId = null;
        state.LastStatus = "Action removed";
        state.LastError = null;
    }

    private static bool TryParseActionId(IReadOnlyDictionary<string, string> inputs, out Guid id)
    {
        id = Guid.Empty;
        return inputs.TryGetValue("actionId", out var raw)
            && !string.IsNullOrWhiteSpace(raw)
            && Guid.TryParse(raw, out id);
    }

    private static void ClearTransientStatus(WidgetState state)
    {
        // Navigating between views (open/close customize, cancel confirm) is a
        // user intent to move on — drop any stale add/remove banner so the new
        // surface starts clean.
        state.LastError = null;
        state.LastStatus = null;
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
                map[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => prop.Value.ToString(),
                };
            }
        }
        catch
        {
            // malformed payload — treat as no inputs
        }
        return map;
    }
}
