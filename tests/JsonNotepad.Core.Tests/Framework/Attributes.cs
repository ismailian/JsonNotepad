namespace JsonNotepad.Core.Tests.Framework;

/// <summary>Marks a public instance method (void or async Task) as a test.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class TestAttribute : Attribute
{
}

/// <summary>Tags a test (or every test in a class) so it can be included/excluded from the command line.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class CategoryAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
