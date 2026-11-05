using JsonNotepad.Core.Model;

namespace JsonNotepad.Controls.Theming;

/// <summary>Color palette shared by the owner-drawn controls (light and dark variants).</summary>
public sealed class Theme
{
    public required string Name { get; init; }

    public required bool IsDark { get; init; }

    public required Color Window { get; init; }

    public required Color WindowText { get; init; }

    public required Color GrayText { get; init; }

    public required Color Selection { get; init; }

    public required Color SelectionText { get; init; }

    public required Color InactiveSelection { get; init; }

    public required Color InactiveSelectionText { get; init; }

    public required Color TreeLines { get; init; }

    public required Color Glyph { get; init; }

    public required Color Splitter { get; init; }

    public required Color Gutter { get; init; }

    public required Color GutterText { get; init; }

    public required Color Key { get; init; }

    public required Color StringValue { get; init; }

    public required Color NumberValue { get; init; }

    public required Color BooleanValue { get; init; }

    public required Color NullValue { get; init; }

    public required Color Punctuation { get; init; }

    public required Color DropFeedback { get; init; }

    public required Color CurrentLine { get; init; }

    public Color ColorFor(JNodeKind kind) => kind switch
    {
        JNodeKind.String => StringValue,
        JNodeKind.Number => NumberValue,
        JNodeKind.Boolean => BooleanValue,
        JNodeKind.Null => NullValue,
        _ => GrayText,
    };

    /// <summary>Badge color for the node-type icons.</summary>
    public Color BadgeFor(JNodeKind kind) => kind switch
    {
        JNodeKind.Object => IsDark ? Color.FromArgb(0xE0, 0xA8, 0x4A) : Color.FromArgb(0xC2, 0x7C, 0x0E),
        JNodeKind.Array => IsDark ? Color.FromArgb(0x5A, 0xB4, 0xE6) : Color.FromArgb(0x1F, 0x6F, 0xB5),
        JNodeKind.String => StringValue,
        JNodeKind.Number => NumberValue,
        JNodeKind.Boolean => BooleanValue,
        _ => NullValue,
    };

    public static Theme Light { get; } = new()
    {
        Name = "Light",
        IsDark = false,
        Window = SystemColors.Window,
        WindowText = SystemColors.WindowText,
        GrayText = Color.FromArgb(0x6E, 0x6E, 0x6E),
        Selection = SystemColors.Highlight,
        SelectionText = SystemColors.HighlightText,
        InactiveSelection = Color.FromArgb(0xD9, 0xD9, 0xD9),
        InactiveSelectionText = SystemColors.WindowText,
        TreeLines = Color.FromArgb(0xA0, 0xA0, 0xA0),
        Glyph = Color.FromArgb(0x5A, 0x5A, 0x5A),
        Splitter = Color.FromArgb(0xD0, 0xD0, 0xD0),
        Gutter = Color.FromArgb(0xF4, 0xF4, 0xF4),
        GutterText = Color.FromArgb(0x2B, 0x91, 0xAF),
        Key = Color.FromArgb(0x1A, 0x1A, 0x8C),
        StringValue = Color.FromArgb(0xA3, 0x15, 0x15),
        NumberValue = Color.FromArgb(0x09, 0x86, 0x58),
        BooleanValue = Color.FromArgb(0x00, 0x00, 0xFF),
        NullValue = Color.FromArgb(0x80, 0x80, 0x80),
        Punctuation = Color.FromArgb(0x50, 0x50, 0x50),
        DropFeedback = Color.FromArgb(0x00, 0x78, 0xD4),
        CurrentLine = Color.FromArgb(0xEE, 0xF4, 0xFB),
    };

    public static Theme Dark { get; } = new()
    {
        Name = "Dark",
        IsDark = true,
        Window = Color.FromArgb(0x1E, 0x1E, 0x1E),
        WindowText = Color.FromArgb(0xD4, 0xD4, 0xD4),
        GrayText = Color.FromArgb(0x8A, 0x8A, 0x8A),
        Selection = Color.FromArgb(0x26, 0x4F, 0x78),
        SelectionText = Color.White,
        InactiveSelection = Color.FromArgb(0x3A, 0x3D, 0x41),
        InactiveSelectionText = Color.FromArgb(0xD4, 0xD4, 0xD4),
        TreeLines = Color.FromArgb(0x58, 0x58, 0x58),
        Glyph = Color.FromArgb(0xC0, 0xC0, 0xC0),
        Splitter = Color.FromArgb(0x3F, 0x3F, 0x46),
        Gutter = Color.FromArgb(0x25, 0x25, 0x26),
        GutterText = Color.FromArgb(0x85, 0x85, 0x85),
        Key = Color.FromArgb(0x9C, 0xDC, 0xFE),
        StringValue = Color.FromArgb(0xCE, 0x91, 0x78),
        NumberValue = Color.FromArgb(0xB5, 0xCE, 0xA8),
        BooleanValue = Color.FromArgb(0x56, 0x9C, 0xD6),
        NullValue = Color.FromArgb(0x9A, 0x9A, 0x9A),
        Punctuation = Color.FromArgb(0xB0, 0xB0, 0xB0),
        DropFeedback = Color.FromArgb(0x3F, 0x9C, 0xFF),
        CurrentLine = Color.FromArgb(0x2A, 0x2D, 0x2E),
    };

    /// <summary>The theme matching the application's color mode.</summary>
    public static Theme FromApplication()
    {
#pragma warning disable WFO5001 // Dark mode APIs are experimental in .NET 9 WinForms.
        return Application.IsDarkModeEnabled ? Dark : Light;
#pragma warning restore WFO5001
    }
}
