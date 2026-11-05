using JsonNotepad.App.Infrastructure;
using JsonNotepad.App.Settings;
using JsonNotepad.Controls.Errors;
using JsonNotepad.Controls.Icons;
using JsonNotepad.Controls.Text;
using JsonNotepad.Controls.Theming;
using JsonNotepad.Controls.Tree;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Paths;
using JsonNotepad.Core.Services;
using JsonNotepad.Core.Session;
using JsonNotepad.Core.Validation;

namespace JsonNotepad.App.Forms;

/// <summary>
/// The main window. This partial holds construction, layout and window-level plumbing;
/// commands live in MainForm.Actions.cs, file handling in MainForm.Files.cs, background
/// work in MainForm.Background.cs and find/replace in MainForm.Find.cs.
/// </summary>
/// <remarks>
/// The form is deliberately thin: document state lives in <see cref="DocumentSession"/>,
/// editing rules in <c>NodeEditor</c>, rendering in the controls library. The form wires
/// them together and owns only UI concerns.
/// </remarks>
internal sealed partial class MainForm : Form
{
    private const string FilterJson = "JSON files (*.json;*.jsonc;*.geojson;*.har;*.webmanifest)|*.json;*.jsonc;*.geojson;*.har;*.webmanifest|All files (*.*)|*.*";

    private readonly AppSettings _settings;
    private readonly string? _startupFile;
    private readonly DelayedActions _delayed;
    private readonly DocumentSession _session;
    private readonly SchemaValidationService _validation = new();
    private readonly RecentFiles _recent;
    private readonly ActionRegistry _actions = new();
    private readonly Theme _theme;

