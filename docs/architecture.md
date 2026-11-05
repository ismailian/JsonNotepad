# JsonNotepad Architecture

This document explains how JsonNotepad is put together. It covers the layers, the main types, the threading model, the design patterns used and the reasons behind them. For the XML Notepad source study that shaped these decisions, see [xmlnotepad-analysis.md](xmlnotepad-analysis.md). For measured numbers and tuning, see [performance.md](performance.md).

## 1. Goals and constraints

| Goal | Consequence |
|---|---|
| Feel like XML Notepad, but for JSON | Two-column tree (names / values), in-place editing, nudge commands, Insert ▸ *kind* ▸ Before/After/Child, an error list, a location bar and an options PropertyGrid |
| Stay fast on large files (hundreds of MB) | Streaming parser, compact model, virtualized views, background work, no per-node UI objects |
| No third-party packages | Only the .NET 9 SDK: `System.Text.Json` (reader only), WinForms, GDI+. The tests use a home-grown runner |
| Keep Core logic separate from UI | `JsonNotepad.Core` targets plain `net9.0` and *cannot* reference WinForms |
| Ready for the Microsoft Store | MSIX packaging, single-source versioning, PerMonitorV2 DPI, accessibility objects, dark mode |

## 2. Solution layout

```
JsonNotepad.sln
├── src/
│   ├── JsonNotepad.Core/        net9.0           UI-agnostic engine
│   ├── JsonNotepad.Controls/    net9.0-windows   reusable owner-drawn WinForms controls
│   └── JsonNotepad.App/         net9.0-windows   the application shell (JsonNotepad.exe)
├── tests/JsonNotepad.Core.Tests/                 dependency-free test runner + 230 tests
├── tools/IconGen/                             procedural logo → .ico + MSIX assets
├── installer/msix, installer/inno             packaging definitions
├── build/                                     PowerShell build/package/version scripts
└── docs/                                      you are here
```

Dependencies point one way only:

```
JsonNotepad.App ──► JsonNotepad.Controls ──► JsonNotepad.Core
      └──────────────────────────────────►┘
```

`Core` knows nothing about the other two projects. `Controls` knows the model but not the application, so it never sees menus, settings or dialogs. `App` composes everything.

## 3. JsonNotepad.Core

| Namespace | Responsibility | Key types |
|---|---|---|
| `Model` | The editable tree and its invariants | `JNode`, `JDocument`, `JNodeKind`, `JsonNumber`, `JsonEquality`, `PreorderCursor`, `NameTable`, `Diagnostic` |
| `IO` | Bytes ⇄ model | `JsonParser`, `JsonLoader`, `JsonDocumentWriter`, `JsonFileSaver`, `FormatDetector`, `FormattedText`, `ParseOptions`, `FormatOptions`, `FileFormatInfo` |
| `Editing` | Undoable operations | `UndoManager`, `EditCommand`, `CompoundCommand`, concrete commands, `NodeEditor`, `KindConverter`, `NameGenerator` |
| `Paths` | Locations | `JsonPath` (build and resolve `$.a['b c'][3]`) |
| `Search` | Find / replace | `FindEngine`, `FindOptions`, `FindMatch` |
| `Validation` | Correctness | `JsonSchemaValidator`, `SchemaValidationService`, `DocumentChecker` |
| `Services` | Infrastructure that isn't UI | `DelayedActions`, `FileChangeWatcher`, `RecentFiles`, `DocumentStatistics` |
| `Session` | "The open file" | `DocumentSession` |

### 3.1 The model: `JNode` and `JDocument`

* **One object per JSON value.** A node holds a kind (byte), name, value, parent and a lazily allocated `List<JNode>` of children: about 56 bytes plus strings. Empty containers and primitives pay nothing for children.
* **Numbers are stored as their literal text.** `12345678901234567890`, `1.10` and `1e400` survive a load/save round trip byte for byte. Nothing is pushed through `double` unless a consumer asks.
* **The document is the aggregate root.** Only `JDocument` mutates attached nodes (`InsertChild`, `RemoveChild`, `SetName`, `SetValue`, `Replace`, `Reorder`). It validates each operation, applies it, bumps `Version` and raises `Changed`. The setters on `JNode` are `internal`.
* **Algorithms are iterative.** Traversal, cloning, writing and equality all use explicit stacks, so nesting depth is limited by memory, not by the thread's stack.
* **`PreorderCursor`** walks the tree in document order in either direction, keeping the `(parent, index)` path. A naive "next sibling via `IndexOf`" walk is O(n²) over a large array, and a large array of records is the typical shape of big JSON.

### 3.2 Parsing

`JsonParser.ParseStream` feeds 1 MB blocks from the stream into `Utf8JsonReader`, carrying `JsonReaderState` across blocks. The buffer doubles only when a single token, such as a huge string, is larger than it. Input memory therefore stays constant and only the tree grows.

