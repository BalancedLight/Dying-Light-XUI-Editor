using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Assets;

public enum XuiConfiguredAssetSourceKind
{
    TextureDefinitionFile,
    Rp6ResourcePack,
}

public sealed class ConfiguredAssetSource : IXuiAssetSource
{
    private const long MaximumDefinitionSize = 64L * 1024 * 1024;
    private readonly object _gate = new();
    private IReadOnlyList<XuiAssetEntry> _entries = [];
    private IReadOnlyList<XuiDiagnostic> _diagnostics = [];

    public ConfiguredAssetSource(
        string path,
        XuiConfiguredAssetSourceKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        Kind = kind;
        AssetRoot = CreateAssetRoot(Path, kind);
    }

    public string Path { get; }

    public XuiConfiguredAssetSourceKind Kind { get; }

    public string DisplayName => Kind switch
    {
        XuiConfiguredAssetSourceKind.TextureDefinitionFile =>
            $"Texture definitions ({Path})",
        XuiConfiguredAssetSourceKind.Rp6ResourcePack =>
            $"RP6 resource pack ({Path})",
        _ => Path,
    };

    public bool IsReadOnly => true;

    public XuiAssetRoot AssetRoot { get; }

    public IReadOnlyList<XuiAssetEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries;
            }
        }
    }

    public IReadOnlyList<XuiDiagnostic> Diagnostics
    {
        get
        {
            lock (_gate)
            {
                return _diagnostics;
            }
        }
    }

    public async Task RebuildAsync(
        CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<XuiAssetEntry> Entries,
            IReadOnlyList<XuiDiagnostic> Diagnostics) snapshot =
            await Task.Run(
                () => BuildSnapshot(cancellationToken),
                cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _entries = snapshot.Entries;
            _diagnostics = snapshot.Diagnostics;
        }
    }

    private (IReadOnlyList<XuiAssetEntry> Entries,
        IReadOnlyList<XuiDiagnostic> Diagnostics) BuildSnapshot(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(Path))
        {
            return (
                [],
                [
                    new XuiDiagnostic(
                        "XUI-SOURCE001",
                        XuiDiagnosticSeverity.Warning,
                        $"Configured resource source '{Path}' does not exist."),
                ]);
        }

        try
        {
            return Kind switch
            {
                XuiConfiguredAssetSourceKind.TextureDefinitionFile =>
                    BuildTextureDefinitionSource(),
                XuiConfiguredAssetSourceKind.Rp6ResourcePack =>
                    BuildRp6Source(cancellationToken),
                _ => ([], []),
            };
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            UnauthorizedAccessException)
        {
            return (
                [],
                [
                    new XuiDiagnostic(
                        "XUI-SOURCE002",
                        XuiDiagnosticSeverity.Warning,
                        $"Could not index configured resource source '{Path}': {exception.Message}"),
                ]);
        }
    }

    private (IReadOnlyList<XuiAssetEntry> Entries,
        IReadOnlyList<XuiDiagnostic> Diagnostics)
        BuildTextureDefinitionSource()
    {
        string extension = System.IO.Path.GetExtension(Path);
        if (!extension.Equals(".def", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".scr", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A texture-definition source must be a .def or .scr file.");
        }

        FileInfo file = new(Path);
        if (file.Length < 0 || file.Length > MaximumDefinitionSize)
        {
            throw new InvalidDataException(
                $"Texture-definition file size {file.Length:N0} is unsafe.");
        }

        long expectedLength = file.Length;
        string relative = System.IO.Path.GetRelativePath(
            AssetRoot.FullPath,
            Path);
        XuiAssetOrigin origin = new(
            XuiAssetContainerKind.LooseFile,
            file.Name,
            Path,
            relative,
            IsReadOnly: true,
            Priority: 10_000);
        XuiAssetEntry entry = new(
            relative,
            expectedLength,
            origin,
            async cancellationToken =>
            {
                FileInfo current = new(Path);
                if (!current.Exists || current.Length != expectedLength)
                {
                    throw new IOException(
                        $"Texture-definition file '{Path}' changed after indexing.");
                }

                return await File.ReadAllBytesAsync(
                    Path,
                    cancellationToken).ConfigureAwait(false);
            });
        return ([entry], []);
    }

    private (IReadOnlyList<XuiAssetEntry> Entries,
        IReadOnlyList<XuiDiagnostic> Diagnostics) BuildRp6Source(
        CancellationToken cancellationToken)
    {
        if (!System.IO.Path.GetExtension(Path).Equals(
                ".rpack",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "An RP6 source must be a .rpack file.");
        }

        Rp6Reader reader = Rp6Reader.Open(Path);
        List<XuiAssetEntry> entries = [];
        foreach (Rp6ResourceDescriptor resource in reader.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resource.PayloadType != 32 ||
                string.IsNullOrWhiteSpace(resource.Name))
            {
                continue;
            }

            string safeName = resource.Name
                .Replace('\\', '_')
                .Replace('/', '_');
            if (safeName.Length == 0)
            {
                continue;
            }

            string virtualPath = string.Concat(
                "rpack/",
                System.IO.Path.GetFileNameWithoutExtension(Path),
                "/textures/",
                safeName,
                ".dds");
            XuiAssetOrigin origin = new(
                XuiAssetContainerKind.Rp6Resource,
                System.IO.Path.GetFileName(Path),
                Path,
                resource.Name,
                IsReadOnly: true,
                Priority: 10_000);
            entries.Add(new XuiAssetEntry(
                virtualPath,
                length: 0,
                origin,
                async cancellationToken =>
                {
                    byte[] raw = await reader.ReadResourceAsync(
                        resource,
                        cancellationToken).ConfigureAwait(false);
                    return DyingLightDdsBuilder.Build(raw);
                }));
        }

        return (entries, []);
    }

    private static XuiAssetRoot CreateAssetRoot(
        string path,
        XuiConfiguredAssetSourceKind kind)
    {
        if (kind == XuiConfiguredAssetSourceKind.TextureDefinitionFile)
        {
            XuiDocumentAssetContext context =
                XuiDocumentAssetContext.Discover(path);
            return new XuiAssetRoot(
                context.Root.FullPath,
                XuiAssetRootKind.AdditionalTextureDefinitions,
                true);
        }

        string directory = System.IO.Path.GetDirectoryName(path) ??
                           Environment.CurrentDirectory;
        return new XuiAssetRoot(
            directory,
            XuiAssetRootKind.Rp6ResourcePack,
            true);
    }
}
