using ObsidianQuickNoteWidget.Core.Models;

namespace ObsidianQuickNoteWidget.Core.Runner;

/// <summary>
/// Persistence surface for the Plugin Runner action catalog. Implementations
/// must serialize overlapping writes and guarantee that readers never observe
/// a torn file on disk. Returned collections are snapshots; callers may mutate
/// them freely without affecting the store.
/// </summary>
public interface IActionCatalogStore
{
    Task<IReadOnlyList<RunnerAction>> ListAsync(CancellationToken ct = default);

    Task<RunnerAction?> GetAsync(Guid id, CancellationToken ct = default);

    Task<RunnerAction> AddAsync(string label, string commandId, string? icon = null, CancellationToken ct = default);

    Task<bool> RemoveAsync(Guid id, CancellationToken ct = default);
}
