using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.Editing;

/// <summary>
/// An undoable edit (Command pattern). Commands must be atomic: validate everything
/// before mutating, so that a failed <see cref="Do"/> leaves the document untouched.
/// </summary>
public abstract class EditCommand
{
    /// <summary>Short user-facing name, shown as "Undo {Name}".</summary>
    public abstract string Name { get; }

    /// <summary>True when executing would change nothing; such commands are not recorded.</summary>
    public virtual bool IsNoop => false;

    /// <summary>The node the UI should select after Do/Redo.</summary>
    public virtual JNode? SelectionAfterDo => null;

    /// <summary>The node the UI should select after Undo.</summary>
    public virtual JNode? SelectionAfterUndo => null;

    /// <summary>Unique id assigned by the <see cref="UndoManager"/>; identifies the state after this command.</summary>
    internal long Sequence { get; set; }

    public abstract void Do();

    public abstract void Undo();

    public virtual void Redo() => Do();
}

/// <summary>Groups several commands into one undo step (Composite pattern).</summary>
public sealed class CompoundCommand : EditCommand
{
    private readonly List<EditCommand> _commands = new();

    public CompoundCommand(string name)
    {
        Name = name;
    }

    public override string Name { get; }

    public IReadOnlyList<EditCommand> Commands => _commands;

    public override bool IsNoop => _commands.TrueForAll(c => c.IsNoop);

    public override JNode? SelectionAfterDo => _commands.Count > 0 ? _commands[^1].SelectionAfterDo : null;

    public override JNode? SelectionAfterUndo => _commands.Count > 0 ? _commands[0].SelectionAfterUndo : null;

    /// <summary>Adds a command that has already been executed.</summary>
    internal void AddExecuted(EditCommand command) => _commands.Add(command);

    public override void Do()
    {
        for (int i = 0; i < _commands.Count; i++)
        {
            try
            {
                _commands[i].Do();
            }
            catch
            {
                // Roll back what was applied so the compound stays atomic.
                for (int j = i - 1; j >= 0; j--)
                {
                    _commands[j].Undo();
                }
                throw;
            }
        }
    }

    public override void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }

    public override void Redo()
    {
        foreach (var c in _commands)
        {
            c.Redo();
        }
    }
}
