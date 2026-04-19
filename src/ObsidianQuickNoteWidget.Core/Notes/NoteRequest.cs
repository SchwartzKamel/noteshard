namespace ObsidianQuickNoteWidget.Core.Notes;

public sealed record NoteRequest(
    string Title,
    string? Folder,
    string? Body,
    string? TagsCsv = null,
    NoteTemplate Template = NoteTemplate.Blank,
    bool AutoDatePrefix = false,
    bool OpenAfterCreate = false,
    bool AppendToDaily = false);

public enum NoteCreationStatus
{
    Created,
    AppendedToDaily,
    InvalidTitle,
    InvalidFolder,
    CliUnavailable,
    CreationFailed,
}

public sealed record NoteCreationResult(
    NoteCreationStatus Status,
    string? VaultRelativePath,
    string? Message);