    // Layout
    private readonly MenuStrip _menu = new();
    private readonly ToolStrip _toolbar = new();
    private readonly ToolStripComboBox _location = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusMessage = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusPath = new() { BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ToolStripStatusLabel _statusKind = new() { BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ToolStripStatusLabel _statusFormat = new() { BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ToolStripProgressBar _progress = new() { Visible = false, Maximum = 1000, Width = 160 };
    private readonly ToolStripStatusLabel _cancelLink = new() { Text = "Cancel", IsLink = true, Visible = false };
    private const int TreeTab = 0;
    private const int TextTab = 1;
    private readonly ViewSwitcher _tabs = new();
    private readonly JsonTreeView _tree = new() { Dock = DockStyle.Fill };
    private readonly JsonTextView _textView = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
    private readonly ErrorListView _errors = new() { Dock = DockStyle.Fill };
    private readonly Label _errorHeader = new() { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
    private readonly ContextMenuStrip _treeMenu = new();
    private ToolStripMenuItem _recentMenu = null!;
    private FindReplaceForm? _findForm;

    // State
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _checkCts;
    private CancellationTokenSource? _textCts;
    private JDocument? _boundDocument;
    private string? _schemaPath;
    private FormattedText? _formatted;
    private IReadOnlyList<Diagnostic> _fileErrors = Array.Empty<Diagnostic>();
    private IReadOnlyList<Diagnostic> _checkDiagnostics = Array.Empty<Diagnostic>();
    private OutputStyle _outputStyle = OutputStyle.Preserve;
    private bool _busy;
    private bool _saving;
    private bool _closeConfirmed;
    private bool _externalPromptOpen;
    private bool _actionsUpdatePending;

    private enum OutputStyle
    {
        Preserve,
        Indented,
        Compact,
    }

    public MainForm(AppSettings settings, string? startupFile)
    {
        _settings = settings;
        _startupFile = startupFile;
        _theme = Theme.FromApplication();
        _delayed = new DelayedActions(WindowsFormsSynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        _session = new DocumentSession(_delayed, settings.MaxUndoHistory);
        _recent = new RecentFiles(settings.RecentFilesCount, settings.RecentFiles);

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = SystemFonts.MessageBoxFont ?? Font;
        Icon = AppInfo.LoadIcon();
        Text = AppInfo.ProductName;
        MinimumSize = new Size(640, 420);
        AllowDrop = true;
        KeyPreview = false;

        RegisterActions();
        BuildLayout();
        ResumeLayout(performLayout: true);

        WireEvents();
        ApplySettings();
        BindDocument();
    }

    // ----- Layout ------------------------------------------------------------------------------

    private void BuildLayout()
    {
        int icon = LogicalToDeviceUnits(16);
        _actions.Configure(icon, _theme.IsDark ? Color.FromArgb(0xE0, 0xE0, 0xE0) : Color.FromArgb(0x20, 0x20, 0x20));

        BuildMenus();
        BuildToolbar();
        BuildContextMenu();

        _split.Panel1.Controls.Add(_tree);
        _split.Panel1.Controls.Add(_textView);
        _split.Panel1.Controls.Add(_tabs);
        _tabs.AddPage("Tree View", _tree);
        _tabs.AddPage("Text View", _textView);
        _tabs.Theme = _theme;
        _split.Panel2.Controls.Add(_errors);
        _split.Panel2.Controls.Add(_errorHeader);
        _split.SplitterWidth = LogicalToDeviceUnits(5);

        _statusStrip.Items.AddRange(new ToolStripItem[] { _statusMessage, _progress, _cancelLink, _statusPath, _statusKind, _statusFormat });
        _statusStrip.ShowItemToolTips = true;

        Controls.Add(_split);
        Controls.Add(_toolbar);
        Controls.Add(_menu);
        Controls.Add(_statusStrip);
        MainMenuStrip = _menu;

        _tree.ContextMenuStrip = _treeMenu;
        _tree.Theme = _theme;
        _textView.Theme = _theme;
        _errors.ApplyTheme(_theme);
        if (_theme.IsDark)
        {
            _statusStrip.BackColor = Color.FromArgb(0x2D, 0x2D, 0x30);
            _statusStrip.ForeColor = _theme.WindowText;
            _menu.ForeColor = _theme.WindowText;
            foreach (ToolStripItem item in _statusStrip.Items)
            {
                item.ForeColor = item == _cancelLink ? _theme.DropFeedback : _theme.WindowText;
            }
            _cancelLink.LinkColor = _theme.DropFeedback;
            _errorHeader.BackColor = Color.FromArgb(0x2D, 0x2D, 0x30);
            _errorHeader.ForeColor = _theme.WindowText;
        }
        else
        {
            _errorHeader.BackColor = SystemColors.ControlLight;
        }
        UpdateErrorList();
    }

    private void ApplySettings()
    {
        _tree.Font = _settings.TreeFont;
        _textView.Font = _settings.TextFont;
        _tree.ShowTreeLines = _settings.ShowTreeLines;
        _tree.NameColumnWidth = LogicalToDeviceUnits(Math.Max(80, _settings.NameColumnWidth));
        _toolbar.Visible = _settings.ShowToolbar;
        _statusStrip.Visible = _settings.ShowStatusBar;
        _split.Panel2Collapsed = !_settings.ShowErrorList;
        _session.UndoManager.MaxHistory = Math.Max(10, _settings.MaxUndoHistory);
        _recent.SetCapacity(_settings.RecentFilesCount);
        InvalidateTextView();
        RequestActionUpdate();
    }

    private void RestoreWindowPlacement()
    {
        if (_settings.Window is { } w && w.Width > 200 && w.Height > 150)
        {
            var bounds = new Rectangle(w.X, w.Y, w.Width, w.Height);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
            {
                // Never restore a window larger than the screen it lands on (DPI or monitor changes).
                var area = Screen.FromRectangle(bounds).WorkingArea;
                bounds.Width = Math.Min(bounds.Width, area.Width);
                bounds.Height = Math.Min(bounds.Height, area.Height);
                bounds.X = Math.Clamp(bounds.X, area.Left, area.Right - bounds.Width);
                bounds.Y = Math.Clamp(bounds.Y, area.Top, area.Bottom - bounds.Height);
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
            if (w.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            }
        }
        else
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(
                Math.Min(LogicalToDeviceUnits(1100), area.Width * 9 / 10),
                Math.Min(LogicalToDeviceUnits(760), area.Height * 9 / 10));
            StartPosition = FormStartPosition.CenterScreen;
        }
    }

    private void WireEvents()
    {
        _session.DocumentReplaced += (_, _) => BindDocument();
        _session.DirtyChanged += (_, _) => UpdateTitle();
        _session.FilePathChanged += (_, _) =>
        {
            UpdateTitle();
            UpdateLocation();
        };
        _session.UndoManager.StateChanged += (_, _) => RequestActionUpdate();
        _session.ExternalChange += OnExternalChange;
        _recent.Changed += (_, _) =>
        {
            RebuildRecentMenu();
            ScheduleSettingsSave();
        };

        _tree.SelectedNodeChanged += (_, _) =>
        {
            UpdateSelectionStatus();
            RequestActionUpdate();
        };
        _tree.ActivePaneChanged += (_, _) => RequestActionUpdate();
        _tree.NodeEditCommitting += OnNodeEditCommitting;
        _tree.NodeDropped += OnNodeDropped;
        _tree.FilesDropped += (_, e) => OpenDroppedFiles(e.Files);
        _tree.IncrementalSearchChanged += (_, text) =>
            SetStatus(text.Length == 0 ? string.Empty : text == "​" ? "Incremental search: start typing…" : $"Incremental search: {text}", transient: text.Length == 0);
        _tree.GotFocus += (_, _) => RequestActionUpdate();
        _tree.LostFocus += (_, _) => RequestActionUpdate();

        _textView.LineActivated += (_, line) => SyncTreeToTextLine(line);
        _textView.GotFocus += (_, _) => RequestActionUpdate();

        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex == TextTab)
            {
                _ = RefreshTextViewAsync(syncToSelection: true);
            }
            RequestActionUpdate();
        };

        _errors.DiagnosticActivated += (_, d) => NavigateTo(d);

        _split.SplitterMoved += (_, _) =>
        {
            if (!_split.Panel2Collapsed)
            {
                _settings.ErrorListHeight = DeviceToLogicalUnits(_split.Height - _split.SplitterDistance);
            }
        };

        _location.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !string.IsNullOrWhiteSpace(_location.Text))
            {
                e.Handled = e.SuppressKeyPress = true;
                await OpenFileAsync(_location.Text.Trim().Trim('"'));
            }
        };
        _location.SelectedIndexChanged += async (_, _) =>
        {
            if (_location.SelectedItem is string path && !string.Equals(path, _session.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                await OpenFileAsync(path);
            }
        };
        _toolbar.SizeChanged += (_, _) => ResizeLocationBox();

        _cancelLink.Click += (_, _) => _loadCts?.Cancel();

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            {
                OpenDroppedFiles(files);
            }
        };
    }

    private int DeviceToLogicalUnits(int value) => (int)Math.Round(value * 96.0 / DeviceDpi);

    private void ResizeLocationBox()
    {
        int used = 0;
        foreach (ToolStripItem item in _toolbar.Items)
        {
            if (item != _location && item.Visible)
            {
                used += item.Width + item.Margin.Horizontal;
            }
        }
        _location.Width = Math.Max(LogicalToDeviceUnits(150), _toolbar.DisplayRectangle.Width - used - LogicalToDeviceUnits(12));
    }

    // ----- Lifecycle -----------------------------------------------------------------------------

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // After DPI auto-scaling has run, so restored device-pixel bounds aren't scaled twice.
        RestoreWindowPlacement();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _split.SplitterDistance = Math.Max(_split.Panel1MinSize, _split.Height - LogicalToDeviceUnits(Math.Max(80, _settings.ErrorListHeight)));
        ResizeLocationBox();
        _tree.Focus();
        if (!string.IsNullOrEmpty(_startupFile))
        {
            await OpenFileAsync(_startupFile);
        }
        else
        {
            SetStatus("Ready. Open a JSON file (Ctrl+O), drop one here, or start editing the new document.", transient: true);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_closeConfirmed || e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            return;
        }
        if (_saving || _session.IsDirty || _busy)
        {
            e.Cancel = true;
            BeginInvoke(new Action(async () =>
            {
                _loadCts?.Cancel();
                while (_saving)
                {
                    await Task.Delay(50);
                }
                if (await ConfirmDiscardChangesAsync())
                {
                    _closeConfirmed = true;
                    Close();
                }
            }));
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveSettingsNow();
        _checkCts?.Cancel();
        _textCts?.Cancel();
        _delayed.Dispose();
        _session.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Keeps standard editing keys inside text boxes instead of triggering menu shortcuts.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (IsTextInputFocused() && IsTextEditingKey(keyData))
        {
            return false;
        }
        if (_actions.TryInvokeShortcut(keyData))
        {
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool IsTextInputFocused() =>
        _location.ComboBox?.Focused == true || ActiveControl is TextBoxBase || _tree.IsEditing;

    private static bool IsTextEditingKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var mods = keyData & Keys.Modifiers;
        return mods switch
        {
            Keys.Control => key is Keys.C or Keys.X or Keys.V or Keys.Z or Keys.Y or Keys.A or Keys.Back or Keys.Delete or Keys.Insert,
            Keys.Shift => key is Keys.Delete or Keys.Insert,
            Keys.None => key is Keys.Delete or Keys.Enter or Keys.F2,
            _ => false,
        };
    }

    // ----- Document binding & status ---------------------------------------------------------------

    private void BindDocument()
    {
        if (_boundDocument is not null)
        {
            _boundDocument.Changed -= OnDocumentChanged;
        }
        _boundDocument = _session.Document;
        _boundDocument.Changed += OnDocumentChanged;

        _tree.Document = _boundDocument;
        bool large = _session.FileSize > (long)_settings.LargeFileThresholdMB * 1024 * 1024;
        _tree.ExpandToDepth(large ? 0 : Math.Max(0, _settings.AutoExpandDepth));
        _tree.SelectedNode = _boundDocument.Root;

        _checkDiagnostics = _session.LoadDiagnostics;
        _formatted = null;
        InvalidateTextView();
        UpdateErrorList();
        UpdateTitle();
        UpdateLocation();
        UpdateFormatStatus();
        UpdateSelectionStatus();
        ScheduleChecks(immediate: true);
        RequestActionUpdate();
    }

    private void OnDocumentChanged(object? sender, JDocumentChangedEventArgs e)
    {
        InvalidateTextView();
        if (_settings.ValidateOnEdit)
        {
            ScheduleChecks(immediate: false);
        }
        if (e.Kind == JDocumentChangeKind.Replaced && e.Parent is null)
        {
            _tree.SelectedNode ??= _session.Document.Root;
        }
        RequestActionUpdate();
    }

    private void UpdateTitle()
    {
        var name = _session.DisplayName + (_session.IsDirty ? " *" : string.Empty);
        Text = $"{name} - {AppInfo.ProductName}";
    }

    private void UpdateLocation()
    {
        _location.Text = _session.FilePath ?? string.Empty;
    }

    private void UpdateFormatStatus()
    {
        var f = _session.Format;
        var indent = _outputStyle == OutputStyle.Compact || (f.WasCompact && _outputStyle == OutputStyle.Preserve) ? "Compact" : f.UseTabs ? "Tabs" : $"Spaces: {f.IndentSize}";
        _statusFormat.Text = $"{f.EncodingDisplayName}  ·  {f.NewLineDisplayName}  ·  {indent}";
    }

    private void UpdateSelectionStatus()
    {
        var node = _tree.SelectedNode;
        if (node is null)
        {
            _statusPath.Text = string.Empty;
            _statusKind.Text = string.Empty;
            return;
        }
        var path = JsonPath.Build(node);
        _statusPath.Text = path.Length > 120 ? "…" + path[^119..] : path;
        _statusPath.ToolTipText = path;
        _statusKind.Text = node.Kind switch
        {
            JNodeKind.Object => $"Object · {node.Count:N0} member{(node.Count == 1 ? string.Empty : "s")}",
            JNodeKind.Array => $"Array · {node.Count:N0} item{(node.Count == 1 ? string.Empty : "s")}",
            JNodeKind.String => $"String · {node.Value?.Length ?? 0:N0} chars",
            _ => node.Kind.DisplayName(),
        };
    }

    private void SetStatus(string message, bool transient = true)
    {
        _statusMessage.Text = message;
        if (transient && message.Length > 0)
        {
            _delayed.Start("clearStatus", () => _statusMessage.Text = string.Empty, TimeSpan.FromSeconds(15));
        }
    }

    private void SetBusy(bool busy, string? message = null, bool cancellable = false)
    {
        _busy = busy;
        _progress.Visible = busy;
        _progress.Value = 0;
        _progress.Style = ProgressBarStyle.Continuous;
        _cancelLink.Visible = busy && cancellable;
        _tree.Enabled = !busy;
        UseWaitCursor = busy;
        if (message is not null)
        {
            SetStatus(message, transient: !busy);
        }
        RequestActionUpdate();
    }

    private void UpdateErrorList()
    {
        var all = new List<Diagnostic>(_fileErrors.Count + _checkDiagnostics.Count);
        all.AddRange(_fileErrors);
        all.AddRange(_checkDiagnostics);
        _errors.Diagnostics = all;
        int errors = all.Count(d => d.Severity == DiagnosticSeverity.Error);
        int warnings = all.Count(d => d.Severity == DiagnosticSeverity.Warning);
        int infos = all.Count - errors - warnings;
        var schema = _schemaPath ?? (_lastResolvedSchema is null ? null : _lastResolvedSchema);
        _errorHeader.Text = $"Error List    {errors:N0} error{(errors == 1 ? string.Empty : "s")} · {warnings:N0} warning{(warnings == 1 ? string.Empty : "s")} · {infos:N0} message{(infos == 1 ? string.Empty : "s")}" +
                            (schema is null ? string.Empty : $"    Schema: {Path.GetFileName(schema)}");
    }

    private void NavigateTo(Diagnostic d)
    {
        if (d.Path is not null && JsonPath.Resolve(_session.Document.Root, d.Path) is { } node)
        {
            _tabs.SelectedIndex = TreeTab;
            _tree.SelectedNode = node;
            _tree.Focus();
        }
        else if (d.Line is long line && line > 0 && _fileErrors.Contains(d) && d.FileName is not null)
        {
            SetStatus($"Parse error at line {line}, column {d.Column}: {d.Message}", transient: false);
        }
    }

    private void RequestActionUpdate()
    {
        if (_actionsUpdatePending || !IsHandleCreated)
        {
            if (!IsHandleCreated)
            {
                _actions.UpdateAll();
            }
            return;
        }
        _actionsUpdatePending = true;
        BeginInvoke(new Action(() =>
        {
            _actionsUpdatePending = false;
            _actions.UpdateAll();
            UpdateUndoText();
        }));
    }

    // ----- Settings persistence ---------------------------------------------------------------------

    private void ScheduleSettingsSave() => _delayed.Start("saveSettings", SaveSettingsNow, TimeSpan.FromSeconds(2));

    private void SaveSettingsNow()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.Window = new WindowPlacement(b.X, b.Y, b.Width, b.Height, WindowState == FormWindowState.Maximized);
        _settings.NameColumnWidth = DeviceToLogicalUnits(_tree.NameColumnWidth);
        _settings.RecentFiles = _recent.Items.ToList();
        SettingsStore.Save(_settings);
    }

    private string? _lastResolvedSchema;

    private static Bitmap? Glyph(string glyph, int size, Color color) => GlyphIcons.Render(glyph, size, color);
}
