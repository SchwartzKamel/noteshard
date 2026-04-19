namespace ObsidianQuickNoteWidget.Core.Runner;

/// <summary>
/// Input validation + normalization for <see cref="Models.RunnerAction"/>
/// fields. Rules are deliberately strict: the catalog is a trust boundary
/// between user-entered UI text and command dispatch.
/// </summary>
public static class RunnerActionValidator
{
    public const int MaxLabelLength = 64;
    public const int MaxCommandIdLength = 256;

    /// <summary>
    /// Validates and normalizes a <paramref name="label"/> / <paramref name="commandId"/>
    /// pair. Both strings are trimmed; the normalized values are returned.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown with a field-specific message if any rule is violated.
    /// </exception>
    public static (string Label, string CommandId) Normalize(string? label, string? commandId)
    {
        var normalizedLabel = NormalizeLabel(label);
        var normalizedCommandId = NormalizeCommandId(commandId);
        return (normalizedLabel, normalizedCommandId);
    }

    private static string NormalizeLabel(string? label)
    {
        if (label is null)
        {
            throw new ArgumentException("Label must not be null.", nameof(label));
        }

        var trimmed = label.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Label must not be empty or whitespace.", nameof(label));
        }

        if (trimmed.Length > MaxLabelLength)
        {
            throw new ArgumentException(
                $"Label must be {MaxLabelLength} characters or fewer (was {trimmed.Length}).",
                nameof(label));
        }

        if (ContainsControlChar(trimmed))
        {
            throw new ArgumentException("Label must not contain control characters.", nameof(label));
        }

        return trimmed;
    }

    private static string NormalizeCommandId(string? commandId)
    {
        if (commandId is null)
        {
            throw new ArgumentException("CommandId must not be null.", nameof(commandId));
        }

        var trimmed = commandId.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("CommandId must not be empty or whitespace.", nameof(commandId));
        }

        if (trimmed.Length > MaxCommandIdLength)
        {
            throw new ArgumentException(
                $"CommandId must be {MaxCommandIdLength} characters or fewer (was {trimmed.Length}).",
                nameof(commandId));
        }

        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                throw new ArgumentException(
                    "CommandId must not contain whitespace (expected a colon-separated identifier like 'workspace:new-tab').",
                    nameof(commandId));
            }
        }

        if (ContainsControlChar(trimmed))
        {
            throw new ArgumentException("CommandId must not contain control characters.", nameof(commandId));
        }

        return trimmed;
    }

    private static bool ContainsControlChar(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }
        return false;
    }
}
