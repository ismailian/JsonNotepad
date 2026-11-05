namespace JsonNotepad.Core.Tests.Framework;

/// <summary>Thrown by <see cref="Assert"/> when an expectation is not met.</summary>
public sealed class AssertionException : Exception
{
    public AssertionException()
    {
    }

    public AssertionException(string message)
        : base(message)
    {
    }

    public AssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
