using JsonNotepad.Controls.Tree;
using JsonNotepad.Core.Search;

namespace JsonNotepad.App.Forms;

/// <summary>Find / Replace integration (<see cref="IFindTarget"/>).</summary>
internal sealed partial class MainForm : IFindTarget
{
    private FindMatch? _lastMatch;

    private void ShowFind(bool replace)
    {
        _tabs.SelectedIndex = TreeTab;
        _findForm ??= new FindReplaceForm(this, _settings.FindHistory, _settings.ReplaceHistory);
        var seed = Selected is { } n ? (_tree.ActivePane == TreePane.Names ? n.Name : n.Value) : null;
        _findForm.ShowFor(this, replace, seed);
    }

    private void FindAgain(bool backwards)
    {
        if (_findForm is null || !_findForm.FindAgain(backwards))
        {
            ShowFind(replace: false);
        }
    }

    public FindMatch? FindNext(FindOptions options)
    {
        var engine = new FindEngine(options);
        var start = Selected;
        FindField? field = _lastMatch is { } last && ReferenceEquals(last.Node, start) ? last.Field : null;
        var match = engine.FindNext(_session.Document.Root, start, field);
        _lastMatch = match;
        if (match is { } m)
        {
            _tabs.SelectedIndex = TreeTab;
            _tree.SelectedNode = m.Node;
            _tree.ActivePane = m.Field == FindField.Name ? TreePane.Names : TreePane.Values;
        }
        return match;
    }

    public string? ReplaceCurrent(FindOptions options, string replacement)
    {
        var engine = new FindEngine(options);
        if (_lastMatch is { } m && ReferenceEquals(m.Node, Selected) && engine.IsMatch(m.Node, m.Field))
        {
            var result = engine.ReplaceMatch(_session.Editor, m, replacement);
            if (!result.Success)
            {
                return result.Error;
            }
        }
        return FindNext(options) is null ? "No more matches." : null;
    }

    public (int Replaced, int Skipped) ReplaceAll(FindOptions options, string replacement)
    {
        var engine = new FindEngine(options);
        var counts = engine.ReplaceAll(_session.Editor, replacement);
        SetStatus($"Replaced {counts.Replaced:N0} occurrence(s).");
        return counts;
    }
}
