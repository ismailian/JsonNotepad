using JsonNotepad.Controls.Icons;

namespace JsonNotepad.App.Infrastructure;

/// <summary>
/// One user command, defined once and surfaced in any number of menus, context menus
/// and toolbar buttons (Command pattern at the UI level). Enabled/checked state is
/// computed from delegates so every surface stays consistent.
/// </summary>
internal sealed class AppAction
{
    private readonly List<ToolStripItem> _items = new();

    public AppAction(string id, string text, Action execute)
    {
        Id = id;
        Text = text;
        Execute = execute;
    }

    public string Id { get; }

    public string Text { get; init; }

    public Action Execute { get; }

    public Func<bool>? CanExecute { get; init; }

    public Func<bool>? IsChecked { get; init; }

    public Keys Shortcut { get; init; }

    /// <summary>Shown in menus for keys handled elsewhere (e.g. "Enter" handled by the tree).</summary>
    public string? ShortcutDisplay { get; init; }

    public string? Glyph { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<ToolStripItem> Items => _items;

    public string ShortcutText => ShortcutDisplay ?? (Shortcut == Keys.None ? string.Empty : new KeysConverter().ConvertToString(Shortcut) ?? string.Empty);

    public bool Enabled => CanExecute?.Invoke() ?? true;

    public void Invoke()
    {
        if (Enabled)
        {
            Execute();
        }
    }

    internal void Attach(ToolStripItem item) => _items.Add(item);
}

/// <summary>Creates menu items / buttons bound to actions and refreshes their state.</summary>
internal sealed class ActionRegistry
{
    private readonly Dictionary<string, AppAction> _actions = new(StringComparer.Ordinal);
    private Color _iconColor = SystemColors.ControlText;
    private int _iconSize = 16;

    public IEnumerable<AppAction> All => _actions.Values;

    public AppAction this[string id] => _actions[id];

    public void Configure(int iconSize, Color iconColor)
    {
        _iconSize = iconSize;
        _iconColor = iconColor;
    }

    public AppAction Add(AppAction action)
    {
        _actions.Add(action.Id, action);
        return action;
    }

    public ToolStripMenuItem Menu(string id, string? textOverride = null)
    {
        var action = _actions[id];
        var item = new ToolStripMenuItem(textOverride ?? action.Text) { Tag = action };
        if (action.Shortcut != Keys.None && ToolStripManager.IsValidShortcut(action.Shortcut))
        {
            item.ShortcutKeys = action.Shortcut;
        }
        if (action.ShortcutDisplay is not null)
        {
            item.ShortcutKeyDisplayString = action.ShortcutDisplay;
        }
        if (action.Glyph is not null)
        {
            item.Image = GlyphIcons.Render(action.Glyph, _iconSize, _iconColor);
        }
        if (action.Description is not null)
        {
            item.ToolTipText = action.Description;
        }
        item.Click += (_, _) => action.Invoke();
        action.Attach(item);
        return item;
    }

    public ToolStripButton Button(string id)
    {
        var action = _actions[id];
        var tip = action.Text.Replace("&", string.Empty, StringComparison.Ordinal).TrimEnd('.', '…');
        if (action.ShortcutText.Length > 0)
        {
            tip += $" ({action.ShortcutText})";
        }
        var button = new ToolStripButton
        {
            DisplayStyle = action.Glyph is null ? ToolStripItemDisplayStyle.Text : ToolStripItemDisplayStyle.Image,
            Text = action.Text.Replace("&", string.Empty, StringComparison.Ordinal),
            ToolTipText = tip,
            Image = action.Glyph is null ? null : GlyphIcons.Render(action.Glyph, _iconSize, _iconColor),
            Tag = action,
            AccessibleName = tip,
        };
        button.Click += (_, _) => action.Invoke();
        action.Attach(button);
        return button;
    }

    /// <summary>Re-evaluates enabled/checked state for every surface.</summary>
    public void UpdateAll()
    {
        foreach (var action in _actions.Values)
        {
            if (action.Items.Count == 0)
            {
                continue;
            }
            bool enabled = action.Enabled;
            bool? isChecked = action.IsChecked?.Invoke();
            foreach (var item in action.Items)
            {
                item.Enabled = enabled;
                if (isChecked is bool c)
                {
                    switch (item)
                    {
                        case ToolStripMenuItem m:
                            m.Checked = c;
                            break;
                        case ToolStripButton b:
                            b.Checked = c;
                            break;
                    }
                }
            }
        }
    }

    /// <summary>Dispatches a key to the action that owns it (used for shortcuts not on any menu).</summary>
    public bool TryInvokeShortcut(Keys keys)
    {
        foreach (var action in _actions.Values)
        {
            if (action.Shortcut == keys && action.Items.Count == 0)
            {
                action.Invoke();
                return true;
            }
        }
        return false;
    }
}
