# CLAUDE.md

Guidance for AI assistants and human contributors working in this repository.

## What this is

JsonNotepad is a Windows desktop JSON viewer/editor modeled on Microsoft XML Notepad.

- Stack: C# / .NET 9 / WinForms.
- Solution: `JsonNotepad.sln`.
- Product version: set only in `Directory.Build.props` (`VersionPrefix`).

## Hard rules

1. **No NuGet packages.** Use only what the .NET 9 SDK and Windows provide. That includes tests: `tests/JsonNotepad.Core.Tests` has its own tiny runner, so don't add xunit/MSTest.
2. **`JsonNotepad.Core` stays UI-free.** It targets plain `net9.0`. Never reference WinForms, `System.Drawing` or the Controls/App projects from it.
3. **Dependencies flow App → Controls → Core** (App may also use Core directly). Controls never reference App.
4. **Every model mutation is a command.** UI code calls `NodeEditor` (or another `EditCommand` executed through `UndoManager`). Never call `JDocument` mutators directly from UI code, and never mutate `JNode` from outside `JsonNotepad.Core`.
5. **The live model belongs to the UI thread.** Background work must either build a new tree that isn't published yet (loading) or operate on `JDocument.CreateSnapshot()`, then check `JDocument.Version` before applying results. See `MainForm.Background.cs` for the pattern.
6. **No recursion over the JSON tree.** Documents can be arbitrarily deep; use explicit stacks (see `JNode.DescendantsAndSelf`, `JsonDocumentWriter`, `PreorderCursor`).
7. **Warnings are errors** (`TreatWarningsAsErrors`). Keep builds at zero warnings. For WinForms analyzer WFO1000, mark runtime-only public control properties with `[DesignerSerializationVisibility(Hidden)]`.

## Commands

```powershell
dotnet build JsonNotepad.sln                                          # build everything
dotnet run --project tests/JsonNotepad.Core.Tests -c Release           # run all tests
dotnet run --project tests/JsonNotepad.Core.Tests -c Release -- --exclude-category perf
dotnet run --project tests/JsonNotepad.Core.Tests -c Release -- FindEngine   # filter
dotnet run --project src/JsonNotepad.App -- samples/bookstore.json     # launch the app
./build/build.ps1                                                   # CI-style build + tests
./build/package-msix.ps1 -Architectures x64                         # MSIX package
./build/version.ps1 patch                                           # bump version
```

A running `JsonNotepad.exe` locks its output folder. Close it before rebuilding the App project.

## Where things live

| Area | Location |
|---|---|
| Model (`JNode`, `JDocument`, `PreorderCursor`, `NameTable`) | `src/JsonNotepad.Core/Model` |
| Parse / load / write / save / format detection | `src/JsonNotepad.Core/IO` |
| Undo, commands, `NodeEditor`, type conversion | `src/JsonNotepad.Core/Editing` |
| JSONPath | `src/JsonNotepad.Core/Paths` |
| Find / replace | `src/JsonNotepad.Core/Search` |
| JSON Schema, duplicate-key checks | `src/JsonNotepad.Core/Validation` |
| Debouncer, file watcher, MRU, statistics | `src/JsonNotepad.Core/Services` |
| "Open file" orchestration | `src/JsonNotepad.Core/Session/DocumentSession.cs` |
| Virtualized tree (+ input, drag/drop, accessibility) | `src/JsonNotepad.Controls/Tree` |
| Virtualized text view and lexer | `src/JsonNotepad.Controls/Text` |
| Error list | `src/JsonNotepad.Controls/Errors` |
| Themes, tab strip | `src/JsonNotepad.Controls/Theming` |
| Icons | `src/JsonNotepad.Controls/Icons` |
| Main window (partials by concern) | `src/JsonNotepad.App/Forms/MainForm*.cs` |
| Commands, menus, toolbar | `MainForm.Actions.cs` via `Infrastructure/ActionRegistry.cs` |
| Settings (PropertyGrid-driven) | `src/JsonNotepad.App/Settings` |
| Packaging | `installer/`, `build/`, `tools/IconGen` |

## Conventions

- File-scoped namespaces, `_camelCase` private fields, one public type per file (small related records and event args may share a file).
- Namespaces mirror folders (`JsonNotepad.Core.Editing`, `JsonNotepad.Controls.Tree`, …).
- Use XML doc comments on public types to explain *why*, not *what*.
- User-visible errors from edits come back as `EditResult` or exceptions caught in `MainForm.RunEdit`; never crash on bad user input.
- New user commands:
  1. Register an `AppAction` in `MainForm.RegisterActions`, with text, shortcut, glyph and `CanExecute`.
  2. Place it with `_actions.Menu(id)` / `_actions.Button(id)`.
  3. Shortcuts must not collide; F1 shows the full list.
- New settings: add a property to `AppSettings` with `[Category]`, `[DisplayName]` and `[Description]` (it appears in Options automatically), and use `[Browsable(false)]` for persisted window state.
- When changing Core behavior, add or adjust tests in `tests/JsonNotepad.Core.Tests` (pick the matching `*Tests.cs` class, mark methods `[Test]`).
- Glyph code points must be written as `char.ConvertFromUtf32(0xE8A5)` or `(char)0x2028`, never pasted literally. Invisible characters corrupt diffs.

## Gotchas

- `Utf8JsonReader` is a ref struct. Parsing is synchronous by design and is offloaded with `Task.Run`; don't try to make it `async`.
- `JNode.GetIndex()` is O(siblings). Avoid it in loops over large arrays; use `PreorderCursor` or the row index the tree already has.
- Main-menu shortcuts are processed before the focused control. `MainForm.ProcessCmdKey` and `InPlaceEditor.ProcessCmdKey` deliberately let text-editing keys (Ctrl+C, Delete, …) reach text boxes.
- WinForms DPI autoscaling runs during layout. Restore saved device-pixel window bounds in `OnLoad`, not in the constructor, or they get scaled twice.
- Dark mode uses the experimental `Application.SetColorMode` (`WFO5001`, suppressed locally). Owner-drawn controls take colors from `Theme`, never `SystemColors`.
- Saving is atomic (temp file + `File.Replace`), so tests that inspect directories should expect no leftover `*.tmp` files.
