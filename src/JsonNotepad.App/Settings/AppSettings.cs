using System.ComponentModel;
using System.Text.Json.Serialization;
using JsonNotepad.Core.IO;

namespace JsonNotepad.App.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum LineEnding
{
    [Description("CRLF (Windows)")]
    CrLf,
    [Description("LF (Unix)")]
    Lf,
}

public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);

/// <summary>
/// User preferences. Browsable properties are edited through a PropertyGrid in the Options
/// dialog (as XML Notepad does); the rest is window state persisted between sessions.
/// </summary>
public sealed class AppSettings
{
    private string _treeFontSpec = FontSpec.Format(FontSpec.DefaultTree);
    private string _textFontSpec = FontSpec.Format(FontSpec.DefaultText);
    private Font? _treeFont;
    private Font? _textFont;

    // ----- Appearance --------------------------------------------------------------

    [Category("Appearance")]
    [DisplayName("Theme")]
    [Description("Light, dark, or follow the Windows setting. Takes effect after restarting JsonNotepad.")]
    public AppTheme Theme { get; set; } = AppTheme.System;

    [Category("Appearance")]
    [DisplayName("Tree font")]
    [Description("Font used by the tree view.")]
    [JsonIgnore]
    public Font TreeFont
    {
        get => _treeFont ??= FontSpec.Parse(TreeFontSpec, FontSpec.DefaultTree);
        set
        {
            _treeFont = value;
            _treeFontSpec = FontSpec.Format(value);
        }
    }

    [Category("Appearance")]
    [DisplayName("Text view font")]
    [Description("Monospaced font used by the text view.")]
    [JsonIgnore]
    public Font TextFont
    {
        get => _textFont ??= FontSpec.Parse(TextFontSpec, FontSpec.DefaultText);
        set
        {
            _textFont = value;
            _textFontSpec = FontSpec.Format(value);
        }
    }

    [Category("Appearance")]
    [DisplayName("Show tree lines")]
    [Description("Draw dotted lines connecting parent and child nodes.")]
    public bool ShowTreeLines { get; set; } = true;

    // ----- Behavior ------------------------------------------------------------------

    [Category("Behavior")]
    [DisplayName("Auto-expand depth")]
    [Description("How many levels to expand when a document is opened.")]
    public int AutoExpandDepth { get; set; } = 2;

    [Category("Behavior")]
    [DisplayName("Large file threshold (MB)")]
    [Description("Files larger than this open with only the root expanded and show a progress bar.")]
    public int LargeFileThresholdMB { get; set; } = 20;

    [Category("Behavior")]
    [DisplayName("Expand All confirmation (nodes)")]
    [Description("Ask before Expand All when the document has more nodes than this.")]
    public int ExpandAllConfirmNodes { get; set; } = 250_000;

    [Category("Behavior")]
    [DisplayName("Undo history size")]
    [Description("Maximum number of undo steps kept in memory.")]
    public int MaxUndoHistory { get; set; } = 1000;

    [Category("Behavior")]
    [DisplayName("Auto-reload unmodified files")]
    [Description("Reload silently when the file changes on disk and you have no unsaved edits.")]
    public bool AutoReloadUnmodified { get; set; } = true;

    [Category("Behavior")]
    [DisplayName("Validate while editing")]
    [Description("Re-run schema validation and duplicate-key checks in the background after edits.")]
    public bool ValidateOnEdit { get; set; } = true;

    [Category("Behavior")]
    [DisplayName("Recent files")]
    [Description("Number of entries in the File ▸ Recent Files menu.")]
    public int RecentFilesCount { get; set; } = 15;

    // ----- Parsing ------------------------------------------------------------------------

    [Category("Parsing")]
    [DisplayName("Allow comments")]
    [Description("Accept // and /* */ comments (JSONC). Comments are not preserved when saving.")]
    public bool AllowComments { get; set; } = true;

    [Category("Parsing")]
    [DisplayName("Allow trailing commas")]
    public bool AllowTrailingCommas { get; set; } = true;

