using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Logging;
using ObsidianQuickNoteWidget.Core.Notes;
using ObsidianQuickNoteWidget.Core.State;

namespace ObsidianQuickNoteTray;

/// <summary>
/// Compact quick-note popup form. Reuses the same <see cref="NoteCreationService"/>
/// as the widget provider so behavior is identical.
/// </summary>
internal sealed class QuickNoteForm : Form
{
    private const string StateKey = "tray";
    private readonly NoteCreationService _notes;
    private readonly IStateStore _store;

    private readonly TextBox _title = new() { PlaceholderText = "Title", Dock = DockStyle.Top };
    private readonly ComboBox _folder = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Top };
    private readonly TextBox _body = new() { Multiline = true, PlaceholderText = "Body (optional)", Dock = DockStyle.Fill, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _datePrefix = new() { Text = "Date prefix", AutoSize = true };
    private readonly CheckBox _openAfter = new() { Text = "Open after create", AutoSize = true };
    private readonly CheckBox _appendDaily = new() { Text = "Append to daily", AutoSize = true };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 2, 6, 2) };
    private readonly Button _create = new() { Text = "Create", Dock = DockStyle.Bottom, Height = 32 };

    public QuickNoteForm(NoteCreationService notes, IStateStore store, IObsidianCli cli)
    {
        _notes = notes;
        _store = store;

        Text = "Obsidian Quick Note";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(480, 420);
        MinimumSize = new Size(400, 360);
        KeyPreview = true;

        var toggles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(4) };
        toggles.Controls.Add(_datePrefix);
        toggles.Controls.Add(_openAfter);
        toggles.Controls.Add(_appendDaily);

        var container = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1 };
        container.Controls.Add(_body);

        Controls.Add(container);
        Controls.Add(toggles);
        Controls.Add(_folder);
        Controls.Add(_title);
        Controls.Add(_create);
        Controls.Add(_status);

        _create.Click += async (_, _) => await CreateAsync();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Hide();
            if (e.Control && e.KeyCode == Keys.Enter) _ = CreateAsync();
        };
        FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };

        LoadState();

        // Populate folders asynchronously
        _ = Task.Run(async () =>
        {
            var folders = await cli.ListFoldersAsync();
            BeginInvoke(() =>
            {
                _folder.Items.Clear();
                _folder.Items.Add(string.Empty);
                foreach (var f in folders) _folder.Items.Add(f);
            });
        });
    }

    public void Focus(string? seedBody = null)
    {
        _title.Text = string.Empty;
        _body.Text = seedBody ?? string.Empty;
        _status.Text = string.Empty;
        if (!Visible) Show();
        else Activate();
        TopMost = true; TopMost = false;
        _title.Focus();
    }

    private void LoadState()
    {
        var s = _store.Get(StateKey);
        _folder.Text = s.LastFolder;
        _datePrefix.Checked = s.AutoDatePrefix;
        _openAfter.Checked = s.OpenAfterCreate;
        _appendDaily.Checked = s.AppendToDaily;
    }

    private void SaveState()
    {
        var s = _store.Get(StateKey);
        s.WidgetId = StateKey;
        s.LastFolder = _folder.Text;
        s.AutoDatePrefix = _datePrefix.Checked;
        s.OpenAfterCreate = _openAfter.Checked;
        s.AppendToDaily = _appendDaily.Checked;
        _store.Save(s);
    }

    private async Task CreateAsync()
    {
        _create.Enabled = false;
        _status.Text = "Creating…";
        try
        {
            var req = new NoteRequest(
                Title: _title.Text,
                Folder: _folder.Text,
                Body: _body.Text,
                AutoDatePrefix: _datePrefix.Checked,
                OpenAfterCreate: _openAfter.Checked,
                AppendToDaily: _appendDaily.Checked);

            var r = await _notes.CreateAsync(req);
            SaveState();

            _status.Text = r.Message ?? r.Status.ToString();
            if (r.Status == NoteCreationStatus.Created || r.Status == NoteCreationStatus.AppendedToDaily)
            {
                _title.Clear(); _body.Clear();
                await Task.Delay(600);
                Hide();
            }
        }
        finally { _create.Enabled = true; }
    }
}
