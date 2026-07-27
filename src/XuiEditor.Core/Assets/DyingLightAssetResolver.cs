using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Assets;

public sealed class DyingLightAssetResolver : IAssetResolver
{
    private const int CacheHeaderSize = 8;
    private const long MaximumDecodedPixels = 67_108_864;
    private readonly object _gate = new();
    private readonly string _cacheDirectory;
    private readonly Dictionary<string, string> _fontMappings;
    private readonly ConcurrentDictionary<string, Lazy<Task<DecodedImage>>> _decodedImages =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, XuiResolvedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, XuiResolvedFile> _ddsFiles = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, XuiTextureRegion> _textureRegions = new(StringComparer.Ordinal);
    private Dictionary<string, XuiFontDefinition> _fonts = new(StringComparer.Ordinal);
    private Dictionary<string, XuiFontStyle> _fontStyles = new(StringComparer.Ordinal);
    private Dictionary<string, XuiVisualTemplate> _visuals = new(StringComparer.Ordinal);
    private IReadOnlyList<XuiDiagnostic> _diagnostics = [];

    public DyingLightAssetResolver(
        IEnumerable<XuiAssetRoot> roots,
        string? cacheDirectory = null,
        IReadOnlyDictionary<string, string>? fontMappings = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        Roots = roots
            .Select(static root => root with { })
            .DistinctBy(static root => root.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _cacheDirectory = cacheDirectory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DyingLightXuiEditor",
                "Cache",
                "Textures");
        _fontMappings = fontMappings is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(
                fontMappings,
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<XuiAssetRoot> Roots { get; }

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

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        AssetIndexSnapshot snapshot = await Task.Run(
            () => BuildSnapshot(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _files = snapshot.Files;
            _ddsFiles = snapshot.DdsFiles;
            _textureRegions = snapshot.TextureRegions;
            _fonts = snapshot.Fonts;
            _fontStyles = snapshot.FontStyles;
            _visuals = snapshot.Visuals;
            _diagnostics = snapshot.Diagnostics;
        }

        _decodedImages.Clear();
    }

    public XuiResolvedFile? ResolveFile(string pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
        {
            return null;
        }

        string key = NormalizeKey(pathOrName);
        lock (_gate)
        {
            if (_files.TryGetValue(key, out XuiResolvedFile? direct))
            {
                return direct;
            }

            string name = Path.GetFileName(key);
            return _files.GetValueOrDefault(name);
        }
    }

    public XuiTextureRegion? ResolveTextureDefinition(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        lock (_gate)
        {
            return _textureRegions.GetValueOrDefault(imagePath.Trim());
        }
    }

    public XuiVisualTemplate? ResolveVisual(string visualId)
    {
        if (string.IsNullOrWhiteSpace(visualId))
        {
            return null;
        }

        lock (_gate)
        {
            return _visuals.GetValueOrDefault(visualId.Trim());
        }
    }

    public async Task<ResolvedTexture?> ResolveTextureAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        XuiTextureRegion? requested = ResolveTextureDefinition(imagePath);
        if (requested is null)
        {
            return null;
        }

        if (requested.Primitive == XuiTexturePrimitive.TileSet)
        {
            return await ResolveTileSetAsync(
                imagePath,
                requested,
                cancellationToken).ConfigureAwait(false);
        }

        XuiTextureRegion region = requested;
        List<XuiDiagnostic> diagnostics = [];

        string? ddsPath = FindTextureFile(region);
        if (ddsPath is null)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET005",
                XuiDiagnosticSeverity.Warning,
                $"DDS '{region.TextureFile}' for image '{imagePath}' was not found."));
            int placeholderWidth = Math.Clamp(
                Math.Max(1, (int)region.SourceRectangle.Width),
                1,
                512);
            int placeholderHeight = Math.Clamp(
                Math.Max(1, (int)region.SourceRectangle.Height),
                1,
                512);
            return new ResolvedTexture(
                imagePath,
                placeholderWidth,
                placeholderHeight,
                CreatePlaceholderPixels(
                    placeholderWidth,
                    placeholderHeight),
                requested,
                string.Empty,
                string.Empty,
                true,
                diagnostics);
        }

        string sourceHash = await ComputeSha256Async(
            ddsPath,
            cancellationToken).ConfigureAwait(false);
        string cacheHash = ComputeCacheHash(sourceHash, region);
        ResolvedTexture? cached = await ReadCacheAsync(
            cacheHash,
            imagePath,
            requested,
            ddsPath,
            false,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        Lazy<Task<DecodedImage>> lazy = _decodedImages.GetOrAdd(
            ddsPath,
            path => new Lazy<Task<DecodedImage>>(
                () => DecodeDdsAsync(path, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        DecodedImage decoded;
        try
        {
            decoded = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            IOException or
            NotSupportedException or
            FormatException)
        {
            _decodedImages.TryRemove(ddsPath, out _);
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET012",
                XuiDiagnosticSeverity.Warning,
                $"DDS '{ddsPath}' could not be decoded for image '{imagePath}': {exception.Message}"));
            int placeholderWidth = Math.Clamp(
                Math.Max(1, (int)region.SourceRectangle.Width),
                1,
                512);
            int placeholderHeight = Math.Clamp(
                Math.Max(1, (int)region.SourceRectangle.Height),
                1,
                512);
            return new ResolvedTexture(
                imagePath,
                placeholderWidth,
                placeholderHeight,
                CreatePlaceholderPixels(
                    placeholderWidth,
                    placeholderHeight),
                requested,
                ddsPath,
                sourceHash,
                true,
                diagnostics);
        }
        catch
        {
            _decodedImages.TryRemove(ddsPath, out _);
            throw;
        }

        byte[] cropped = Crop(decoded, region, diagnostics);
        int width = Math.Max(1, Math.Min(
            (int)region.SourceRectangle.Width,
            decoded.Width - Math.Max(0, (int)region.SourceRectangle.X)));
        int height = Math.Max(1, Math.Min(
            (int)region.SourceRectangle.Height,
            decoded.Height - Math.Max(0, (int)region.SourceRectangle.Y)));
        await WriteCacheAsync(
            cacheHash,
            width,
            height,
            cropped,
            cancellationToken).ConfigureAwait(false);
        return new ResolvedTexture(
            imagePath,
            width,
            height,
            cropped,
            requested,
            ddsPath,
            cacheHash,
            false,
            diagnostics);
    }

    private async Task<ResolvedTexture?> ResolveTileSetAsync(
        string imagePath,
        XuiTextureRegion requested,
        CancellationToken cancellationToken)
    {
        List<XuiDiagnostic> diagnostics = [];
        List<ResolvedTileTexturePart> resolvedParts = [];
        bool approximation = false;
        IEnumerable<IGrouping<XuiTileRole, XuiTilePart>> roles =
            requested.TileParts.GroupBy(static part => part.Role);
        foreach (IGrouping<XuiTileRole, XuiTilePart> role in roles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            XuiTilePart[] candidates = role
                .OrderByDescending(static part => part.Probability)
                .ToArray();
            if (candidates.Length > 1)
            {
                approximation = true;
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-ASSET004",
                    XuiDiagnosticSeverity.Info,
                    $"Tileset '{imagePath}' has {candidates.Length} runtime variants for {role.Key}; the editor deterministically previews the highest-probability declaration."));
            }

            XuiTilePart selected = candidates[0];
            XuiTextureRegion? selectedRegion =
                ResolveTextureDefinition(selected.RegionName);
            if (selectedRegion is null ||
                selectedRegion.Primitive == XuiTexturePrimitive.TileSet ||
                !string.Equals(
                    selectedRegion.DefinitionPath,
                    requested.DefinitionPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    selectedRegion.TextureFile,
                    requested.TextureFile,
                    StringComparison.OrdinalIgnoreCase))
            {
                approximation = true;
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-ASSET003",
                    XuiDiagnosticSeverity.Warning,
                    $"Tileset '{imagePath}' references unresolved or cross-texture region '{selected.RegionName}' for {selected.Role}."));
                continue;
            }

            ResolvedTexture? resolved = await ResolveTextureAsync(
                selected.RegionName,
                cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                approximation = true;
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-ASSET003",
                    XuiDiagnosticSeverity.Warning,
                    $"Tileset '{imagePath}' could not decode region '{selected.RegionName}' for {selected.Role}."));
                continue;
            }

            diagnostics.AddRange(resolved.Diagnostics);
            approximation |= resolved.IsApproximation;
            int rotationMode = selected.RotationMode;
            if (rotationMode is < 0 or > 6)
            {
                approximation = true;
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-ASSET011",
                    XuiDiagnosticSeverity.Warning,
                    $"Tileset '{imagePath}' uses unknown rotation mode {rotationMode} for '{selected.RegionName}'; the editor leaves it unrotated."));
                rotationMode = 0;
            }

            (int width, int height, byte[] pixels) = TransformTilePixels(
                resolved.Width,
                resolved.Height,
                resolved.BgraPixels,
                rotationMode);
            resolvedParts.Add(new ResolvedTileTexturePart(
                selected.Role,
                selected.RegionName,
                selected.Probability,
                rotationMode,
                width,
                height,
                pixels,
                resolved.SourcePath,
                resolved.ContentHash));
        }

        if (resolvedParts.Count == 0)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET003",
                XuiDiagnosticSeverity.Warning,
                $"Tileset '{imagePath}' has no resolvable tile declarations."));
            return null;
        }

        (int sampleWidth, int sampleHeight, byte[] samplePixels) =
            ComposeTileSample(resolvedParts);
        StringBuilder identity = new();
        _ = identity
            .Append(requested.DefinitionPath)
            .Append('|')
            .Append(requested.Name);
        foreach (ResolvedTileTexturePart part in resolvedParts)
        {
            _ = identity
                .Append('|')
                .Append(part.Role)
                .Append(':')
                .Append(part.RegionName)
                .Append(':')
                .Append(part.Probability)
                .Append(':')
                .Append(part.RotationMode)
                .Append(':')
                .Append(part.ContentHash);
        }

        string contentHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())));
        return new ResolvedTexture(
            imagePath,
            sampleWidth,
            sampleHeight,
            samplePixels,
            requested,
            resolvedParts[0].SourcePath,
            contentHash,
            approximation,
            diagnostics)
        {
            TileParts = resolvedParts,
        };
    }

    public ResolvedFont ResolveFont(
        string fontId,
        double requestedSize,
        IReadOnlyDictionary<string, string>? userMappings = null)
    {
        string requested = fontId?.Trim() ?? string.Empty;
        XuiFontStyle? style;
        XuiFontDefinition? definition;
        lock (_gate)
        {
            style = _fontStyles.GetValueOrDefault(requested);
            definition = style is null
                ? _fonts.GetValueOrDefault(requested)
                : _fonts.GetValueOrDefault(style.EngineFontId);
        }

        string engineFamily = definition?.Family ?? requested;
        string? mapped = null;
        _ = _fontMappings.TryGetValue(requested, out mapped);
        if (mapped is null)
        {
            _ = _fontMappings.TryGetValue(engineFamily, out mapped);
        }

        if (userMappings is not null)
        {
            if (userMappings.TryGetValue(requested, out string? requestedMapping))
            {
                mapped = requestedMapping;
            }
            else if (userMappings.TryGetValue(
                         engineFamily,
                         out string? familyMapping))
            {
                mapped = familyMapping;
            }
        }

        List<XuiDiagnostic> diagnostics = [];
        string family = string.IsNullOrWhiteSpace(mapped)
            ? ApproximateFamily(engineFamily)
            : mapped.Trim();
        string? fontFile = null;
        bool mappedAsFile =
            family.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
            family.EndsWith(".otf", StringComparison.OrdinalIgnoreCase);
        if (mappedAsFile)
        {
            try
            {
                fontFile = Path.GetFullPath(family);
                if (!File.Exists(fontFile))
                {
                    diagnostics.Add(new XuiDiagnostic(
                        "XUI-FONT002",
                        XuiDiagnosticSeverity.Warning,
                        $"Mapped font file '{fontFile}' does not exist; '{requested}' uses an approximate fallback."));
                    family = ApproximateFamily(engineFamily);
                    fontFile = null;
                    mapped = null;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-FONT002",
                    XuiDiagnosticSeverity.Warning,
                    $"Mapped font path '{family}' is invalid ({exception.Message}); '{requested}' uses an approximate fallback."));
                family = ApproximateFamily(engineFamily);
                mapped = null;
            }
        }

        bool approximate = string.IsNullOrWhiteSpace(mapped);
        double baseSize = requestedSize > 0
            ? requestedSize
            : (definition?.BaseSize ?? 16) * (style?.Scale ?? 1);
        if (approximate)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-FONT001",
                XuiDiagnosticSeverity.Warning,
                $"Font '{requested}' is approximated with '{family}'. Configure an exact font mapping when legally available."));
        }

        return new ResolvedFont(
            requested,
            fontFile is null ? family : Path.GetFileNameWithoutExtension(fontFile),
            Math.Max(1, baseSize),
            approximate,
            fontFile,
            diagnostics);
    }

    private AssetIndexSnapshot BuildSnapshot(CancellationToken cancellationToken)
    {
        Dictionary<string, XuiResolvedFile> files = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, XuiResolvedFile> ddsFiles = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, XuiTextureRegion> regions = new(StringComparer.Ordinal);
        Dictionary<string, XuiFontDefinition> fonts = new(StringComparer.Ordinal);
        Dictionary<string, XuiFontStyle> fontStyles = new(StringComparer.Ordinal);
        Dictionary<string, XuiVisualTemplate> visuals = new(StringComparer.Ordinal);
        List<XuiDiagnostic> diagnostics = [];
        List<(string Path, string Text)> fontSources = [];

        foreach (XuiAssetRoot root in Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root.FullPath))
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-ASSET006",
                    XuiDiagnosticSeverity.Warning,
                    $"Asset root '{root.FullPath}' does not exist."));
                continue;
            }

            foreach (string file in EnumerateFiles(root.FullPath, cancellationToken))
            {
                string extension = Path.GetExtension(file);
                if (!IsIndexedExtension(extension))
                {
                    continue;
                }

                string relative = NormalizeKey(Path.GetRelativePath(root.FullPath, file));
                XuiResolvedFile resolved = new(file, root, relative);
                files.TryAdd(relative, resolved);
                files.TryAdd(Path.GetFileName(relative), resolved);
                if (extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
                {
                    ddsFiles.TryAdd(Path.GetFileName(relative), resolved);
                    continue;
                }

                string fileName = Path.GetFileName(file);
                if (extension.Equals(".xui", StringComparison.OrdinalIgnoreCase))
                {
                    IndexVisualLibrary(
                        file,
                        root,
                        visuals,
                        diagnostics,
                        cancellationToken);
                }

                if (extension.Equals(".def", StringComparison.OrdinalIgnoreCase) ||
                    (extension.Equals(".scr", StringComparison.OrdinalIgnoreCase) &&
                     file.Contains(
                         $"{Path.DirectorySeparatorChar}texturedefs{Path.DirectorySeparatorChar}",
                         StringComparison.OrdinalIgnoreCase)))
                {
                    string text = ReadText(file, diagnostics);
                    TextureDefinitionParseResult parsed =
                        TextureDefinitionParser.Parse(text, file);
                    diagnostics.AddRange(parsed.Diagnostics);
                    foreach (XuiTextureRegion region in parsed.Regions)
                    {
                        regions.TryAdd(region.Name, region);
                    }
                }

                if (fileName.Equals("basicfonts.scr", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("fontstyles", StringComparison.OrdinalIgnoreCase))
                {
                    fontSources.Add((file, ReadText(file, diagnostics)));
                }
            }
        }

        FontDefinitionParseResult parsedFonts =
            FontDefinitionParser.Parse(fontSources);
        diagnostics.AddRange(parsedFonts.Diagnostics);
        foreach (XuiFontDefinition font in parsedFonts.Fonts)
        {
            fonts.TryAdd(font.EngineId, font);
        }

        foreach (XuiFontStyle style in parsedFonts.Styles)
        {
            fontStyles.TryAdd(style.Id, style);
        }

        return new AssetIndexSnapshot(
            files,
            ddsFiles,
            regions,
            fonts,
            fontStyles,
            visuals,
            diagnostics);
    }

    private static void IndexVisualLibrary(
        string path,
        XuiAssetRoot root,
        Dictionary<string, XuiVisualTemplate> visuals,
        List<XuiDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException exception)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET008",
                XuiDiagnosticSeverity.Warning,
                $"Could not read '{path}': {exception.Message}"));
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET008",
                XuiDiagnosticSeverity.Warning,
                $"Could not read '{path}': {exception.Message}"));
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        XuiSyntaxTree tree;
        try
        {
            tree = new XuiSyntaxParser().Parse(bytes);
        }
        catch (XuiParseException)
        {
            // Ordinary screen files are indexed as opaque resources. A malformed
            // screen only matters to visual resolution if it actually declared a
            // usable XuiVisual, which cannot be established safely after parse failure.
            return;
        }

        foreach (XuiSyntaxNode syntax in tree.Root
                     .DescendantsAndSelf()
                     .Where(static node =>
                         node.Kind == XuiSyntaxKind.Element &&
                         node.Name == "XuiVisual"))
        {
            string id = XuiModelReader.GetId(syntax, tree.Source)?.Trim() ?? string.Empty;
            if (id.Length == 0)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-ASSET009",
                    XuiDiagnosticSeverity.Warning,
                    $"An XuiVisual in '{path}' has no Id and cannot be resolved.",
                    syntax.Span,
                    syntax.Key));
                continue;
            }

            XuiVisualTemplate template = new(
                id,
                syntax,
                tree.Source,
                path,
                root,
                XuiTimelineParser.Parse(syntax, tree.Source));
            if (!visuals.TryAdd(id, template) &&
                string.Equals(
                    visuals[id].SourcePath,
                    path,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-ASSET010",
                    XuiDiagnosticSeverity.Warning,
                    $"Visual library '{path}' declares duplicate template Id '{id}'. The first declaration wins.",
                    syntax.Span,
                    syntax.Key));
            }
        }
    }

    private string? FindTextureFile(XuiTextureRegion region)
    {
        string definitionDirectory = Path.GetDirectoryName(region.DefinitionPath) ?? string.Empty;
        string adjacent = Path.Combine(definitionDirectory, region.TextureFile);
        if (File.Exists(adjacent))
        {
            return adjacent;
        }

        lock (_gate)
        {
            return _ddsFiles.GetValueOrDefault(Path.GetFileName(region.TextureFile))?.Path;
        }
    }

    private static (int Width, int Height, byte[] Pixels) TransformTilePixels(
        int width,
        int height,
        byte[] pixels,
        int rotationMode)
    {
        if (rotationMode == 0)
        {
            return (width, height, pixels);
        }

        bool swapsAxes = rotationMode is 1 or 3;
        int destinationWidth = swapsAxes ? height : width;
        int destinationHeight = swapsAxes ? width : height;
        byte[] transformed = GC.AllocateUninitializedArray<byte>(
            checked(destinationWidth * destinationHeight * 4));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (int destinationX, int destinationY) = rotationMode switch
                {
                    1 => (height - 1 - y, x),
                    2 => (width - 1 - x, height - 1 - y),
                    3 => (y, width - 1 - x),
                    4 => (width - 1 - x, y),
                    5 => (x, height - 1 - y),
                    6 => (width - 1 - x, height - 1 - y),
                    _ => (x, y),
                };
                pixels.AsSpan(((y * width) + x) * 4, 4).CopyTo(
                    transformed.AsSpan(
                        ((destinationY * destinationWidth) + destinationX) * 4,
                        4));
            }
        }

        return (destinationWidth, destinationHeight, transformed);
    }

    private static (int Width, int Height, byte[] Pixels) ComposeTileSample(
        IReadOnlyList<ResolvedTileTexturePart> parts)
    {
        int[] columnWidths = new int[3];
        int[] rowHeights = new int[3];
        foreach (ResolvedTileTexturePart part in parts)
        {
            (int column, int row) = TileCell(part.Role);
            columnWidths[column] = Math.Max(columnWidths[column], part.Width);
            rowHeights[row] = Math.Max(rowHeights[row], part.Height);
        }

        int width = Math.Max(1, columnWidths.Sum());
        int height = Math.Max(1, rowHeights.Sum());
        byte[] pixels = new byte[checked(width * height * 4)];
        foreach (ResolvedTileTexturePart part in parts)
        {
            (int column, int row) = TileCell(part.Role);
            int cellX = columnWidths.Take(column).Sum();
            int cellY = rowHeights.Take(row).Sum();
            int cellWidth = columnWidths[column];
            int cellHeight = rowHeights[row];
            for (int y = 0; y < cellHeight; y++)
            {
                int sourceY = y % part.Height;
                for (int x = 0; x < cellWidth; x++)
                {
                    int sourceX = x % part.Width;
                    part.BgraPixels.AsSpan(
                            ((sourceY * part.Width) + sourceX) * 4,
                            4)
                        .CopyTo(pixels.AsSpan(
                            ((((cellY + y) * width) + cellX + x) * 4),
                            4));
                }
            }
        }

        return (width, height, pixels);
    }

    private static (int Column, int Row) TileCell(XuiTileRole role) =>
        role switch
        {
            XuiTileRole.CornerTopLeft => (0, 0),
            XuiTileRole.Top => (1, 0),
            XuiTileRole.CornerTopRight => (2, 0),
            XuiTileRole.Left => (0, 1),
            XuiTileRole.Middle => (1, 1),
            XuiTileRole.Right => (2, 1),
            XuiTileRole.CornerBottomLeft => (0, 2),
            XuiTileRole.Bottom => (1, 2),
            XuiTileRole.CornerBottomRight => (2, 2),
            _ => (1, 1),
        };

    private static async Task<DecodedImage> DecodeDdsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        BcDecoder decoder = new();
        Memory2D<ColorRgba32> image = await decoder
            .Decode2DAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        long pixelCount = checked((long)image.Width * image.Height);
        if (pixelCount <= 0 || pixelCount > MaximumDecodedPixels)
        {
            throw new InvalidDataException(
                $"DDS '{path}' has an unsafe decoded size of {image.Width}×{image.Height}.");
        }

        byte[] bgra = GC.AllocateUninitializedArray<byte>(
            checked((int)pixelCount * 4));
        Span2D<ColorRgba32> pixels = image.Span;
        int destination = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                ColorRgba32 pixel = pixels[y, x];
                bgra[destination++] = pixel.b;
                bgra[destination++] = pixel.g;
                bgra[destination++] = pixel.r;
                bgra[destination++] = pixel.a;
            }
        }

        return new DecodedImage(image.Width, image.Height, bgra);
    }

    private static byte[] Crop(
        DecodedImage decoded,
        XuiTextureRegion region,
        List<XuiDiagnostic> diagnostics)
    {
        int x = Math.Clamp((int)region.SourceRectangle.X, 0, decoded.Width - 1);
        int y = Math.Clamp((int)region.SourceRectangle.Y, 0, decoded.Height - 1);
        int width = Math.Clamp(
            (int)region.SourceRectangle.Width,
            1,
            decoded.Width - x);
        int height = Math.Clamp(
            (int)region.SourceRectangle.Height,
            1,
            decoded.Height - y);
        if (x != (int)region.SourceRectangle.X ||
            y != (int)region.SourceRectangle.Y ||
            width != (int)region.SourceRectangle.Width ||
            height != (int)region.SourceRectangle.Height)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET007",
                XuiDiagnosticSeverity.Warning,
                $"Texture region '{region.Name}' exceeds the decoded DDS and was clipped."));
        }

        byte[] result = GC.AllocateUninitializedArray<byte>(
            checked(width * height * 4));
        int sourceStride = decoded.Width * 4;
        int destinationStride = width * 4;
        for (int row = 0; row < height; row++)
        {
            decoded.Bgra.AsSpan(
                    ((y + row) * sourceStride) + (x * 4),
                    destinationStride)
                .CopyTo(result.AsSpan(row * destinationStride, destinationStride));
        }

        return result;
    }

    private async Task<ResolvedTexture?> ReadCacheAsync(
        string hash,
        string name,
        XuiTextureRegion definition,
        string sourcePath,
        bool approximation,
        IReadOnlyList<XuiDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(_cacheDirectory, hash + ".bgra");
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] data;
        try
        {
            data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return null;
        }

        if (data.Length < CacheHeaderSize)
        {
            return null;
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(data);
        int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        long expected = CacheHeaderSize + checked((long)width * height * 4);
        if (width <= 0 || height <= 0 || expected != data.Length)
        {
            return null;
        }

        return new ResolvedTexture(
            name,
            width,
            height,
            data[CacheHeaderSize..],
            definition,
            sourcePath,
            hash,
            approximation,
            diagnostics);
    }

    private async Task WriteCacheAsync(
        string hash,
        int width,
        int height,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);
        string finalPath = Path.Combine(_cacheDirectory, hash + ".bgra");
        if (File.Exists(finalPath))
        {
            return;
        }

        byte[] data = GC.AllocateUninitializedArray<byte>(
            checked(CacheHeaderSize + pixels.Length));
        BinaryPrimitives.WriteInt32LittleEndian(data, width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), height);
        pixels.CopyTo(data, CacheHeaderSize);
        string temporaryPath = Path.Combine(
            _cacheDirectory,
            $".{hash}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                data,
                cancellationToken).ConfigureAwait(false);
            try
            {
                File.Move(temporaryPath, finalPath);
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                // Another resolver completed the same content-addressed cache entry.
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string ComputeCacheHash(
        string sourceHash,
        XuiTextureRegion region)
    {
        string identity = string.Create(
            CultureInfo.InvariantCulture,
            $"{sourceHash}|{region.SourceRectangle.X}|{region.SourceRectangle.Y}|{region.SourceRectangle.Width}|{region.SourceRectangle.Height}|BGRA32-v1");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static IEnumerable<string> EnumerateFiles(
        string root,
        CancellationToken cancellationToken)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> subdirectories;
            IEnumerable<string> files;
            try
            {
                subdirectories = Directory.EnumerateDirectories(directory).ToArray();
                files = Directory.EnumerateFiles(directory).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }

            foreach (string subdirectory in subdirectories)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(subdirectory);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if (!attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(subdirectory);
                }
            }
        }
    }

    private static bool IsIndexedExtension(string extension) =>
        extension.Equals(".xui", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".def", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".scr", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".mat", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".otf", StringComparison.OrdinalIgnoreCase);

    private static string ReadText(
        string path,
        List<XuiDiagnostic> diagnostics)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET008",
                XuiDiagnosticSeverity.Warning,
                $"Could not read '{path}': {exception.Message}"));
            return string.Empty;
        }
    }

    private static string NormalizeKey(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

    private static string ApproximateFamily(string engineFamily)
    {
        if (engineFamily.Contains("boxed", StringComparison.OrdinalIgnoreCase))
        {
            return "Segoe UI";
        }

        return string.IsNullOrWhiteSpace(engineFamily)
            ? "Segoe UI"
            : engineFamily;
    }

    private static byte[] CreatePlaceholderPixels(int width, int height)
    {
        int safeWidth = Math.Clamp(width, 1, 512);
        int safeHeight = Math.Clamp(height, 1, 512);
        byte[] pixels = new byte[checked(safeWidth * safeHeight * 4)];
        for (int y = 0; y < safeHeight; y++)
        {
            for (int x = 0; x < safeWidth; x++)
            {
                bool dark = ((x / 8) + (y / 8)) % 2 == 0;
                int offset = ((y * safeWidth) + x) * 4;
                pixels[offset] = dark ? (byte)48 : (byte)70;
                pixels[offset + 1] = dark ? (byte)35 : (byte)22;
                pixels[offset + 2] = dark ? (byte)38 : (byte)255;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    private sealed record DecodedImage(int Width, int Height, byte[] Bgra);

    private sealed record AssetIndexSnapshot(
        Dictionary<string, XuiResolvedFile> Files,
        Dictionary<string, XuiResolvedFile> DdsFiles,
        Dictionary<string, XuiTextureRegion> TextureRegions,
        Dictionary<string, XuiFontDefinition> Fonts,
        Dictionary<string, XuiFontStyle> FontStyles,
        Dictionary<string, XuiVisualTemplate> Visuals,
        IReadOnlyList<XuiDiagnostic> Diagnostics);
}
