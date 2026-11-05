using System.Diagnostics;
using JsonNotepad.App.Infrastructure;
using JsonNotepad.Controls.Icons;
using JsonNotepad.Controls.Tree;
using JsonNotepad.Core.Editing;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Paths;

namespace JsonNotepad.App.Forms;

/// <summary>Command definitions, menus, toolbar and the handlers behind them.</summary>
internal sealed partial class MainForm
{
    private const string ClipboardNameFormat = "JsonNotepad.MemberName";

    private static readonly JNodeKind[] InsertableKinds =
        { JNodeKind.Object, JNodeKind.Array, JNodeKind.String, JNodeKind.Number, JNodeKind.Boolean, JNodeKind.Null };

    private JNode? Selected => _tree.SelectedNode;

    private bool CanEdit => !_busy && Selected is not null && _tabs.SelectedIndex == TreeTab;

    private void RegisterActions()
    {
        AppAction Add(string id, string text, Action run, Func<bool>? can = null, Keys keys = Keys.None, string? glyph = null, string? display = null, Func<bool>? isChecked = null) =>
            _actions.Add(new AppAction(id, text, run) { CanExecute = can, Shortcut = keys, Glyph = glyph, ShortcutDisplay = display, IsChecked = isChecked });

        bool NotBusy() => !_busy;

        // File
        Add("file.new", "&New", () => _ = NewDocumentAsync(), NotBusy, Keys.Control | Keys.N, GlyphIcons.New);
        Add("file.open", "&Open…", () => _ = OpenWithDialogAsync(), NotBusy, Keys.Control | Keys.O, GlyphIcons.Open);
        Add("file.reload", "&Reload", () => _ = ReloadAsync(), () => NotBusy() && !_session.IsUntitled, Keys.Control | Keys.R, GlyphIcons.Refresh);
        Add("file.save", "&Save", () => _ = SaveAsync(saveAs: false), () => NotBusy() && !_saving, Keys.Control | Keys.S, GlyphIcons.Save);
        Add("file.saveAs", "Save &As…", () => _ = SaveAsync(saveAs: true), () => NotBusy() && !_saving, Keys.Control | Keys.Shift | Keys.S, GlyphIcons.SaveAs);
        Add("file.newWindow", "New &Window", OpenNewWindow, null, Keys.Control | Keys.Shift | Keys.N, GlyphIcons.NewWindow);
        Add("file.exportErrors", "Export &Errors…", ExportErrors, () => _errors.Diagnostics.Count > 0);
        Add("file.exit", "E&xit", Close, null, Keys.None, null, "Alt+F4");

        // Edit
        Add("edit.undo", "&Undo", Undo, () => NotBusy() && _session.UndoManager.CanUndo, Keys.Control | Keys.Z, GlyphIcons.Undo);
        Add("edit.redo", "&Redo", Redo, () => NotBusy() && _session.UndoManager.CanRedo, Keys.Control | Keys.Y, GlyphIcons.Redo);
        Add("edit.cut", "Cu&t", Cut, () => CanEdit && NodeEditor.CanDelete(Selected), Keys.Control | Keys.X, GlyphIcons.Cut);
        Add("edit.copy", "&Copy", Copy, () => NotBusy() && (Selected is not null || _tabs.SelectedIndex == TextTab), Keys.Control | Keys.C, GlyphIcons.Copy);
        Add("edit.paste", "&Paste", () => Paste(asChild: false), () => CanEdit, Keys.Control | Keys.V, GlyphIcons.Paste);
        Add("edit.pasteChild", "Paste as C&hild", () => Paste(asChild: true), () => CanEdit && Selected!.IsContainer, Keys.Control | Keys.Shift | Keys.V);
        Add("edit.delete", "&Delete", Delete, () => CanEdit && NodeEditor.CanDelete(Selected), Keys.Delete, GlyphIcons.Delete);
        Add("edit.rename", "Re&name", () => _tree.BeginEdit(TreePane.Names), () => CanEdit && NodeEditor.CanRename(Selected), Keys.F2, GlyphIcons.Rename);
        Add("edit.value", "Edit &Value", () => _tree.BeginEdit(TreePane.Values), () => CanEdit && NodeEditor.CanEditValue(Selected), Keys.None, null, "Enter");
        Add("edit.json", "Edit as &JSON…", EditAsJson, () => CanEdit, Keys.Control | Keys.E);
        Add("edit.duplicate", "D&uplicate", Duplicate, () => CanEdit && NodeEditor.CanDelete(Selected), Keys.Control | Keys.D, GlyphIcons.Duplicate);
        foreach (var kind in InsertableKinds)
        {
            var k = kind;
            Add($"change.{k}", k.DisplayName(), () => ChangeKind(k), () => CanEdit && Selected!.Kind != k, isChecked: () => Selected?.Kind == k);
        }
        Add("nudge.up", "Nudge &Up", () => Nudge(NudgeDirection.Up), () => CanEdit && NodeEditor.CanNudge(Selected, NudgeDirection.Up), Keys.Control | Keys.Shift | Keys.Up, GlyphIcons.Up);
        Add("nudge.down", "Nudge &Down", () => Nudge(NudgeDirection.Down), () => CanEdit && NodeEditor.CanNudge(Selected, NudgeDirection.Down), Keys.Control | Keys.Shift | Keys.Down, GlyphIcons.Down);
        Add("nudge.left", "Nudge &Left (outdent)", () => Nudge(NudgeDirection.Left), () => CanEdit && NodeEditor.CanNudge(Selected, NudgeDirection.Left), Keys.Control | Keys.Shift | Keys.Left, GlyphIcons.Left);
        Add("nudge.right", "Nudge &Right (indent)", () => Nudge(NudgeDirection.Right), () => CanEdit && NodeEditor.CanNudge(Selected, NudgeDirection.Right), Keys.Control | Keys.Shift | Keys.Right, GlyphIcons.Right);
        Add("edit.sort", "S&ort Properties", () => Sort(recursive: false), () => CanEdit && Selected!.Kind == JNodeKind.Object, Keys.None, GlyphIcons.Sort);
        Add("edit.sortDeep", "Sort Properties (&Recursive)", () => Sort(recursive: true), () => CanEdit && Selected!.IsContainer);
        Add("edit.copyPath", "Copy &Path", CopyPath, () => NotBusy() && Selected is not null, Keys.Control | Keys.Shift | Keys.C);
        Add("edit.gotoPath", "&Go to Path…", GoToPath, NotBusy, Keys.Control | Keys.G);
        Add("edit.find", "&Find…", () => ShowFind(replace: false), NotBusy, Keys.Control | Keys.F, GlyphIcons.Find);
        Add("edit.replace", "R&eplace…", () => ShowFind(replace: true), NotBusy, Keys.Control | Keys.H);
        Add("edit.findNext", "Find &Next", () => FindAgain(backwards: false), NotBusy, Keys.F3);
        Add("edit.findPrev", "Find Pre&vious", () => FindAgain(backwards: true), NotBusy, Keys.Shift | Keys.F3);
        Add("edit.incremental", "&Incremental Search", () =>
        {
            _tabs.SelectedIndex = TreeTab;
            _tree.StartIncrementalSearch();
        }, NotBusy, Keys.Control | Keys.I);

        // Insert
        foreach (var kind in InsertableKinds)
        {
            var k = kind;
            Add($"insert.{k}.Before", "&Before", () => Insert(k, InsertPosition.Before), () => CanEdit && NodeEditor.CanInsert(Selected, InsertPosition.Before));
            Add($"insert.{k}.After", "&After", () => Insert(k, InsertPosition.After), () => CanEdit && NodeEditor.CanInsert(Selected, InsertPosition.After));
            Add($"insert.{k}.Child", "&Child", () => Insert(k, InsertPosition.Child), () => CanEdit && NodeEditor.CanInsert(Selected, InsertPosition.Child));
        }
        Add("insert.sameAfter", "Insert &Sibling", InsertSibling, () => CanEdit, Keys.Control | Keys.Enter);
        Add("insert.stringChild", "Insert C&hild", () => Insert(JNodeKind.String, InsertPosition.Child), () => CanEdit && Selected!.IsContainer, Keys.Control | Keys.Shift | Keys.Enter);

        // View
        Add("view.expandAll", "&Expand All", ExpandAll, () => NotBusy(), Keys.Control | Keys.Shift | Keys.Add, GlyphIcons.ExpandAll);
        Add("view.collapseAll", "&Collapse All", () => _tree.CollapseAll(), NotBusy, Keys.Control | Keys.Shift | Keys.Subtract, GlyphIcons.CollapseAll);
        Add("view.expandSubtree", "Expand Su&btree", () => _tree.ExpandAll(Selected), () => NotBusy() && Selected is { HasChildren: true }, Keys.None, null, "Num *");
        Add("view.tree", "&Tree View", () => _tabs.SelectedIndex = TreeTab, null, Keys.Control | Keys.D1, null, null, () => _tabs.SelectedIndex == TreeTab);
        Add("view.text", "Te&xt View", () => _tabs.SelectedIndex = TextTab, null, Keys.Control | Keys.D2, null, null, () => _tabs.SelectedIndex == TextTab);
        Add("view.errors", "E&rror List", ToggleErrorList, null, Keys.Control | Keys.D3, null, null, () => !_split.Panel2Collapsed);
        Add("view.toolbar", "Tool&bar", () =>
        {
            _settings.ShowToolbar = !_settings.ShowToolbar;
            _toolbar.Visible = _settings.ShowToolbar;
            RequestActionUpdate();
        }, null, Keys.None, null, null, () => _settings.ShowToolbar);
        Add("view.status", "&Status Bar", () =>
        {
            _settings.ShowStatusBar = !_settings.ShowStatusBar;
            _statusStrip.Visible = _settings.ShowStatusBar;
            RequestActionUpdate();
        }, null, Keys.None, null, null, () => _settings.ShowStatusBar);
        Add("view.stats", "St&atistics…", () => _ = ShowStatisticsAsync(), NotBusy, Keys.None, GlyphIcons.Statistics);
        Add("view.options", "&Options…", ShowOptions, NotBusy, Keys.None, GlyphIcons.Settings);

        // Tools
        Add("tools.validate", "&Validate", () => ScheduleChecks(immediate: true, force: true), NotBusy, Keys.F5, GlyphIcons.Validate);
        Add("tools.schema", "Assign &Schema…", AssignSchema, NotBusy);
        Add("tools.clearSchema", "&Clear Schema", ClearSchema, () => _schemaPath is not null);
        Add("tools.nextError", "&Next Error", NextError, () => _errors.Diagnostics.Count > 0, Keys.F4);
        Add("format.preserve", "&Preserve Original", () => SetOutputStyle(OutputStyle.Preserve), null, Keys.None, null, null, () => _outputStyle == OutputStyle.Preserve);
        Add("format.indented", "&Indented", () => SetOutputStyle(OutputStyle.Indented), null, Keys.None, null, null, () => _outputStyle == OutputStyle.Indented);
        Add("format.compact", "&Compact (minified)", () => SetOutputStyle(OutputStyle.Compact), null, Keys.None, null, null, () => _outputStyle == OutputStyle.Compact);

        // Help
        Add("help.shortcuts", "&Keyboard Shortcuts", ShowShortcuts, null, Keys.F1, GlyphIcons.Help);
        Add("help.github", "JsonNotepad on &GitHub", () => AppInfo.OpenUrl(AppInfo.RepositoryUrl));
        Add("help.issue", "&Report a Problem", () => AppInfo.OpenUrl(AppInfo.RepositoryUrl + "/issues/new"));
        Add("help.logs", "Open &Log Folder", () =>
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            AppInfo.OpenUrl(AppPaths.LogDirectory);
        });
        Add("help.about", "&About JsonNotepad", () =>
        {
            using var about = new AboutForm();
            about.ShowDialog(this);
        });
    }

    private void BuildMenus()
    {
        ToolStripMenuItem Top(string text, params ToolStripItem[] items)
        {
            var m = new ToolStripMenuItem(text);
            m.DropDownItems.AddRange(items);
            return m;
        }
        ToolStripMenuItem M(string id) => _actions.Menu(id);
        ToolStripSeparator Sep() => new();

        _recentMenu = new ToolStripMenuItem("Recent &Files");
        RebuildRecentMenu();

        _menu.Items.Add(Top("&File", M("file.new"), M("file.open"), M("file.reload"), Sep(), M("file.save"), M("file.saveAs"), Sep(),
            M("file.newWindow"), M("file.exportErrors"), Sep(), _recentMenu, Sep(), M("file.exit")));

        _menu.Items.Add(Top("&Edit", M("edit.undo"), M("edit.redo"), Sep(), M("edit.cut"), M("edit.copy"), M("edit.paste"), M("edit.pasteChild"), M("edit.delete"), Sep(),
            M("edit.rename"), M("edit.value"), M("edit.json"), M("edit.duplicate"), BuildChangeToMenu(), BuildNudgeMenu(), M("edit.sort"), M("edit.sortDeep"), Sep(),
            M("edit.copyPath"), M("edit.gotoPath"), Sep(), M("edit.find"), M("edit.replace"), M("edit.findNext"), M("edit.findPrev"), M("edit.incremental")));

        _menu.Items.Add(Top("&View", M("view.expandAll"), M("view.collapseAll"), M("view.expandSubtree"), Sep(), M("view.tree"), M("view.text"), M("view.errors"), Sep(),
            M("view.toolbar"), M("view.status"), Sep(), M("view.stats"), M("view.options")));

        var insert = BuildInsertMenu("&Insert");
        insert.DropDownItems.Add(Sep());
        insert.DropDownItems.Add(M("insert.sameAfter"));
        insert.DropDownItems.Add(M("insert.stringChild"));
        _menu.Items.Add(insert);

        var output = new ToolStripMenuItem("&Output Format");
        output.DropDownItems.AddRange(new ToolStripItem[] { M("format.preserve"), M("format.indented"), M("format.compact") });
        _menu.Items.Add(Top("&Tools", M("tools.validate"), M("tools.schema"), M("tools.clearSchema"), M("tools.nextError"), Sep(), output));

        _menu.Items.Add(Top("&Help", M("help.shortcuts"), Sep(), M("help.github"), M("help.issue"), M("help.logs"), Sep(), M("help.about")));
    }

    private ToolStripMenuItem BuildInsertMenu(string text)
    {
        var insert = new ToolStripMenuItem(text);
        foreach (var kind in InsertableKinds)
        {
            var sub = new ToolStripMenuItem(kind.DisplayName());
            sub.DropDownItems.AddRange(new ToolStripItem[]
            {
                _actions.Menu($"insert.{kind}.Before"), _actions.Menu($"insert.{kind}.After"), _actions.Menu($"insert.{kind}.Child"),
            });
            insert.DropDownItems.Add(sub);
        }
        return insert;
    }

    private ToolStripMenuItem BuildChangeToMenu()
    {
        var change = new ToolStripMenuItem("C&hange To");
        foreach (var kind in InsertableKinds)
        {
            change.DropDownItems.Add(_actions.Menu($"change.{kind}"));
        }
        return change;
    }

    private ToolStripMenuItem BuildNudgeMenu()
    {
        var nudge = new ToolStripMenuItem("Nud&ge");
        nudge.DropDownItems.AddRange(new ToolStripItem[] { _actions.Menu("nudge.up"), _actions.Menu("nudge.down"), _actions.Menu("nudge.left"), _actions.Menu("nudge.right") });
        return nudge;
    }

    private void BuildToolbar()
    {
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.ImageScalingSize = new Size(LogicalToDeviceUnits(16), LogicalToDeviceUnits(16));
        _toolbar.Padding = new Padding(LogicalToDeviceUnits(4), 2, LogicalToDeviceUnits(4), 2);
        _toolbar.CanOverflow = true;
        ToolStripButton B(string id) => _actions.Button(id);
        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            B("file.new"), B("file.open"), B("file.save"), new ToolStripSeparator(),
            B("edit.undo"), B("edit.redo"), new ToolStripSeparator(),
            B("edit.cut"), B("edit.copy"), B("edit.paste"), B("edit.delete"), new ToolStripSeparator(),
            B("nudge.up"), B("nudge.down"), B("nudge.left"), B("nudge.right"), new ToolStripSeparator(),
            B("view.expandAll"), B("view.collapseAll"), new ToolStripSeparator(),
            B("edit.find"), B("tools.validate"), new ToolStripSeparator(),
            new ToolStripLabel("Location:"), _location,
        });
        _location.AutoSize = false;
        _location.DropDownStyle = ComboBoxStyle.DropDown;
        _location.FlatStyle = FlatStyle.System;
        _location.ToolTipText = "File location — type a path and press Enter, or pick a recent file";
        _location.DropDown += (_, _) =>
        {
            _location.Items.Clear();
            foreach (var path in _recent.Items)
            {
                _location.Items.Add(path);
            }
        };
    }

    private void BuildContextMenu()
    {
        ToolStripMenuItem M(string id) => _actions.Menu(id);
        _treeMenu.Items.AddRange(new ToolStripItem[]
        {
            M("edit.cut"), M("edit.copy"), M("edit.paste"), M("edit.pasteChild"), M("edit.delete"), new ToolStripSeparator(),
            M("edit.rename"), M("edit.value"), M("edit.json"), M("edit.duplicate"), new ToolStripSeparator(),
            BuildInsertMenu("&Insert"), BuildChangeToMenu(), BuildNudgeMenu(), M("edit.sort"), new ToolStripSeparator(),
            M("edit.copyPath"), M("view.expandSubtree"),
        });
        _treeMenu.Opening += (_, _) => _actions.UpdateAll();
    }

    private void RebuildRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        int i = 1;
        foreach (var path in _recent.Items)
        {
            var p = path;
            var text = (i <= 9 ? $"&{i} " : "   ") + (p.Length > 90 ? "…" + p[^89..] : p);
            var item = new ToolStripMenuItem(text) { ToolTipText = p };
            item.Click += async (_, _) => await OpenFileAsync(p);
            _recentMenu.DropDownItems.Add(item);
            i++;
        }
        if (_recent.Items.Count > 0)
        {
            _recentMenu.DropDownItems.Add(new ToolStripSeparator());
            var clear = new ToolStripMenuItem("&Clear Recent Files");
            clear.Click += (_, _) => _recent.Clear();
            _recentMenu.DropDownItems.Add(clear);
        }
        _recentMenu.Enabled = _recent.Items.Count > 0;
    }

    private void UpdateUndoText()
    {
        var undo = _session.UndoManager.NextUndo;
        var redo = _session.UndoManager.NextRedo;
        foreach (var item in _actions["edit.undo"].Items.OfType<ToolStripMenuItem>())
        {
            item.Text = undo is null ? "&Undo" : $"&Undo {undo.Name}";
        }
        foreach (var item in _actions["edit.redo"].Items.OfType<ToolStripMenuItem>())
        {
            item.Text = redo is null ? "&Redo" : $"&Redo {redo.Name}";
        }
    }

    // ----- Edit handlers -------------------------------------------------------------------------

    /// <summary>Runs an edit and reports validation/state errors to the user instead of crashing.</summary>
    private void RunEdit(Action edit)
    {
        try
        {
            edit();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            System.Media.SystemSounds.Beep.Play();
            SetStatus(ex.Message);
        }
    }

    private void Undo()
    {
        if (_tree.IsEditing)
        {
            _tree.EditorTextBox.Undo();
            return;
        }
        var cmd = _session.UndoManager.Undo();
        if (cmd?.SelectionAfterUndo is { } node && _session.Document.Contains(node))
        {
            _tree.SelectedNode = node;
        }
        SetStatus(cmd is null ? string.Empty : $"Undid {cmd.Name}");
    }

    private void Redo()
    {
        var cmd = _session.UndoManager.Redo();
        if (cmd?.SelectionAfterDo is { } node && _session.Document.Contains(node))
        {
            _tree.SelectedNode = node;
        }
        SetStatus(cmd is null ? string.Empty : $"Redid {cmd.Name}");
    }

    private void Copy()
    {
        if (_tree.IsEditing)
        {
            _tree.EditorTextBox.Copy();
            return;
        }
        if (_tabs.SelectedIndex == TextTab)
        {
            _textView.CopySelection();
            return;
        }
        if (_errors.Focused && _errors.SelectedDiagnostic is { } d)
        {
            Clipboard.SetText($"{d.Severity}: {d.Message} ({d.Path ?? d.Line?.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
            return;
        }
        if (Selected is not { } node)
        {
            return;
        }
        var text = NodeEditor.ToClipboardText(node, CurrentFormatOptions() with { UseTabs = false });
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        if (node.Parent?.Kind == JNodeKind.Object && node.Name is not null)
        {
            data.SetData(ClipboardNameFormat, node.Name);
        }
        Clipboard.SetDataObject(data, copy: true);
        SetStatus($"Copied {JsonPath.Build(node)} ({text.Length:N0} characters)");
    }

    private void Cut()
    {
        if (_tree.IsEditing)
        {
            _tree.EditorTextBox.Cut();
            return;
        }
        Copy();
        Delete();
    }

    private void Paste(bool asChild)
    {
        if (_tree.IsEditing)
        {
            _tree.EditorTextBox.Paste();
            return;
        }
        if (Selected is not { } target)
        {
            return;
        }
        string text;
        string? name;
        try
        {
            text = Clipboard.GetText();
            name = Clipboard.GetData(ClipboardNameFormat) as string;
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            SetStatus($"The clipboard is busy: {ex.Message}");
            return;
        }
        var nodes = NodeEditor.ParseClipboardText(text, _settings.ToParseOptions(), out var error);
        if (nodes is null)
        {
            System.Media.SystemSounds.Beep.Play();
            SetStatus("The clipboard does not contain valid JSON" + (error is null ? "." : $": {error}"));
            return;
        }
        var position = asChild || target.IsRoot || (target.IsContainer && (target.Count == 0 || _tree.IsExpanded(target)))
            ? InsertPosition.Child
            : InsertPosition.After;
        if (!NodeEditor.CanInsert(target, position))
        {
            position = InsertPosition.After;
        }
        RunEdit(() =>
        {
            var last = _session.Editor.Paste(target, position, nodes, name);
            if (last is not null)
            {
                _tree.SelectedNode = last;
            }
        });
    }

    private void Delete()
    {
        if (_tree.IsEditing || Selected is not { } node || !NodeEditor.CanDelete(node))
        {
            return;
        }
        RunEdit(() => _tree.SelectedNode = _session.Editor.Delete(node));
    }

    private void Duplicate()
    {
        if (Selected is { } node)
        {
            RunEdit(() => _tree.SelectedNode = _session.Editor.Duplicate(node));
        }
    }

    private void Insert(JNodeKind kind, InsertPosition position)
    {
        if (Selected is not { } target)
        {
            return;
        }
        RunEdit(() =>
        {
            var node = _session.Editor.Insert(target, position, kind);
            _tree.SelectedNode = node;
            if (node.Parent?.Kind == JNodeKind.Object)
            {
                _tree.BeginEdit(TreePane.Names);
            }
            else if (NodeEditor.CanEditValue(node))
            {
                _tree.BeginEdit(TreePane.Values);
            }
        });
    }

    private void InsertSibling()
    {
        if (Selected is not { } node)
        {
            return;
        }
        if (node.IsRoot)
        {
            Insert(JNodeKind.String, InsertPosition.Child);
            return;
        }
        Insert(node.Kind, InsertPosition.After);
    }

    private void ChangeKind(JNodeKind kind)
    {
        if (Selected is { } node)
        {
            RunEdit(() => _tree.SelectedNode = _session.Editor.ChangeKind(node, kind));
        }
    }

    private void Nudge(NudgeDirection direction)
    {
        if (Selected is { } node)
        {
            RunEdit(() =>
            {
                _session.Editor.Nudge(node, direction);
                _tree.SelectedNode = node;
            });
        }
    }

    private void Sort(bool recursive)
    {
        if (Selected is { } node)
        {
            RunEdit(() =>
            {
                _session.Editor.SortProperties(node, recursive);
                _tree.SelectedNode = node;
            });
        }
    }

    private void EditAsJson()
    {
        if (Selected is not { } node)
        {
            return;
        }
        long count = node.DescendantsAndSelf().Take(500_001).LongCount();
        if (count > 500_000)
        {
            MessageBox.Show(this, "This subtree is too large to edit as text. Edit it in the tree instead.", AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var json = Core.IO.JsonDocumentWriter.ToJson(node, CurrentFormatOptions() with { Indented = true, UseTabs = false, TrailingNewLine = false });
        var options = _settings.ToParseOptions();
        var result = InputDialog.ShowMultiline(this, "Edit as JSON", $"Editing {JsonPath.Build(node)} — press Ctrl+Enter or OK to apply:", json, _settings.TextFont,
            text => Core.IO.JsonParser.TryParse(text, options, out _, out var error) ? null : error);
        if (result is null || result.Replace("\r\n", "\n", StringComparison.Ordinal) == json.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            return;
        }
        var edit = _session.Editor.ReplaceWithJson(node, result, options);
        if (edit.Success)
        {
            _tree.SelectedNode = edit.Node;
        }
        else
        {
            SetStatus(edit.Error ?? "Invalid JSON.");
        }
    }

    private void OnNodeEditCommitting(object? sender, NodeEditEventArgs e)
    {
        var result = e.Pane == TreePane.Names ? _session.Editor.Rename(e.Node, e.NewText) : _session.Editor.SetValue(e.Node, e.NewText);
        if (!result.Success)
        {
            e.Error = result.Error;
        }
        else
        {
            UpdateSelectionStatus();
        }
    }

    private void OnNodeDropped(object? sender, NodeDropEventArgs e)
    {
        RunEdit(() =>
        {
            if (e.Copy)
            {
                var copy = e.Node.DeepClone();
                _tree.SelectedNode = _session.Editor.InsertNode(e.Target, e.Position, copy, "Copy");
            }
            else if (_session.Editor.Move(e.Node, e.Target, e.Position))
            {
                _tree.SelectedNode = e.Node;
            }
        });
    }

    private void CopyPath()
    {
        if (Selected is { } node)
        {
            var path = JsonPath.Build(node);
            Clipboard.SetText(path);
            SetStatus($"Copied path {path}");
        }
    }

    private void GoToPath()
    {
        var current = Selected is null ? JsonPath.RootToken : JsonPath.Build(Selected);
        var root = _session.Document.Root;
        var path = InputDialog.Show(this, "Go to Path", "JSONPath (e.g. $.store.book[0].title):", current, text =>
        {
            if (!JsonPath.TryParse(text, out _, out var error))
            {
                return error;
            }
            return JsonPath.Resolve(root, text) is null ? "No node exists at that path." : null;
        });
        if (path is not null && JsonPath.Resolve(root, path) is { } node)
        {
            _tabs.SelectedIndex = TreeTab;
            _tree.SelectedNode = node;
            _tree.Focus();
        }
    }

    private void ExpandAll()
    {
        long nodes = _session.Document.Root.DescendantsAndSelf().Take(_settings.ExpandAllConfirmNodes + 1).LongCount();
        if (nodes > _settings.ExpandAllConfirmNodes &&
            MessageBox.Show(this, $"This document has more than {_settings.ExpandAllConfirmNodes:N0} nodes. Expanding everything may take a moment and use extra memory.\n\nExpand all anyway?",
                AppInfo.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        UseWaitCursor = true;
        try
        {
            _tree.ExpandAll();
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ToggleErrorList()
    {
        _split.Panel2Collapsed = !_split.Panel2Collapsed;
        _settings.ShowErrorList = !_split.Panel2Collapsed;
        RequestActionUpdate();
    }

    private void NextError()
    {
        if (_split.Panel2Collapsed)
        {
            ToggleErrorList();
        }
        if (_errors.SelectNext() is { } d)
        {
            NavigateTo(d);
            SetStatus(d.Message);
        }
    }

    private void SetOutputStyle(OutputStyle style)
    {
        _outputStyle = style;
        UpdateFormatStatus();
        InvalidateTextView();
        RequestActionUpdate();
    }

    private void ShowOptions()
    {
        using var dlg = new OptionsForm(_settings);
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        var r = dlg.Result;
        bool themeChanged = r.Theme != _settings.Theme;
        CopySettings(r, _settings);
        ApplySettings();
        ScheduleChecks(immediate: true);
        SaveSettingsNow();
        if (themeChanged)
        {
            MessageBox.Show(this, "The new theme will be applied the next time JsonNotepad starts.", AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static void CopySettings(Settings.AppSettings from, Settings.AppSettings to)
    {
        foreach (var p in typeof(Settings.AppSettings).GetProperties())
        {
            if (p.CanRead && p.CanWrite && p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false).Length == 0)
            {
                p.SetValue(to, p.GetValue(from));
            }
        }
    }

    private void ShowShortcuts()
    {
        var rows = _actions.All
            .Where(a => a.ShortcutText.Length > 0)
            .Select(a => (a.Text.Replace("&", string.Empty, StringComparison.Ordinal).TrimEnd('…'), a.ShortcutText))
            .Concat(new[]
            {
                (string.Empty, string.Empty),
                ("Tree: move selection", "Arrow keys, PgUp/PgDn, Home/End"),
                ("Tree: expand / collapse", "Right / Left, Num + / Num -"),
                ("Tree: switch name/value column", "Tab"),
                ("Tree: find by typing", "Type the start of a name or value"),
                ("Editor: commit / cancel", "Enter / Esc"),
                ("Editor: new line in a string", "Ctrl+Enter"),
                ("Tree: move node", "Drag and drop (Ctrl = copy)"),
                ("Text view: jump to node", "Double-click a line"),
            });
        using var form = new ReportForm("Keyboard Shortcuts", "Command", "Shortcut", rows, new Size(560, 640));
        form.ShowDialog(this);
    }

    private void OpenNewWindow()
    {
        if (Environment.ProcessPath is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        }
    }

    private void ExportErrors()
    {
        using var dlg = new SaveFileDialog { Filter = "Tab-separated values (*.tsv)|*.tsv|Text files (*.txt)|*.txt", FileName = Path.GetFileNameWithoutExtension(_session.DisplayName) + "-errors.tsv" };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        var lines = new List<string> { "Severity\tMessage\tLocation\tSource\tFile" };
        lines.AddRange(_errors.Diagnostics.Select(d => $"{d.Severity}\t{d.Message}\t{JsonNotepad.Controls.Errors.ErrorListView.FormatLocation(d)}\t{d.Source}\t{d.FileName}"));
        try
        {
            File.WriteAllLines(dlg.FileName, lines);
            SetStatus($"Exported {_errors.Diagnostics.Count:N0} entries to {dlg.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
