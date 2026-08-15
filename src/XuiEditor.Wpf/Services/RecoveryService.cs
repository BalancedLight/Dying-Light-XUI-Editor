using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using XuiEditor.Core.Documents;

namespace XuiEditor.Wpf.Services;

public sealed record RecoverySnapshot(
    string MetadataPath,
    string ContentPath,
    string? OriginalPath,
    DateTime TimestampUtc)
{
    public string? RecoveryKey { get; init; }

    public string? DisplayName { get; init; }

    public string? SourceOrigin { get; init; }

    public string? SourceVirtualPath { get; init; }
}

public static class RecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string RecoveryDirectory =>
        Path.Combine(EditorSettingsStore.ApplicationDirectory, "Recovery");

    public static async Task<RecoverySnapshot> WriteAsync(
        XuiDocument document,
        CancellationToken cancellationToken = default) =>
        await WriteAsync(
            document,
            RecoveryDirectory,
            recoveryKey: null,
            displayName: null,
            sourceOrigin: null,
            sourceVirtualPath: null,
            cancellationToken).ConfigureAwait(false);

    public static async Task<RecoverySnapshot> WriteAsync(
        XuiDocument document,
        string recoveryKey,
        string? displayName,
        string? sourceOrigin,
        string? sourceVirtualPath,
        CancellationToken cancellationToken = default) =>
        await WriteAsync(
            document,
            RecoveryDirectory,
            recoveryKey,
            displayName,
            sourceOrigin,
            sourceVirtualPath,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<RecoverySnapshot> WriteAsync(
        XuiDocument document,
        string recoveryDirectory,
        CancellationToken cancellationToken = default) =>
        await WriteAsync(
            document,
            recoveryDirectory,
            recoveryKey: null,
            displayName: null,
            sourceOrigin: null,
            sourceVirtualPath: null,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<RecoverySnapshot> WriteAsync(
        XuiDocument document,
        string recoveryDirectory,
        string? recoveryKey,
        string? displayName,
        string? sourceOrigin,
        string? sourceVirtualPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);
        Directory.CreateDirectory(recoveryDirectory);
        string identity = recoveryKey ?? document.Path ?? "untitled";
        string key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        string contentPath = Path.Combine(recoveryDirectory, key + ".xui");
        string metadataPath = Path.Combine(recoveryDirectory, key + ".json");
        string contentTemporary = contentPath + $".{Guid.NewGuid():N}.tmp";
        string metadataTemporary = metadataPath + $".{Guid.NewGuid():N}.tmp";
        DateTime timestamp = DateTime.UtcNow;
        try
        {
            await File.WriteAllBytesAsync(
                contentTemporary,
                document.Format.Encode(document.Text),
                cancellationToken).ConfigureAwait(false);
            string metadata = JsonSerializer.Serialize(
                new RecoveryMetadata(
                    document.Path,
                    timestamp,
                    recoveryKey,
                    displayName ?? document.DisplayName,
                    sourceOrigin ?? document.Source?.Origin,
                    sourceVirtualPath ?? document.Source?.VirtualPath),
                JsonOptions);
            await File.WriteAllTextAsync(
                metadataTemporary,
                metadata,
                cancellationToken).ConfigureAwait(false);
            File.Move(contentTemporary, contentPath, overwrite: true);
            File.Move(metadataTemporary, metadataPath, overwrite: true);
        }
        finally
        {
            DeleteIfPresent(contentTemporary);
            DeleteIfPresent(metadataTemporary);
        }

        return new RecoverySnapshot(
            metadataPath,
            contentPath,
            document.Path,
            timestamp)
        {
            RecoveryKey = recoveryKey,
            DisplayName = displayName ?? document.DisplayName,
            SourceOrigin = sourceOrigin ?? document.Source?.Origin,
            SourceVirtualPath = sourceVirtualPath ?? document.Source?.VirtualPath,
        };
    }

    public static IReadOnlyList<RecoverySnapshot> Find() =>
        Find(RecoveryDirectory);

    internal static IReadOnlyList<RecoverySnapshot> Find(
        string recoveryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);
        if (!Directory.Exists(recoveryDirectory))
        {
            return [];
        }

        List<RecoverySnapshot> snapshots = [];
        foreach (string metadataPath in Directory.EnumerateFiles(
                     recoveryDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                RecoveryMetadata? metadata = JsonSerializer.Deserialize<RecoveryMetadata>(
                    File.ReadAllText(metadataPath),
                    JsonOptions);
                string contentPath =
                    Path.ChangeExtension(metadataPath, ".xui") ??
                    metadataPath + ".xui";
                if (metadata is not null && File.Exists(contentPath))
                {
                    snapshots.Add(new RecoverySnapshot(
                        metadataPath,
                        contentPath,
                        metadata.OriginalPath,
                        metadata.TimestampUtc)
                    {
                        RecoveryKey = metadata.RecoveryKey,
                        DisplayName = metadata.DisplayName,
                        SourceOrigin = metadata.SourceOrigin,
                        SourceVirtualPath = metadata.SourceVirtualPath,
                    });
                }
            }
            catch (JsonException)
            {
                // A partial or damaged recovery is ignored without touching source files.
            }
            catch (IOException)
            {
                // The recovery folder may be concurrently updated by another editor.
            }
        }

        return snapshots
            .OrderByDescending(static snapshot => snapshot.TimestampUtc)
            .ToArray();
    }

    public static void Delete(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        DeleteIfPresent(snapshot.MetadataPath);
        DeleteIfPresent(snapshot.ContentPath);
    }

    public static void DeleteForPath(string? path)
    {
        string identity = path ?? "untitled";
        string key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        DeleteIfPresent(Path.Combine(RecoveryDirectory, key + ".json"));
        DeleteIfPresent(Path.Combine(RecoveryDirectory, key + ".xui"));
    }

    private static void DeleteIfPresent(string path)
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
            // Recovery cleanup is best-effort and must never endanger source files.
        }
    }

    private sealed record RecoveryMetadata(
        string? OriginalPath,
        DateTime TimestampUtc,
        string? RecoveryKey = null,
        string? DisplayName = null,
        string? SourceOrigin = null,
        string? SourceVirtualPath = null);
}