* Property names go through a `NameTable`, the JSON equivalent of `System.Xml.NameTable`. It uses .NET 9's `Dictionary.GetAlternateLookup<ReadOnlySpan<char>>`, so a name that is already known allocates nothing. One million records with the same five keys hold five key strings, not five million. Short values ("active", "USD") and short number literals are atomized the same way. Both tables are size-bounded.
* Duplicate keys are found with pooled `HashSet`s, one per open object, and reported as warnings with a JSONPath.
* `JsonLoader` sniffs the BOM (UTF-8/16/32, plus BOM-less UTF-16), transcodes non-UTF-8 input with `Encoding.CreateTranscodingStream`, and detects indentation and line endings so that saving preserves the author's style (`FileFormatInfo`).
* `JsonException` is translated into `JsonParseException` with a 1-based line and column.

### 3.3 Writing and saving

`JsonDocumentWriter` is hand-written instead of using `Utf8JsonWriter`, because it needs three things together:

1. writing number literals verbatim;
2. writing to any `TextWriter` and encoding;
3. recording the start line of every node in pre-order (`nodeLines`). This powers Tree ⇄ Text View synchronization.

`JsonFileSaver` writes to a temporary file in the target directory, flushes it to disk, and then uses `File.Replace` (falling back to `File.Move(overwrite)`). A crash, full disk or cancellation can never leave a truncated file. `File.Replace` also preserves the original file's ACLs and attributes.

### 3.4 Editing: Command, Composite and Facade

```
UI ──► NodeEditor (Facade) ──► EditCommand (Command) ──► JDocument (mutations + events)
                    │                    ▲
                    └──► UndoManager ────┘   (history, compound groups, save point)
```

* Each command is atomic: it validates before mutating, and `CompoundCommand` rolls back what it already applied if a later step throws.
* `UndoManager.BeginCompound(name)` returns an `IDisposable` scope, so *Replace All*, *Paste* of several members and *Sort (recursive)* are each a single undo step.
* **Dirty tracking is derived, not stored.** Every recorded command gets a sequence id that names the document state after it ran, and the save point stores that id. The document is clean when the current state id equals the saved one. This stays correct across undoing past the save point, discarding redo branches and trimming history. It also lets a background save mark exactly the state it wrote, even if the user kept editing meanwhile.
* `NodeEditor` holds the editing *rules*: unique member names on insert, name handling when a node moves between an object and an array (nudge left/right, drag and drop), value validation per type, *Change To* conversions (`KindConverter`), sorting, and clipboard parsing, which accepts both values and `"key": value` fragments.

### 3.5 Validation

`JsonSchemaValidator` is a dependency-free validator. It covers the keywords used by most real-world schemas from draft 4 to 2020-12: types, enum/const, object, array, string and number constraints, common formats, combinators, `if/then/else` and local `$ref`s. Errors are reported as JSONPaths, built incrementally and only turned into strings when an error is actually reported. Remote `$ref`s are reported, never fetched.

`SchemaValidationService` picks the schema: an explicit *Tools ▸ Assign Schema* wins, otherwise a root `"$schema"` that points to a local file. It caches compiled validators by file timestamp. `DocumentChecker` re-runs checks that can go stale while editing, such as duplicate keys.

### 3.6 `DocumentSession`

`DocumentSession` is the UI-agnostic "open file" object (the role `XmlCache` plays in XML Notepad). It owns the document, undo manager, `NodeEditor`, path, `FileFormatInfo`, load diagnostics and a `FileChangeWatcher`. It raises `DocumentReplaced`, `FilePathChanged`, `DirtyChanged` and `ExternalChange`. `OpenAsync` and `SaveAsync` are where async and threading meet the model (see §6).

## 4. JsonNotepad.Controls

| Control | What it is |
|---|---|
| `JsonTreeView` | Owner-drawn two-column tree: names with +/- glyphs, dotted tree lines and type badges on the left, values on the right, with a draggable divider |
| `JsonTextView` | Read-only, virtualized, syntax-highlighted text view with a line-number gutter, selection and copy |
| `ErrorListView` | Virtual-mode `ListView` for diagnostics |
| `ViewSwitcher` | Borderless, themable tab strip (the stock `TabControl` border cannot be themed) |
| `Theme` | Light and dark palettes shared by every owner-drawn surface |
| `GlyphIcons` / `NodeKindIcons` | Toolbar icons rendered from the Segoe Fluent Icons / MDL2 font, and type badges drawn with GDI+, cached per size and theme |

### 4.1 How the tree is virtualized

XML Notepad builds lazy `TreeNode` wrappers. JsonNotepad goes a step further and creates **no per-node UI objects at all**:

* `TreeRows` holds a flat `List<TreeRow>` of the rows that are currently visible. `TreeRow` is a 16-byte struct of node, depth and index. It also holds a `HashSet<JNode>` of expanded nodes.
* Scrolling is by row, and painting draws only the rows in the clip rectangle. Hit testing is `top + y / rowHeight`. All three are O(1) per visible row, however large the document is.
* Expand and collapse splice the list in place (`InsertRange` / `RemoveRange`).
* Structural model edits only mark the list dirty. It is rebuilt lazily before the next paint, at O(visible rows) cost.
* Tree lines need to know whether an ancestor is a last child. `JNode.IsLastChild` answers that in O(1) by comparing with `Parent.Children[^1]`, so no per-row line state is stored.

