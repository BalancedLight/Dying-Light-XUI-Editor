using System.Security.Cryptography;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Documents;

public sealed record XuiDocumentOptions(IReadOnlyList<string>? ProtectedRoots = null)
{
    public IReadOnlyList<string> NormalizedProtectedRoots { get; } =
        (ProtectedRoots ?? [])
        .Select(static path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public enum XuiSaveDisposition
{
    Unchanged,
    Saved,
}

public sealed record XuiSaveResult(
    XuiSaveDisposition Disposition,
    string Path,
    string? BackupPath);

public sealed class XuiDocument
{
    private readonly XuiSyntaxParser _parser;
    private readonly XuiDocumentOptions _options;
    private string _baselineText;
    private FileFingerprint? _openedFingerprint;

    private XuiDocument(
        XuiSyntaxParser parser,
        XuiSyntaxTree syntaxTree,
        string? path,
        XuiDocumentOptions options,
        FileFingerprint? openedFingerprint)
    {
        _parser = parser;
        _options = options;
        SyntaxTree = syntaxTree;
        Path = path;
        _baselineText = syntaxTree.Source;
        _openedFingerprint = openedFingerprint;
        History = new XuiCommandHistory(this);
    }

    public string? Path { get; private set; }

    public XuiSyntaxTree SyntaxTree { get; private set; }

    public XuiSyntaxNode Root => SyntaxTree.Root;

    public XuiTextFormat Format => SyntaxTree.Format;

    public string Text => SyntaxTree.Source;

    public bool IsDirty => !string.Equals(Text, _baselineText, StringComparison.Ordinal);

    public long Revision { get; private set; }

    public XuiCommandHistory History { get; }

    public event EventHandler? Changed;

    public static async Task<XuiDocument> OpenAsync(
        string path,
        XuiDocumentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = System.IO.Path.GetFullPath(path);
        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        XuiSyntaxParser parser = new();
        XuiSyntaxTree tree = parser.Parse(bytes);
        FileFingerprint fingerprint = await FileFingerprint.CreateAsync(
            fullPath,
            cancellationToken).ConfigureAwait(false);
        return new XuiDocument(
            parser,
            tree,
            fullPath,
            options ?? new XuiDocumentOptions(),
            fingerprint);
    }

    public static XuiDocument FromText(
        string text,
        XuiDocumentOptions? options = null,
        XuiTextFormat? format = null)
    {
        XuiSyntaxParser parser = new();
        XuiSyntaxTree tree = parser.Parse(text, format);
        return new XuiDocument(
            parser,
            tree,
            path: null,
            options ?? new XuiDocumentOptions(),
            openedFingerprint: null);
    }

    public void Execute(IXuiCommand command) => History.Execute(command);

    public void Undo() => History.Undo();

    public void Redo() => History.Redo();

    public async Task<XuiSaveResult> SaveAsync(
        string? targetPath = null,
        CancellationToken cancellationToken = default)
    {
        string resolvedPath = ResolveTargetPath(targetPath);
        EnsureWritablePath(resolvedPath);

        bool samePath = Path is not null &&
                        string.Equals(Path, resolvedPath, StringComparison.OrdinalIgnoreCase);
        if (!IsDirty && samePath)
        {
            return new XuiSaveResult(XuiSaveDisposition.Unchanged, resolvedPath, null);
        }

        if (samePath && _openedFingerprint is not null && File.Exists(resolvedPath))
        {
            FileFingerprint current = await FileFingerprint.CreateAsync(
                resolvedPath,
                cancellationToken).ConfigureAwait(false);
            if (current != _openedFingerprint)
            {
                throw new IOException(
                    "The XUI file changed on disk after it was opened. Save As or reload it before saving.");
            }
        }

        string directory = System.IO.Path.GetDirectoryName(resolvedPath)
            ?? throw new IOException("The target XUI path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(resolvedPath)}.{Guid.NewGuid():N}.tmp");
        string backupPath = resolvedPath + ".bak";
        byte[] bytes = Format.Encode(Text);

        try
        {
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            string? createdBackup = null;
            if (File.Exists(resolvedPath))
            {
                File.Replace(
                    temporaryPath,
                    resolvedPath,
                    backupPath,
                    ignoreMetadataErrors: true);
                createdBackup = backupPath;
            }
            else
            {
                File.Move(temporaryPath, resolvedPath);
            }

            Path = resolvedPath;
            _baselineText = Text;
            _openedFingerprint = await FileFingerprint.CreateAsync(
                resolvedPath,
                cancellationToken).ConfigureAwait(false);
            History.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
            return new XuiSaveResult(
                XuiSaveDisposition.Saved,
                resolvedPath,
                createdBackup);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal void ApplyValidatedEdit(int start, string expected, string replacement)
    {
        if (start < 0 || start > Text.Length ||
            expected.Length > Text.Length - start ||
            !Text.AsSpan(start, expected.Length).SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                "The edit no longer matches the current XUI document revision.");
        }

        string candidate = string.Concat(
            Text.AsSpan(0, start),
            replacement,
            Text.AsSpan(start + expected.Length));
        XuiSyntaxTree candidateTree = _parser.Parse(candidate, Format);
        SyntaxTree = candidateTree;
        Revision++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string ResolveTargetPath(string? targetPath)
    {
        string? selected = targetPath ?? Path;
        if (string.IsNullOrWhiteSpace(selected))
        {
            throw new InvalidOperationException(
                "This document has no path. Choose a writable location with Save As.");
        }

        return System.IO.Path.GetFullPath(selected);
    }

    private void EnsureWritablePath(string path)
    {
        foreach (string protectedRoot in _options.NormalizedProtectedRoots)
        {
            string relative = System.IO.Path.GetRelativePath(protectedRoot, path);
            bool outside = relative.Equals("..", StringComparison.Ordinal) ||
                           relative.StartsWith(
                               ".." + System.IO.Path.DirectorySeparatorChar,
                               StringComparison.Ordinal);
            if (!outside && !System.IO.Path.IsPathRooted(relative))
            {
                throw new UnauthorizedAccessException(
                    $"'{path}' is inside the protected asset root '{protectedRoot}'. Use Save As into a writable workspace.");
            }
        }
    }

    private sealed record FileFingerprint(long Length, DateTime LastWriteTimeUtc, string Sha256)
    {
        public static async Task<FileFingerprint> CreateAsync(
            string path,
            CancellationToken cancellationToken)
        {
            FileInfo info = new(path);
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            info.Refresh();
            return new FileFingerprint(
                info.Length,
                info.LastWriteTimeUtc,
                Convert.ToHexString(hash));
        }
    }
}
