using System.Text;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.IO;

/// <summary>
/// Writes documents to disk safely: the content goes to a temporary file in the target
/// directory which then atomically replaces the original, so a crash, full disk or
/// cancellation mid-save can never leave a truncated file behind.
/// </summary>
public static class JsonFileSaver
{
    private const int BufferSize = 1 << 20;

    /// <summary>
    /// Saves a snapshot on a worker thread. Pass a snapshot (<see cref="JDocument.CreateSnapshot"/>)
    /// rather than the live root so the user can keep editing while a large file is written.
    /// </summary>
    public static Task SaveAsync(JNode snapshotRoot, string path, FileFormatInfo format, FormatOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotRoot);
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Task.Run(() => Save(snapshotRoot, path, format, options, cancellationToken), cancellationToken);
    }

    public static void Save(JNode root, string path, FileFormatInfo format, FormatOptions options, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new IOException($"Invalid path: {path}");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan))
            using (var writer = new StreamWriter(stream, CreateEncoding(format), BufferSize))
            {
                JsonDocumentWriter.Write(root, writer, options, null, cancellationToken);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(fullPath))
            {
                try
                {
                    // Preserves the original file's ACLs, attributes and creation time.
                    File.Replace(tempPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
                {
                    // Some file systems (FAT, certain network shares) don't support Replace.
                    File.Move(tempPath, fullPath, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static Encoding CreateEncoding(FileFormatInfo format) => format.Encoding.CodePage switch
    {
        65001 => new UTF8Encoding(format.HasByteOrderMark),
        1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: format.HasByteOrderMark),
        1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: format.HasByteOrderMark),
        12000 => new UTF32Encoding(bigEndian: false, byteOrderMark: format.HasByteOrderMark),
        12001 => new UTF32Encoding(bigEndian: true, byteOrderMark: format.HasByteOrderMark),
        _ => format.Encoding,
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