    [Category("Parsing")]
    [DisplayName("Maximum nesting depth")]
    public int MaxDepth { get; set; } = 1024;

    // ----- Formatting ----------------------------------------------------------------------

    [Category("Formatting")]
    [DisplayName("Preserve file formatting")]
    [Description("Save with the encoding, indentation and line endings detected when the file was opened.")]
    public bool PreserveFileFormatting { get; set; } = true;

    [Category("Formatting")]
    [DisplayName("Indent size")]
    public int IndentSize { get; set; } = 2;

    [Category("Formatting")]
    [DisplayName("Indent with tabs")]
    public bool UseTabs { get; set; }

    [Category("Formatting")]
    [DisplayName("Line endings")]
    public LineEnding LineEnding { get; set; } = LineEnding.CrLf;

    [Category("Formatting")]
    [DisplayName("Escape non-ASCII characters")]
    [Description("Write characters outside ASCII as \\uXXXX escapes.")]
    public bool EscapeNonAscii { get; set; }

    [Category("Formatting")]
    [DisplayName("Final newline")]
    [Description("End indented files with a line break.")]
    public bool TrailingNewLine { get; set; } = true;

    // ----- Persisted window state (not shown in Options) -----------------------------------------

    [Browsable(false)]
    public string TreeFontSpec
    {
        get => _treeFontSpec;
        set
        {
            _treeFontSpec = value;
            _treeFont = null;
        }
    }

    [Browsable(false)]
    public string TextFontSpec
    {
        get => _textFontSpec;
        set
        {
            _textFontSpec = value;
            _textFont = null;
        }
    }

    [Browsable(false)]
    public WindowPlacement? Window { get; set; }

    [Browsable(false)]
    public int NameColumnWidth { get; set; } = 320;

    [Browsable(false)]
    public int ErrorListHeight { get; set; } = 160;

    [Browsable(false)]
    public bool ShowErrorList { get; set; } = true;

    [Browsable(false)]
    public bool ShowStatusBar { get; set; } = true;

    [Browsable(false)]
    public bool ShowToolbar { get; set; } = true;

    [Browsable(false)]
    public List<string> RecentFiles { get; set; } = new();

    [Browsable(false)]
    public string? LastDirectory { get; set; }

    [Browsable(false)]
    public List<string> FindHistory { get; set; } = new();

    [Browsable(false)]
    public List<string> ReplaceHistory { get; set; } = new();

    public ParseOptions ToParseOptions() => new()
    {
        AllowComments = AllowComments,
        AllowTrailingCommas = AllowTrailingCommas,
        MaxDepth = Math.Clamp(MaxDepth, 16, 100_000),
        DetectDuplicateKeys = true,
    };

    public FormatOptions ToFormatOptions() => new()
    {
        Indented = true,
        IndentSize = Math.Clamp(IndentSize, 0, 16),
        UseTabs = UseTabs,
        NewLine = LineEnding == LineEnding.Lf ? "\n" : "\r\n",
        EscapeNonAscii = EscapeNonAscii,
        TrailingNewLine = TrailingNewLine,
    };

    public AppSettings Clone() => SettingsStore.Clone(this);
}

/// <summary>Font ⇄ invariant string conversion for the settings file.</summary>
internal static class FontSpec
{
    public static Font DefaultTree => new("Segoe UI", 9.75f);

    public static Font DefaultText => new("Consolas", 10f);

    public static string Format(Font font) =>
        TypeDescriptor.GetConverter(typeof(Font)).ConvertToInvariantString(font) ?? string.Empty;

    public static Font Parse(string? spec, Font fallback)
    {
        if (!string.IsNullOrWhiteSpace(spec))
        {
            try
            {
                if (TypeDescriptor.GetConverter(typeof(Font)).ConvertFromInvariantString(spec) is Font f)
                {
                    return f;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or FormatException)
            {
            }
        }
        return fallback;
    }
}
