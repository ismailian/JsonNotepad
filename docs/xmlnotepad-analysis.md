# What JsonNotepad Learned from XML Notepad

JsonNotepad is a JSON viewer and editor for Windows, and its design follows [Microsoft XML Notepad](https://github.com/microsoft/XmlNotepad) on purpose. XML Notepad has been a reliable tool for nearly twenty years. Its source code is published under the MIT License (Copyright (c) Microsoft Corporation), so anyone can read how a fast, keyboard-driven tree editor gets built in WinForms. We are grateful to Chris Lovett and the other contributors for making it public.

This document records what we found in the XML Notepad source and how those findings shaped JsonNotepad. No XML Notepad code was copied. JsonNotepad borrows ideas and structure, and re-implements them for JSON on .NET 9.

All file references point to the XML Notepad repository under `src/`.

---

## 1. XML Notepad's architecture

### 1.1 Project and assembly layering

| Project | Assembly | Target | Responsibility |
|---|---|---|---|
| `src/Model` | `XmlNotepad.Model` | netstandard2.0 | Non-visual services: `XmlCache` (document plus file watching), `UndoManager`/`Command`, `Checker` (schema validation), `SchemaCache`, `Settings`, `DelayedActions`, `RecentFiles`/`MostRecentlyUsed`, `XPathGenerator`, `XmlIntellisenseProvider`, `XInclude`, `Updater`, `AppAnalytics` |
| `src/XmlNotepad` | `Microsoft.XmlNotepad` | .NET Framework 4.8 | Reusable controls: `TreeView`, `XmlTreeView`, `NodeTextView`, `TextEditorOverlay`, `TaskList`, `XsltControl`/`AsyncXslt`, `Commands.cs`, `Builders.cs`, `XmlTreeViewFindTarget` |
| `src/Application` | `XmlNotepad` (WinExe) | .NET Framework 4.8 | Shell: `FormMain`, `FormSearch`, `FormOptions`, `FormSchemas`, `FormCsvImport`, the XML diff wrapper, the about and analytics dialogs |
| `XmlNotepadSetup`, `XmlNotepadBundle`, `XmlNotepadPackage` | — | WiX / MSIX | Installers |
| `UnitTests`, `WindowsInput` | — | — | UI automation tests that drive the real app with simulated input |

Dependencies run in one direction only: Application → XmlNotepad → Model. The Model project has no WinForms reference, which is why it can target netstandard2.0. The split is not perfectly clean, though. The edit commands in `Commands.cs` (`InsertNode`, `MoveNode`, and the rest) sit in the *controls* assembly, because each one changes the `XmlDocument` and the matching `XmlTreeNode` together. As a result, undoable editing cannot be tested without the tree control.

### 1.2 Why a custom TreeView

The header comment in `TreeView.cs` gives the reasons directly. The stock `System.Windows.Forms.TreeView` keeps its own copy of every node, and it has "limitations … like maximum of height of 32k pixels". XML Notepad's `TreeView` is a `UserControl` with `UserPaint`, `OptimizedDoubleBuffer` and `AllPaintingInWmPaint` enabled, and it reads its nodes from the model through the abstract `TreeNode` and `TreeNodeCollection` classes.

- **Lazy population.** `XmlTreeNodeCollection.Populate()` (in `XmlTreeView.cs`) creates the `XmlTreeNode` wrappers for an element's attributes and child nodes the first time something touches `Count`, the indexer or the enumerator. A collapsed subtree that is never opened costs no wrapper objects.
- **Layout.** `TreeView.OnLayout` calls `LayoutNodes`, which walks every *expanded* node, calls `TreeNode.Layout`, measures each label with `Graphics.MeasureString`, and accumulates `VirtualHeight` and `VirtualWidth`. Layout cost therefore grows with the number of expanded nodes, not with the window size.
- **Paint.** `OnPaint` shifts the graphics transform by `ScrollPosition` and calls `DrawNodes` recursively. Each node is drawn only if its row intersects the clip rectangle, and the walk returns as soon as `y > clip.Bottom`. The walk still starts at the top of the tree, so nodes above the viewport are visited even though they are not drawn.
- **Scrolling.** The control has no native scrollbars. `XmlTreeView` owns `vScrollBar1` and `hScrollBar1`, and `SyncScrollbars()` converts `VirtualHeight / ItemHeight` into scroll units. Scrolling then sets `ScrollPosition` on both child views.
- **Batching.** `BeginUpdate`/`EndUpdate` keep an `updateDepth` counter, so operations such as `ExpandAll` run one layout instead of one per node.
- **Accessibility.** `AccessibleTree` and `AccessibleNode` expose the owner-drawn rows to UI Automation. The `UnitTests` project relies on this.

### 1.3 NodeTextView and shared scrolling

The value column is a separate `NodeTextView` control. It shares the same `TreeNodeCollection` (`_myTreeView.Nodes = _nodeTextView.Nodes = nodes`) and sits to the right of a draggable `Resizer`. `NodeTextView.PaintNodes` repeats the tree's clip-and-stop walk. The two views stay row-aligned because `XmlTreeView.ScrollPosition` and `SyncScrollbars` always set the same Y offset on both, and `HandleMouseWheel` is attached to each.

### 1.4 TextEditorOverlay

Neither view creates a control per row. Each one owns a single `TextEditorOverlay`: one borderless multiline `TextBox`, hidden until editing starts and then positioned over the label through the `LayoutEditor` event. The overlay also hosts a `CompletionSet` (IntelliSense drop-down) and can switch to a custom `IXmlEditor` such as `DateTimeEditor`, or to an `IXmlBuilder` such as `ColorBuilder` or `UriBuilder` (`Builders.cs`). On commit it raises `CommitEdit`, and the owning view turns that into an undoable command.

### 1.5 Commands and undo

`UndoManager` (in `Model/UndoManager.cs`) keeps a `List<Command>` and a position. `Push` throws away the redo tail, drops the oldest entry once the stack exceeds its maximum (`FormMain` uses 1000), and runs `cmd.Do()`. `Command` is a small abstract class (`Do`, `Undo`, `Redo`, `IsNoop`, `Name`). `CompoundCommand` runs its children in order and undoes them in reverse.

`Commands.cs` contains the concrete commands: `EditNodeName`, `EditElementName`, `EditAttributeName`, `EditNodeValue`, `InsertNode`, `DeleteNode`, `ChangeNode` (change node type), `MoveNode` (move or copy, including drag/drop), `NudgeNode` (Up/Down/Left/Right, built on top of `MoveNode`), and `CutCommand`/`PasteCommand` with the `TreeData` clipboard `IDataObject`. `TreeParent` hides an awkward detail of the XML DOM: attributes have no `ParentNode`, and root-level tree nodes have no `Parent`.

The dirty flag is a plain `bool` in `XmlCache`. Any `NodeChanged`, `NodeInserted` or `NodeRemoved` event on the `XmlDocument` sets it. Undoing back to the saved state therefore still shows the document as modified.

### 1.6 DelayedActions

`DelayedActions` is a named debouncer. `StartDelayedAction(name, action, delay)` restarts a `System.Threading.Timer` for that name, and when the timer fires, the action goes through a `DispatchHandler` (in practice `Control.BeginInvoke`) back to the UI thread. It is used throughout the code base: settings saves (`"DelaySaveSettings"`), XSLT refresh (`"UpdateXslt"`, 0.5 s), file reloads (`"reload"`, 1 s), status clearing, and update checks. One quirk is that the dispatcher is stored in a `static` field, which makes the class global in practice.

### 1.7 XmlCache and file watching

`XmlCache` owns the `XmlDocument`, the `DomLoader` (which records line and column information per node so errors can be located), the `SchemaCache`, and a `FileSystemWatcher` on the file's directory. `FileChangedActions` deals with editors that save in unusual ways, for example "rename to `.bak`, then write" or "write `.new`, then rename". It collects related events and waits until they settle (1 s through `DelayedActions`). It then decides between a reload (the final name equals the original) and a rename. `CheckReload` acts only when `LastWriteTime` is newer than the time recorded at load, and it opens the file with `FileShare.ReadWrite` first to make sure the writer has finished. `SaveCopy` stops the watcher, serializes into a `MemoryStream`, and calls `File.WriteAllBytes`. A serialization exception therefore cannot wipe the file on disk, although the write itself is not atomic.

### 1.8 Validation, Checker, and the error list

`Checker` validates the DOM with `XmlSchemaValidator`, using the schemas found through `SchemaCache` and its `SchemaResolver`. It also stores per-node `XmlSchemaInfo` in `TypeInfoMap` for IntelliSense. `TaskList` shows the errors in a `DataGridView`, and `Navigate` jumps to the node involved.

**Validation runs synchronously.** `FormMain.OnModelChanged` calls `_model.ValidateModel(handler)` on the UI thread after every model change, except inside a batch. On a large document, every edit pays for a full validation pass.

`SchemaCache` maps target namespaces and source URIs to `CacheEntry` objects, keeps the list across sessions (it implements `IXmlSerializable`), and is managed through `FormSchemas`.

### 1.9 Settings and recent files

`Settings` is a typed property bag saved as `XmlNotepad.settings`. Its location can be Portable, Local, Roaming, or one of several other modes (`SettingsLocation`). Values can be anything that has a `TypeConverter`, plus `Hashtable`, arrays, or `IXmlSerializable`. The class watches its own file, so several running instances stay in sync. `PersistentFileNames` stores paths relative to the install directory. `RecentFiles` (up to 1000 URIs) and `MostRecentlyUsed` (combo-box history such as find terms) are saved through `Settings`, with writes debounced.

### 1.10 IntelliSense, Find, and XPath

- `IIntellisenseProvider` (implemented by `XmlIntellisenseProvider`) answers `GetExpectedNames`/`GetExpectedValues` from the schema, using a context-specific `Checker`. `IntelliTip` shows annotation tooltips on hover.
- `IFindTarget` (in `Model/FindTarget.cs`) separates `FormSearch` from the tree. `XmlTreeViewFindTarget` runs `//node()` (or the user's XPath), stores *every* match in a `List<XmlNodeMatch>`, and drops the list on `ModelChanged`. To locate the start position it scans that list for the selected node. `FindFlags` supports Regex, XPath, MatchCase, WholeWord, and Backwards. `ReplaceCurrent` performs the replace.
- `XPathGenerator` builds a namespace-aware XPath for a node, adding positional indexes only where siblings share a name. It powers "Copy XPath" and the find dialog's location box.

### 1.11 XSLT output and async usage

`XsltViewer` and `XsltControl` render transform output in WebView2. `AsyncXslt.TransformDocumentAsync` runs the transform under `Task.Run`, writes to a temp file through a `ProgressiveStream` that reports progress, and supports cancellation. Its input is the live `_model.Document`. No snapshot is taken.

Elsewhere, `FormMain` uses `async` mainly at the edges: `FileEntity.Fetch` (local or HTTP), the update check, and WebView2 start-up. The parse itself (`XmlCache.Load`, then `DomLoader.Load`) runs on the UI thread. As an aside, `FormMain.ImportJson` already opens JSON by converting it to XML with `JsonConvert.DeserializeXmlNode`. That conversion loses JSON's types and array structure, which is part of why a native JSON editor is worth building.

### 1.12 Performance techniques and limitations

**What works well:**
- lazy wrapper creation
- painting only the clipped region
- double buffering
- update batching
- debounced background-timer work
- a single in-place editor
- in-memory serialization before writing to disk
- XSLT on the thread pool

**Limitations we observed:**
- The full `XmlDocument` DOM is in memory, plus a `Dictionary<XmlNode, LineInfo>` and one `XmlTreeNode` per populated node.
- Layout measures every expanded node's label. `ExpandAll` on a large file is slow.
- Painting starts at the root, so scrolling deep into a long expanded list costs O(rows above).
- Parsing, validation (after every edit), find-list construction, and saving all run on the UI thread.
- The dirty flag does not track save points.
- The background XSLT reads a DOM that the UI thread can modify at the same time.

### 1.13 Packaging and updates

- **WiX MSI** (`XmlNotepadSetup/Product.wxs`): per-machine install, `MajorUpgrade`, Start-menu shortcut.
- **WiX bundle** (`XmlNotepadBundle/Bundle.wxs`): chains the `NetFx48Web` installer with the MSI and shows the RTF license.
- **MSIX** (`XmlNotepadPackage.wapproj`, `Package.appxmanifest`): full-trust desktop app with `.xml` file-type associations, published to the Store and winget (see `publish.cmd`).
- **ClickOnce** (`Application.csproj`: `IsWebBootstrapper`, `InstallUrl`, `SignManifests`).
- **Updater** (`Model/Updater.cs`): downloads `Updates.xml` (whose schema is `Updates.xsd`) on a configured schedule, compares version numbers, and shows an update button. The same file doubles as release notes, rendered through `Updates.xslt`.

---

## 2. How JsonNotepad adapts each idea

JsonNotepad has three projects that mirror XML Notepad's layering, but draws the Model boundary more strictly:

- **`JsonNotepad.Core`** (`net9.0`, no UI) holds the model, I/O, editing commands, paths, search, validation, services and session.
- **`JsonNotepad.Controls`** (`net9.0-windows`) holds the owner-drawn tree, text view, error list, theme and icons.
- **`JsonNotepad.App`** (WinExe) is the shell.

### 2.1 Mapping

| XML Notepad | JsonNotepad | What changed |
|---|---|---|
| Model / XmlNotepad / Application assemblies | `JsonNotepad.Core` / `JsonNotepad.Controls` / `JsonNotepad.App` | Commands moved into Core (`Editing/`), so undoable edits are UI-free and unit-testable. |
| `XmlDocument` DOM | `Model/JNode` + `JDocument`, an observable aggregate with change events | Built for JSON; the controls subscribe to model events instead of the DOM's `NodeChanged`. |
| `XmlReader` + `DomLoader` line table | `IO` streaming parser on `System.Text.Json` `Utf8JsonReader`, fed 1 MB blocks; `NameTable` interns keys through the .NET 9 `Dictionary` alternate lookup | Repeated keys share one string, and the parser never needs the whole file as a single `string`. |
| `XmlWriter` save, `MemoryStream` then `File.WriteAllBytes` | Hand-written writer that keeps the original number literals and produces a node→line map; atomic saver (temp file + `File.Replace`); encoding/BOM/indent/newline detection | The original file stays intact if the write fails partway, and round-trips keep numeric text and formatting conventions. |
| `TreeView` + `XmlTreeNodeCollection.Populate` (one wrapper per node) | `JsonTreeView`: one owner-drawn control, virtualized through a flattened list of visible rows (a 16-byte struct) plus a set of expanded nodes | No per-node wrapper objects. Hit-testing and painting index straight into the visible-row list, so scrolling cost does not depend on how many rows lie above the viewport. |
| Separate `NodeTextView` + `Resizer` + `SyncScrollbars` | Name and value columns drawn by the same `JsonTreeView`, with a draggable divider | A single control has a single scroll offset, so the two columns cannot drift apart. |
| `TextEditorOverlay` | One in-place `TextBox` editor | Same idea: a single editor control, positioned over the cell being edited. |
| `UndoManager`, `Command`, `CompoundCommand` | `Editing/UndoManager` with sequence-id save points; `CompoundCommand` | Undoing back to the saved state clears the dirty flag. |
| `InsertNode`, `DeleteNode`, `EditNodeName`, `EditNodeValue`, `ChangeNode`, `MoveNode`, `NudgeNode`, `PasteCommand` | Insert / Delete / Rename / EditValue / Replace / Move / Reorder commands; `NodeEditor` facade (including nudge); `KindConverter` for change-type; paste of member fragments | Same command set, behind one facade the UI calls. |
| `DelayedActions` (static dispatcher, `Control.BeginInvoke`) | `Services/DelayedActions` | Instance-based; marshals back through `SynchronizationContext`. |
| `XmlCache` + `FileSystemWatcher` + `FileChangedActions` | `Services/FileChangeWatcher` | Same goal: absorb bursts from save-by-rename editors and reload only when the file really changed. |
| `Checker` + `SchemaCache` + `TaskList` (`DataGridView`) | `Validation/JsonSchemaValidator` (a dependency-free subset of drafts 4 through 2020-12) + `SchemaValidationService`; `ErrorListView` (a virtual `ListView`) | Validation runs in the background on a snapshot, not synchronously after every edit. The virtual list handles large error counts. |
| `Settings`, `RecentFiles`, `FormOptions` (a `PropertyGrid`) | `Services/RecentFiles`; `PropertyGrid` options dialog in the App | |
| `IFindTarget`, `XmlTreeViewFindTarget`, `FormSearch` | `Search/FindEngine` with `PreorderCursor` (O(1) amortized traversal); find/replace dialog | Matches are found incrementally from the cursor, not collected up front into a list. |
| `XPathGenerator` | `Paths/JsonPath`, build and resolve | Both directions: copy a path, and navigate to one. |
| `DomLoader` line info | `JsonTextView`: virtualized, syntax-highlighted, read-only text with a line gutter; the writer's node→line map | |
| `XmlStats` tool | `Services/DocumentStatistics` + statistics dialog | |
| `XmlCache` as the per-document hub | `Session/DocumentSession` | |
| Menu handlers spread across `FormMain` | Menus and toolbars bound through a central action registry in `MainForm` | |
| Bitmap `ImageList` icons, light/dark `ThemeColors` | Glyph icons from the Segoe Fluent Icons / MDL2 Assets fonts; `Theme` (light/dark) | Glyphs scale with DPI and theme colors. |

### 2.2 Threading policy

XML Notepad's main performance limit is that most work runs on the UI thread. JsonNotepad sets explicit rules:

- **The UI thread owns the model.** All `JDocument` changes go through commands on the UI thread.
- **Background work runs on the thread pool**, against either a *deep-cloned snapshot* or a document that has not yet been published to the UI. This covers parsing on load, save serialization, text-view generation, schema validation, and statistics. The design avoids the case where XML Notepad's `AsyncXslt` reads a DOM that the UI may be modifying.
- **A version counter discards stale results.** A validation or text-view result produced from an older version is ignored, never applied.
- **`DelayedActions` marshals back through `SynchronizationContext`**, so debounced callbacks always run on the UI thread.

### 2.3 Intentionally not ported

- **XSLT output tab, WebView2 hosting**: these have no clear JSON counterpart, and they would add a large runtime dependency.
- **XInclude, DTD handling**: XML-specific.
- **IntelliSense from XSD** (`XmlIntellisenseProvider`, `Builders.cs` editors): JSON Schema is used for validation only, at least for now.
- **XML diff** (`XmlDiffWrapper`)
- **CSV/HTML import**
- **Analytics** (`AppAnalytics`, `FormAnalytics`) and the **built-in updater** (`Updater`, `Updates.xml`): JsonNotepad contains no telemetry or self-update code.

---

## 3. Summary

XML Notepad shows that WinForms can deliver a responsive tree editor if the control owns its painting and the model drives the view. JsonNotepad keeps that core: an owner-drawn tree, a single in-place editor, a command-based undo stack, named debouncing, and resilient handling of changes on disk. It changes three things for large JSON files:

1. The tree uses a flat list of visible rows instead of one wrapper object per node.
2. Parsing, saving, validation and other heavy work run on snapshots off the UI thread.
3. Editing logic lives in a UI-free Core library.

Thank you again to the XML Notepad authors for publishing their work under the MIT License.
