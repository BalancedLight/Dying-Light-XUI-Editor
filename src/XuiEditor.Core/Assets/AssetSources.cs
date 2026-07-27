using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Assets;

public enum XuiAssetContainerKind
{
    LooseFile,
    ZipPak,
    Rp6Resource,
}

public sealed record XuiAssetOrigin(
    XuiAssetContainerKind Kind,
    string SourceName,
    string ContainerPath,
    string EntryPath,
    bool IsReadOnly,
    int Priority)
{
    public string DisplayPath =>
        Kind == XuiAssetContainerKind.LooseFile
            ? ContainerPath
            : $"{ContainerPath}::{EntryPath}";
}

public sealed class XuiAssetEntry
{
    private readonly Func<CancellationToken, ValueTask<byte[]>> _reader;

    public XuiAssetEntry(
        string virtualPath,
        long length,
        XuiAssetOrigin origin,
        Func<CancellationToken, ValueTask<byte[]>> reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        VirtualPath = NormalizeVirtualPath(virtualPath);
        Length = length;
        Origin = origin;
        _reader = reader;
    }

    public string VirtualPath { get; }

    public string FileName => Path.GetFileName(VirtualPath);

    public long Length { get; }

    public XuiAssetOrigin Origin { get; }

    public ValueTask<byte[]> ReadAllBytesAsync(
        CancellationToken cancellationToken = default) =>
        _reader(cancellationToken);

    public static string NormalizeVirtualPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = path
            .Replace('\\', '/')
            .TrimStart('/');
        if (normalized.Length == 0 ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(static part =>
                part.Length == 0 ||
                part == "." ||
                part == ".." ||
                part.Contains('\0', StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Asset path '{path}' is not a safe relative virtual path.");
        }

        return normalized;
    }
}

public interface IXuiAssetSource
{
    string DisplayName { get; }

    bool IsReadOnly { get; }

    IReadOnlyList<XuiAssetEntry> Entries { get; }

    IReadOnlyList<XuiDiagnostic> Diagnostics { get; }

    Task RebuildAsync(CancellationToken cancellationToken = default);
}
