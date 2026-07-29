using XuiEditor.Core.Values;

namespace XuiEditor.Core.Assets;

public enum XuiCatalogAssetKind
{
    Screen,
    Visual,
    Texture,
    Font,
}

public sealed record XuiCatalogAsset(
    string Name,
    XuiCatalogAssetKind Kind,
    string LogicalPath,
    string SourceDisplayPath,
    bool IsReadOnly,
    bool IsVirtual,
    XuiVector2? LogicalSize,
    XuiResolvedFile? SourceFile)
{
    public string KindLabel => Kind.ToString();

    public string AccessLabel => IsReadOnly ? "Read-only" : "Workspace";

    public string SizeLabel => LogicalSize is XuiVector2 size
        ? FormattableString.Invariant($"{size.X:0.###} × {size.Y:0.###}")
        : string.Empty;
}

public interface IXuiAssetCatalog
{
    long Revision { get; }

    IReadOnlyList<XuiCatalogAsset> Assets { get; }

    Task RebuildAsync(CancellationToken cancellationToken = default);

    Task<string> CopyToWorkspaceAsync(
        XuiCatalogAsset asset,
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}

public sealed class DyingLightXuiAssetCatalog : IXuiAssetCatalog
{
    private readonly DyingLightAssetResolver _resolver;
    private IReadOnlyList<XuiCatalogAsset> _assets = [];

    public DyingLightXuiAssetCatalog(DyingLightAssetResolver resolver)
    {
        _resolver = resolver ??
            throw new ArgumentNullException(nameof(resolver));
        Refresh();
    }

    public long Revision => _resolver.Revision;

    public IReadOnlyList<XuiCatalogAsset> Assets => _assets;

    public async Task RebuildAsync(
        CancellationToken cancellationToken = default)
    {
        await _resolver.RebuildAsync(cancellationToken).ConfigureAwait(false);
        Refresh();
    }

    public async Task<string> CopyToWorkspaceAsync(
        XuiCatalogAsset asset,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (asset.SourceFile is null)
        {
            throw new InvalidOperationException(
                $"{asset.Kind} '{asset.Name}' has no single copyable source file.");
        }

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        Directory.CreateDirectory(root);
        string relative = SafeRelativePath(asset.SourceFile.RelativePath);
        if (relative.Length == 0)
        {
            relative = Path.GetFileName(asset.SourceFile.Path);
        }

        string destination = Path.GetFullPath(Path.Combine(root, relative));
        EnsureContained(root, destination);
        EnsureNoReparseDirectories(root, destination);
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination) ?? root);
        if (File.Exists(destination))
        {
            throw new IOException(
                $"Workspace asset '{destination}' already exists.");
        }

        byte[] content = await asset.SourceFile
            .ReadAllBytesAsync(cancellationToken)
            .ConfigureAwait(false);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporary,
                content,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return destination;
    }

    private void Refresh()
    {
        List<XuiCatalogAsset> assets = [];
        assets.AddRange(_resolver.Files
            .Where(static file => file.RelativePath.EndsWith(
                ".xui",
                StringComparison.OrdinalIgnoreCase))
            .Select(file => new XuiCatalogAsset(
                Path.GetFileNameWithoutExtension(file.RelativePath),
                XuiCatalogAssetKind.Screen,
                file.RelativePath,
                file.DisplayPath,
                file.Root.EffectiveIsReadOnly || file.IsVirtual,
                file.IsVirtual,
                null,
                file)));
        assets.AddRange(_resolver.VisualTemplates.Select(visual =>
        {
            XuiResolvedFile? source = _resolver.ResolveFile(visual.SourcePath);
            return new XuiCatalogAsset(
                visual.Id,
                XuiCatalogAssetKind.Visual,
                visual.SourcePath,
                source?.DisplayPath ?? visual.SourcePath,
                visual.Root.EffectiveIsReadOnly || source?.IsVirtual == true,
                source?.IsVirtual == true,
                null,
                source);
        }));
        assets.AddRange(_resolver.TextureDefinitions.Select(texture =>
        {
            XuiResolvedFile? source =
                _resolver.ResolveFile(texture.DefinitionRelativePath) ??
                _resolver.ResolveFile(texture.DefinitionPath);
            return new XuiCatalogAsset(
                texture.Name,
                XuiCatalogAssetKind.Texture,
                texture.DefinitionRelativePath,
                source?.DisplayPath ?? texture.DefinitionPath,
                texture.DefinitionRoot?.EffectiveIsReadOnly == true ||
                source?.IsVirtual == true,
                source?.IsVirtual == true,
                new XuiVector2(
                    Math.Max(1, texture.SourceRectangle.Width),
                    Math.Max(1, texture.SourceRectangle.Height)),
                source);
        }));
        assets.AddRange(_resolver.FontIds.Select(font =>
            new XuiCatalogAsset(
                font,
                XuiCatalogAssetKind.Font,
                font,
                font,
                true,
                false,
                null,
                null)));
        _assets = assets
            .GroupBy(
                static asset =>
                    $"{(int)asset.Kind}\0{asset.Name}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static asset => asset.Kind)
            .ThenBy(static asset => asset.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string SafeRelativePath(string path)
    {
        string normalized = path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) ||
            normalized.Split(Path.DirectorySeparatorChar)
                .Any(static segment => segment == ".."))
        {
            return Path.GetFileName(normalized);
        }

        return normalized;
    }

    private static void EnsureContained(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        if (relative == ".." ||
            relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                "The workspace destination escapes the configured workspace root.");
        }
    }

    private static void EnsureNoReparseDirectories(
        string root,
        string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent is null)
        {
            return;
        }

        string relative = Path.GetRelativePath(root, parent);
        if (relative == ".")
        {
            return;
        }

        string current = root;
        foreach (string segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) &
                 FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Copy to Workspace does not traverse reparse-point directories.");
            }
        }
    }
}