The control merges XML Notepad's `XmlTreeView` and `NodeTextView` into one control, so there is a single scroll position, focus and selection to keep in sync. A single `InPlaceEditor` text box is overlaid on the cell being edited (XML Notepad's `TextEditorOverlay` idea). It keeps editing keys such as Ctrl+C or Delete to itself, so the main menu's shortcuts don't take them.

The control also supports type-to-find, drag and drop (move, or copy with Ctrl), tooltips for truncated text and an `AccessibleObject` per visible row. It raises events (`NodeEditCommitting`, `NodeDropped`, `FilesDropped`, …) and never edits the model itself. The host decides.

## 5. JsonNotepad.App

* `Program` sets up exception handling, loads settings, applies the color mode (dark mode uses .NET 9's experimental `Application.SetColorMode`) and runs `MainForm`.
* **`MainForm` is split by concern into partial classes:**

  | File | Contents |
  |---|---|
  | `MainForm.cs` | Layout and lifecycle |
  | `MainForm.Actions.cs` | Commands, menus and their handlers |
  | `MainForm.Files.cs` | Open, save, reload and prompts |
  | `MainForm.Background.cs` | Validation, text view and statistics |
  | `MainForm.Find.cs` | `IFindTarget` |

* **`ActionRegistry` and `AppAction`** apply the Command pattern at the UI level. Each command is defined once (text, shortcut, glyph, `CanExecute`, `IsChecked`, `Execute`) and can appear in any number of menus, context menus and toolbar buttons. Enabled and checked state is refreshed in one coalesced pass after selection, undo, focus or busy-state changes.
* **`AppSettings`** is edited through a categorized `PropertyGrid`, as in XML Notepad. It is persisted as JSON under `%LOCALAPPDATA%\JsonNotepad` with an atomic write.
* **Dialogs** share `DialogBase`: `OptionsForm`, `FindReplaceForm` (modeless), `InputDialog` (Go to Path, Edit as JSON), `ReportForm` (statistics, keyboard shortcuts) and `AboutForm`.
* `Log` and `CrashReporter` provide a minimal file log, plus a user-facing message for unexpected exceptions.

## 6. Threading model

**Rule: the model belongs to the UI thread.** Nothing mutates or even reads the live tree from another thread. Background work uses one of two safe shapes:

1. **Build, then publish.** Loading parses on the thread pool into a brand-new tree that nobody else can see. Only after `await` returns to the UI thread does `DocumentSession` swap it in.
2. **Snapshot, then compute.** Saving, Text View formatting, schema validation and statistics call `JDocument.CreateSnapshot()` on the UI thread. This is an iterative deep clone that shares the immutable strings, so it only allocates node objects. The work then runs in `Task.Run` against the snapshot. When the result comes back, it is applied only if `JDocument.Version` (and the document instance) is unchanged; otherwise it is discarded or redone.

Coordination tools:

| Tool | Role |
|---|---|
| `DelayedActions` | Named, restartable debouncers that fire on the captured `SynchronizationContext`. A burst of keystrokes causes one validation, one text-view refresh and one settings save |
| `CancellationTokenSource` per kind of work | Starting new work cancels the previous run; parser, writer, validator and statistics all poll the token |
| `IProgress<LoadProgress>` | Progress is throttled to 10 Hz and marshalled to the UI automatically; the status bar shows MB read plus a Cancel link |

XML Notepad, by contrast, validates synchronously on the UI thread after every change, and its async XSLT transform reads the live DOM. Snapshots are the fix for both.

**Trade-off.** A snapshot of a very large document costs a fraction of a second and temporarily doubles node memory. It is debounced (validation waits 700 ms after the last edit) and skipped when nothing consumes it (the Text View is formatted only while visible). The alternative, readers/writer locking of the live tree, would add a lock to every UI-thread read. That is a worse trade for an editor.

## 7. Design patterns at a glance

| Pattern | Where |
|---|---|
| Command | `EditCommand` + `UndoManager` (model); `AppAction` (UI) |
| Composite | `CompoundCommand` |
| Facade | `NodeEditor`, `DocumentSession` |
| Observer | `JDocument.Changed`, `UndoManager.StateChanged`, session events, control events |
| Builder | `JsonParser.TreeBuilder` |
| Strategy | `FindEngine` (one regex pipeline for literal, whole-word and regex modes); `KindConverter` |
| Flyweight | `NameTable` atomization; cached glyph and badge bitmaps |
| Snapshot / immutable hand-off | Background work on `CreateSnapshot()` |
| Debounce | `DelayedActions` |

## 8. Extending JsonNotepad

* **New edit operation:** add a command (or compose existing ones) and a method on `NodeEditor`, then register an `AppAction` in `MainForm.Actions.cs` and place it in a menu. Add tests in `tests/JsonNotepad.Core.Tests`.
* **New background analysis:** follow the snapshot pattern in `MainForm.Background.cs`.
* **New setting:** add a property with `[Category]`, `[DisplayName]` and `[Description]` to `AppSettings`. It appears in the Options dialog automatically.
