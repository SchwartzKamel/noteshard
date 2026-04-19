using System.Reflection;

namespace ObsidianQuickNoteWidget.Core.AdaptiveCards;

/// <summary>Loads Adaptive Card JSON template strings from embedded resources.</summary>
public static class CardTemplates
{
    public const string SmallTemplate = "QuickNote.small.json";
    public const string MediumTemplate = "QuickNote.medium.json";
    public const string LargeTemplate = "QuickNote.large.json";
    public const string RecentNotesTemplate = "RecentNotes.json";
    public const string CliMissingTemplate = "CliMissing.json";

    public static string Load(string name)
    {
        var assembly = typeof(CardTemplates).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.Ordinal))
            ?? throw new FileNotFoundException($"Embedded card template '{name}' not found");

        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new FileNotFoundException($"Embedded card template stream for '{name}' not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string LoadForSize(string size) => size?.ToLowerInvariant() switch
    {
        "small" => Load(SmallTemplate),
        "large" => Load(LargeTemplate),
        _ => Load(MediumTemplate),
    };
}
