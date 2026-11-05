# Performance and large files

## What makes JsonNotepad fast

| Technique | Effect |
|---|---|
| Streaming `Utf8JsonReader` in 1 MB blocks | Memory for the *input* is constant; the parse runs on the thread pool with progress and cancellation |
| `NameTable` atomization of keys and short values | Large arrays of records keep one copy of each key string |
| Compact model (~56 B per value, lazy child lists, literal numbers) | Several million values fit comfortably in memory |
| Virtualized tree (flattened visible rows; no per-node UI objects) | Opening, scrolling and painting cost O(rows on screen) |
| Virtualized text view (one string + line-start index) | An 11-million-line document scrolls like a small one |
| Snapshot-based background work | Validation, formatting, statistics and saving never block typing |
| Debouncing (`DelayedActions`) | Bursts of edits trigger one re-validation |
| Large-file mode | Above the threshold (Options ▸ *Large file threshold*, 20 MB by default), only the root is expanded and a cancellable progress bar is shown |
| Atomic, buffered saves (1 MB buffers) | Saving runs at disk speed and is crash-safe |

## Measurements

These were measured on the development machine (Windows 11, .NET 9.0, Release build). Treat them as relative numbers: they vary by CPU and disk.

**141 MB, 8.1 million values** (600,000 records × 9 fields with nested objects and arrays):

| Operation | Time / size |
|---|---|
| Tokenize only (`Utf8JsonReader`, no tree) | ~0.3 s |
| Load into the tree, workstation GC | ~6.5 s |
| Load into the tree, server GC with DATAS (shipping configuration) | ~4.1 s |
| Process working set after load | ~1.0 GB |
| Text View formatting (≈11 M lines) | a few seconds, in the background |

**Test-suite perf smoke test** (34 MB, 3.0 M values): load 2.0–2.7 s; deep clone ~1 s; save ~0.7 s (80–110 MB/s); text formatting ~1.2 s; find in last record ~0.45 s; statistics ~0.2 s.

### Where the time goes

Tokenizing is fast. Roughly 90% of load time is spent allocating and garbage-collecting the ~16 million long-lived objects (nodes and strings) that make up an editable tree.

To confirm this, a stripped-down replica builder with no interning and no duplicate checks was just as slow. A generational GC has to promote every one of these objects, and that dominates.

| Mitigation | Status |
|---|---|
| Server GC (`<ServerGarbageCollection>true</ServerGarbageCollection>`) | Shipping. Cuts load time ~40%. Since .NET 9, server GC uses DATAS (Dynamic Adaptation To Application Sizes), so small documents keep a small heap |
| A larger gen0 budget (`DOTNET_GCgen0size`) | Measured, rejected. Helped a further 10–25% but raises idle memory for every user |

**Next step:** a struct-of-arrays model (node table + string pool) would roughly halve memory and load time, at the cost of a much more complex editing layer. It is tracked as future work.

## Practical limits

| Size | Behavior |
|---|---|
| Up to a few hundred MB | Comfortable on a 16 GB machine |
| Above 400 MB on disk | The Text View declines to render the whole document; use the tree |
| *Expand All* above 250,000 nodes | Asks for confirmation (configurable) |
| *Edit as JSON* above 500,000 nodes in a subtree | Refused; edit in the tree instead |
| Snapshot work (validation, text view, statistics) | Temporarily doubles node memory |

## Measuring yourself

```powershell
dotnet run --project tests/JsonNotepad.Core.Tests -c Release -- --category perf
```

The app also logs load timings to `%LOCALAPPDATA%\JsonNotepad\logs`, and the status bar shows node count, size, time and MB/s after every open.
