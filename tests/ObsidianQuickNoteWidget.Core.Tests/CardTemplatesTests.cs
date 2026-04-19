using System.Text.Json;
using ObsidianQuickNoteWidget.Core.AdaptiveCards;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

/// <summary>
/// Structural tests for <see cref="CardTemplates"/>: each size selection must
/// resolve to a *distinct* embedded template, verified by looking for a field
/// present in only that template (not by substring-matching "AdaptiveCard",
/// which is in every template and would accept any wrong routing).
/// </summary>
public class CardTemplatesTests
{
    // Discriminators — each string below appears in ONLY one template. Chosen
    // from the template source so that a mutation swapping size→template
    // routing is guaranteed to fail.
    private const string SmallOnlyMarker = "Quick note title";          // small placeholder
    private const string MediumOnlyMarker = "Folder (type new or pick)"; // medium placeholder
    private const string LargeOnlyMarker = "toggleAdvanced";             // large-only verb
    private const string RecentsOnlyMarker = "openVault";                // RecentNotes-only verb

    private static readonly (string Name, string Marker)[] AllTemplates =
    [
        (CardTemplates.SmallTemplate, SmallOnlyMarker),
        (CardTemplates.MediumTemplate, MediumOnlyMarker),
        (CardTemplates.LargeTemplate, LargeOnlyMarker),
        (CardTemplates.RecentNotesTemplate, RecentsOnlyMarker),
    ];

    [Theory]
    [InlineData("small", SmallOnlyMarker, MediumOnlyMarker, LargeOnlyMarker)]
    [InlineData("medium", MediumOnlyMarker, SmallOnlyMarker, LargeOnlyMarker)]
    [InlineData("large", LargeOnlyMarker, SmallOnlyMarker, MediumOnlyMarker)]
    public void LoadForSize_PicksCorrectTemplate(string size, string mustContain, string mustNotContain1, string mustNotContain2)
    {
        var json = CardTemplates.LoadForSize(size);
        Assert.Contains(mustContain, json);
        Assert.DoesNotContain(mustNotContain1, json);
        Assert.DoesNotContain(mustNotContain2, json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("xl")]
    public void LoadForSize_UnknownSize_FallsBackToMedium(string? size)
    {
        var json = CardTemplates.LoadForSize(size!);
        Assert.Contains(MediumOnlyMarker, json);
        Assert.DoesNotContain(LargeOnlyMarker, json);
        Assert.DoesNotContain(SmallOnlyMarker, json);
    }

    [Fact]
    public void AllTemplates_AreValidAdaptiveCardJson()
    {
        foreach (var (name, _) in AllTemplates)
        {
            var json = CardTemplates.Load(name);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("AdaptiveCard", doc.RootElement.GetProperty("type").GetString());
            Assert.True(doc.RootElement.TryGetProperty("body", out var body));
            Assert.Equal(JsonValueKind.Array, body.ValueKind);
        }
    }

    [Fact]
    public void DiscriminatorMarkers_AreActuallyUnique()
    {
        // Guard the guard: if someone copies a marker between templates, our
        // routing tests silently degrade. Re-verify uniqueness at runtime.
        foreach (var (name, marker) in AllTemplates)
        {
            foreach (var (otherName, _) in AllTemplates)
            {
                if (otherName == name) continue;
                var otherJson = CardTemplates.Load(otherName);
                Assert.DoesNotContain(marker, otherJson);
            }
        }
    }

    [Fact]
    public void AllTemplates_DeclareSchemaVersion_1_5()
    {
        foreach (var name in new[]
        {
            CardTemplates.SmallTemplate,
            CardTemplates.MediumTemplate,
            CardTemplates.LargeTemplate,
            CardTemplates.RecentNotesTemplate,
            CardTemplates.CliMissingTemplate,
        })
        {
            var json = CardTemplates.Load(name);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("1.5", doc.RootElement.GetProperty("version").GetString());
        }
    }

    [Fact]
    public void AllTemplates_EverySubmitOrExecuteAction_BindsWidgetId()
    {
        // Spec: every Action.Submit / Action.Execute (including inline selectAction
        // blocks on containers / textblocks) must carry `data.widgetId` bound to a
        // `${widgetId}` (or `${$root.widgetId}`) template token so the provider can
        // route the event back to the originating widget session.
        foreach (var name in new[]
        {
            CardTemplates.SmallTemplate,
            CardTemplates.MediumTemplate,
            CardTemplates.LargeTemplate,
            CardTemplates.RecentNotesTemplate,
            CardTemplates.CliMissingTemplate,
        })
        {
            var json = CardTemplates.Load(name);
            using var doc = JsonDocument.Parse(json);
            var offenders = new List<string>();
            WalkActions(doc.RootElement, name, offenders);
            Assert.True(offenders.Count == 0,
                $"Actions in '{name}' missing `data.widgetId` binding: {string.Join("; ", offenders)}");
        }
    }

    private static void WalkActions(JsonElement el, string templateName, List<string> offenders)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                if (el.TryGetProperty("type", out var type) &&
                    type.ValueKind == JsonValueKind.String)
                {
                    var t = type.GetString();
                    if (t == "Action.Submit" || t == "Action.Execute")
                    {
                        var verb = el.TryGetProperty("verb", out var v) ? v.GetString() : "(no-verb)";
                        if (!el.TryGetProperty("data", out var data) ||
                            data.ValueKind != JsonValueKind.Object ||
                            !data.TryGetProperty("widgetId", out var wid) ||
                            wid.ValueKind != JsonValueKind.String ||
                            !(wid.GetString() == "${widgetId}" || wid.GetString() == "${$root.widgetId}"))
                        {
                            offenders.Add($"{t}(verb={verb})");
                        }
                    }
                }
                foreach (var prop in el.EnumerateObject())
                    WalkActions(prop.Value, templateName, offenders);
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    WalkActions(item, templateName, offenders);
                break;
        }
    }
}
