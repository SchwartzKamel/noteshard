using System.Text.Json;
using ObsidianQuickNoteWidget.Core.Models;

namespace ObsidianQuickNoteWidget.Core.Runner;

/// <summary>
/// JSON-backed <see cref="IActionCatalogStore"/> persisting to
/// <c>%LocalAppData%\ObsidianQuickNoteWidget\action-catalog.json</c>.
/// <para>
/// Mirrors the persistence patterns used by
/// <see cref="State.JsonStateStore"/>: in-memory cache loaded lazily, an
/// in-process gate serializing mutating calls, atomic writes via
/// <c>tmp + File.Move(overwrite)</c>, and permissive error handling on load
/// so a corrupt file degrades to an empty catalog rather than crashing the
/// widget host.
/// </para>
/// <para>
/// Cross-process safety is intentionally not provided here — %LocalAppData%
/// is user-scoped and last-write-wins semantics are acceptable for this
/// foundation layer.
/// </para>
/// </summary>
public sealed class JsonActionCatalogStore : IActionCatalogStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<RunnerAction>? _cache;

    public JsonActionCatalogStore(string? path = null)
    {
        _path = path ?? DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "ObsidianQuickNoteWidget", "action-catalog.json");

    public async Task<IReadOnlyList<RunnerAction>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cache = EnsureLoaded();
            return cache.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RunnerAction?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cache = EnsureLoaded();
            for (var i = 0; i < cache.Count; i++)
            {
                if (cache[i].Id == id) return cache[i];
            }
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RunnerAction> AddAsync(string label, string commandId, string? icon = null, CancellationToken ct = default)
    {
        var (normLabel, normCommandId) = RunnerActionValidator.Normalize(label, commandId);
        var normIcon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cache = EnsureLoaded();
            var action = new RunnerAction(Guid.NewGuid(), normLabel, normCommandId, normIcon);
            cache.Add(action);
            Persist(cache);
            return action;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cache = EnsureLoaded();
            for (var i = 0; i < cache.Count; i++)
            {
                if (cache[i].Id == id)
                {
                    cache.RemoveAt(i);
                    Persist(cache);
                    return true;
                }
            }
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<RunnerAction> EnsureLoaded()
    {
        if (_cache is not null) return _cache;
        _cache = Load();
        return _cache;
    }

    private List<RunnerAction> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<RunnerAction>();
            var json = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<List<RunnerAction>>(json, JsonOpts);
            return parsed ?? new List<RunnerAction>();
        }
        catch
        {
            return new List<RunnerAction>();
        }
    }

    private void Persist(List<RunnerAction> cache)
    {
        var tmp = _path + ".tmp";
        var json = JsonSerializer.Serialize(cache, JsonOpts);
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }
}
