namespace ObsidianQuickNoteWidget.Core.Notes;

public enum NoteTemplate
{
    Blank,
    Daily,
    Meeting,
    Book,
    Idea,
}

public static class NoteTemplates
{
    public static string SeedBody(NoteTemplate template, DateTimeOffset now) => template switch
    {
        NoteTemplate.Blank => string.Empty,
        NoteTemplate.Daily => $"# {now:yyyy-MM-dd}\n\n## Notes\n\n## Tasks\n- [ ] \n\n## Reflections\n\n",
        NoteTemplate.Meeting => $"# Meeting\n\n- **Date:** {now:yyyy-MM-dd HH:mm}\n- **Attendees:** \n- **Agenda:** \n\n## Notes\n\n## Action items\n- [ ] \n\n",
        NoteTemplate.Book => "# Book\n\n- **Author:** \n- **Started:** \n- **Finished:** \n- **Rating:** \n\n## Summary\n\n## Highlights\n\n",
        NoteTemplate.Idea => "# Idea\n\n## Problem\n\n## Sketch\n\n## Next steps\n- [ ] \n\n",
        _ => string.Empty,
    };

    public static IReadOnlyList<string> Tags(NoteTemplate template) => template switch
    {
        NoteTemplate.Daily => new[] { "daily" },
        NoteTemplate.Meeting => new[] { "meeting" },
        NoteTemplate.Book => new[] { "book" },
        NoteTemplate.Idea => new[] { "idea" },
        _ => Array.Empty<string>(),
    };
}
