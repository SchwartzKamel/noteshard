using Microsoft.Windows.Widgets.Providers;

namespace ObsidianQuickNoteWidget.Providers;

/// <summary>
/// Test seam around the single static dependency on
/// <see cref="WidgetManager"/>.<see cref="WidgetManager.UpdateWidget"/>.
///
/// Production code uses <see cref="WidgetManagerUpdateSink"/>, which forwards
/// calls to <see cref="WidgetManager.GetDefault"/>. Tests inject a recording
/// fake so scenarios can assert how many times (and with what payload) a
/// <see cref="ObsidianWidgetProvider"/> attempted to push a card update.
///
/// Added in 1.0.0.9 after a typed-text-wipe regression slipped through the
/// suite because there was no way to observe <c>PushUpdate</c> calls from
/// background paths (<c>Activate</c>, context-change, timer refresh). See
/// <c>docs/contributing/testing.md</c> ("BDD scenario tests") for how to
/// write tests against this seam.
/// </summary>
internal interface IWidgetUpdateSink
{
    void Submit(WidgetUpdateRequestOptions options);
}

internal sealed class WidgetManagerUpdateSink : IWidgetUpdateSink
{
    public void Submit(WidgetUpdateRequestOptions options)
        => WidgetManager.GetDefault().UpdateWidget(options);
}
