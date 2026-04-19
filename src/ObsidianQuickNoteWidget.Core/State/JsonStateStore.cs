using System.Text.Json;

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
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, WidgetState> _cache;

    public JsonStateStore(string? path = null)
    {
        _path = path ?? DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
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
            var json = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, WidgetState>>(json, JsonOpts);
            return parsed ?? new Dictionary<string, WidgetState>();
        }
        catch
        {
            return new Dictionary<string, WidgetState>();
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
        catch
        {
            // best effort; widget must never crash over state persistence
        }
    }

    private static WidgetState Clone(WidgetState s) =>
        JsonSerializer.Deserialize<WidgetState>(JsonSerializer.Serialize(s, JsonOpts), JsonOpts)!;
}
