using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Logging;

namespace ObsidianQuickNoteWidget.Core.Notes;

/// <summary>
/// Orchestrates: sanitize title → validate folder → build body (templates + frontmatter)
/// → resolve duplicate filename → invoke CLI → optionally open.
/// Switches to `obsidian daily:append` when <see cref="NoteRequest.AppendToDaily"/> is true.
/// </summary>
public sealed class NoteCreationService
{
    private readonly IObsidianCli _cli;
    private readonly ILog _log;
    private readonly TimeProvider _time;

    public NoteCreationService(IObsidianCli cli, ILog? log = null, TimeProvider? time = null)
    {
        _cli = cli;
        _log = log ?? NullLog.Instance;
        _time = time ?? TimeProvider.System;
    }

    public async Task<NoteCreationResult> CreateAsync(NoteRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        if (!_cli.IsAvailable)
            return new NoteCreationResult(NoteCreationStatus.CliUnavailable, null,
                "Obsidian CLI not found. Enable it in Obsidian → Settings → General → Command Line Interface.");

        if (req.AppendToDaily)
        {
            var text = (req.Body ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text) && !string.IsNullOrWhiteSpace(req.Title))
                text = req.Title.Trim();

            if (string.IsNullOrEmpty(text))
                return new NoteCreationResult(NoteCreationStatus.InvalidTitle, null, "Nothing to append");

            var ok = await _cli.AppendDailyAsync(text, ct).ConfigureAwait(false);
            return ok
                ? new NoteCreationResult(NoteCreationStatus.AppendedToDaily, null, "Appended to today's daily note")
                : new NoteCreationResult(NoteCreationStatus.CreationFailed, null, "obsidian daily:append failed");
        }

        var now = _time.GetLocalNow();
        var rawTitle = req.AutoDatePrefix ? $"{now:yyyy-MM-dd} {req.Title}" : req.Title;

        var stem = FilenameSanitizer.Sanitize(rawTitle);
        if (stem is null)
            return new NoteCreationResult(NoteCreationStatus.InvalidTitle, null, "Title is empty after sanitization");

        var folderCheck = FolderPathValidator.Validate(req.Folder);
        if (!folderCheck.IsValid)
            return new NoteCreationResult(NoteCreationStatus.InvalidFolder, null, folderCheck.Error);

        var folder = folderCheck.NormalizedPath ?? string.Empty;

        var tags = FrontmatterBuilder.ParseTagsCsv(req.TagsCsv).ToList();
        foreach (var t in NoteTemplates.Tags(req.Template))
            if (!tags.Any(existing => existing.Equals(t, StringComparison.OrdinalIgnoreCase)))
                tags.Add(t);

        var bodyContent = BuildBody(req, tags, now);

        var vaultRoot = await _cli.GetVaultRootAsync(ct).ConfigureAwait(false);
        bool Exists(string vaultRelPath)
        {
            if (vaultRoot is null) return false;
            var full = Path.Combine(vaultRoot, vaultRelPath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full);
        }

        var targetPath = DuplicateFilenameResolver.ResolveUnique(folder, stem, ".md", Exists);

        var created = await _cli.CreateNoteAsync(targetPath, bodyContent, ct).ConfigureAwait(false);
        if (created is null)
            return new NoteCreationResult(NoteCreationStatus.CreationFailed, null,
                "Obsidian CLI failed to create the note. Check that Obsidian is running and the CLI is enabled.");

        if (req.OpenAfterCreate)
        {
            _ = _cli.OpenNoteAsync(created, ct);
        }

        _log.Info($"Created {created}");
        return new NoteCreationResult(NoteCreationStatus.Created, created, $"Created {created}");
    }

    private static string BuildBody(NoteRequest req, IReadOnlyList<string> tags, DateTimeOffset now)
    {
        var seeded = NoteTemplates.SeedBody(req.Template, now);
        var user = string.IsNullOrEmpty(req.Body) ? string.Empty : req.Body;

        string combined;
        if (seeded.Length == 0) combined = user;
        else if (user.Length == 0) combined = seeded;
        else combined = seeded.TrimEnd() + "\n\n" + user;

        return FrontmatterBuilder.Build(tags, now, combined);
    }
}
