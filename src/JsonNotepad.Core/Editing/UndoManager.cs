namespace JsonNotepad.Core.Editing;

public enum UndoAction
{
    Do,
    Undo,
    Redo,
}

public sealed class CommandExecutedEventArgs(EditCommand command, UndoAction action) : EventArgs
{
    public EditCommand Command { get; } = command;

    public UndoAction Action { get; } = action;
}

/// <summary>
/// Linear undo/redo history with bounded size, compound grouping and save-point tracking.
/// </summary>
/// <remarks>
/// The design follows XML Notepad's UndoManager, with one improvement: "dirty" is
/// derived rather than stored. Every command gets a unique sequence number that names
/// the document state after it ran; the save point records that number. The document
/// is clean exactly when the current state id equals the saved one — which stays
/// correct across undo past the save point, redo-branch truncation and history trimming.
/// </remarks>
public sealed class UndoManager
{
    private readonly List<EditCommand> _stack = new();
    private int _position;
    private long _nextSequence = 1;
    private long _baseState;          // state id below the oldest retained command
    private long _savedState;         // state id at the last save (0 == the initial state)
    private CompoundCommand? _compound;
    private int _compoundDepth;

    public UndoManager(int maxHistory = 1000)
    {
        MaxHistory = Math.Max(1, maxHistory);
    }

    public int MaxHistory { get; set; }

    public event EventHandler? StateChanged;

    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;

    public bool CanUndo => _position > 0 && _compound is null;

    public bool CanRedo => _position < _stack.Count && _compound is null;

    public EditCommand? NextUndo => CanUndo ? _stack[_position - 1] : null;

    public EditCommand? NextRedo => CanRedo ? _stack[_position] : null;

    public int Count => _stack.Count;

    /// <summary>Identifier of the current document state.</summary>
    public long CurrentState => _position > 0 ? _stack[_position - 1].Sequence : _baseState;

    public bool IsAtSavePoint => CurrentState == _savedState;

    /// <summary>Executes and records <paramref name="command"/>.</summary>
    public void Execute(EditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.IsNoop)
        {
            return;
        }

        command.Do(); // throws => nothing recorded

        if (_compound is not null)
        {
            _compound.AddExecuted(command);
            return;
        }

        Record(command);
        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, UndoAction.Do));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts grouping subsequent <see cref="Execute"/> calls into one undo step.
    /// Dispose the returned scope to close the group. Scopes nest.
    /// </summary>
    public IDisposable BeginCompound(string name)
    {
        if (_compoundDepth++ == 0)
        {
            _compound = new CompoundCommand(name);
        }
        return new CompoundScope(this);
    }

    public EditCommand? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }
        var command = _stack[_position - 1];
        command.Undo();
        _position--;
        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, UndoAction.Undo));
        StateChanged?.Invoke(this, EventArgs.Empty);
        return command;
    }

    public EditCommand? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }
        var command = _stack[_position];
        command.Redo();
        _position++;
        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, UndoAction.Redo));
        StateChanged?.Invoke(this, EventArgs.Empty);
        return command;
    }

    /// <summary>Marks the current state as saved.</summary>
    public void MarkSavePoint() => MarkSavePoint(CurrentState);

    /// <summary>Marks a specific state as saved (used by background saves of an earlier snapshot).</summary>
    public void MarkSavePoint(long state)
    {
        _savedState = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forces the document to be considered modified (e.g. after a reload error).</summary>
    public void InvalidateSavePoint()
    {
        _savedState = -1;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _stack.Clear();
        _position = 0;
        _baseState = 0;
        _savedState = 0;
        _compound = null;
        _compoundDepth = 0;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Record(EditCommand command)
    {
        command.Sequence = _nextSequence++;
        if (_position < _stack.Count)
        {
            _stack.RemoveRange(_position, _stack.Count - _position); // discard redo branch
        }
        _stack.Add(command);
        _position++;
        while (_stack.Count > MaxHistory)
        {
            _baseState = _stack[0].Sequence;
            _stack.RemoveAt(0);
            _position--;
        }
    }

    private void EndCompound()
    {
        if (_compoundDepth == 0)
        {
            throw new InvalidOperationException("No compound command is open.");
        }
        if (--_compoundDepth > 0)
        {
            return;
        }
        var compound = _compound!;
        _compound = null;
        if (compound.Commands.Count == 0)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        Record(compound);
        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(compound, UndoAction.Do));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class CompoundScope(UndoManager owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.EndCompound();
            }
        }
    }
}
