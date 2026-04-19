namespace ObsidianQuickNoteWidget.Core.State;

public interface IStateStore
{
    WidgetState Get(string widgetId);
    void Save(WidgetState state);
    void Delete(string widgetId);
}
