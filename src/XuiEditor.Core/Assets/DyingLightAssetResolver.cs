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
    private const long DefaultMaximumCacheBytes = 2L * 1024 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly object _cacheGate = new();
    private readonly string _cacheDirectory;
    private readonly long _maximumCacheBytes;
    private readonly Dictionary<string, string> _fontMappings;
    private readonly IReadOnlyList<IXuiAssetSource> _sources;
    private readonly string _locale;
    private readonly XuiInputGlyphScheme _inputGlyphScheme;
    private readonly ConcurrentDictionary<string, Lazy<Task<DecodedImage>>> _decodedImages =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _assetContents =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<ResolvedBitmapFont?>>>
        _resolvedBitmapFonts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, XuiResolvedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<XuiResolvedFile> _fileList = [];
    private Dictionary<string, XuiResolvedFile> _ddsFiles = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, IReadOnlyList<XuiResolvedFile>> _ddsCandidates =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, XuiTextureRegion> _textureRegions = new(StringComparer.Ordinal);
    private Dictionary<string, XuiFontDefinition> _fonts = new(StringComparer.Ordinal);
    private Dictionary<string, XuiFontStyle> _fontStyles = new(StringComparer.Ordinal);
    private Dictionary<string, XuiBitmapFontMetrics> _bitmapFontMetrics =
        new(StringComparer.OrdinalIgnoreCase);
    private double _fontGlobalScale = 1;
    private Dictionary<string, XuiVisualTemplate> _visuals = new(StringComparer.Ordinal);
    private ILocalizationCatalog? _localization;
    private InputGlyphCatalog _inputGlyphs = new();
    private IReadOnlyList<XuiDiagnostic> _diagnostics = [];
    private long _revision;

    public DyingLightAssetResolver(
        IEnumerable<XuiAssetRoot> roots,
        string? cacheDirectory = null,
        IReadOnlyDictionary<string, string>? fontMappings = null,
        IEnumerable<IXuiAssetSource>? sources = null,
        string? locale = null,
        XuiInputGlyphScheme inputGlyphScheme =
            XuiInputGlyphScheme.KeyboardAndMouse,
        long maximumCacheBytes = DefaultMaximumCacheBytes)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCacheBytes);
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
        _maximumCacheBytes = maximumCacheBytes;
        _fontMappings = fontMappings is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(
                fontMappings,
                StringComparer.OrdinalIgnoreCase);
        _sources = (sources ?? [])
            .DistinctBy(static source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _locale = DyingLightInstallProfile.NormalizeLocale(
            locale ??
            _sources
                .OfType<IDyingLightInstallIndex>()
                .Select(static source => source.Profile.NormalizedLocale)
                .FirstOrDefault());
        _inputGlyphScheme = inputGlyphScheme;
    }

    public IReadOnlyList<XuiAssetRoot> Roots { get; }

    public long Revision => Interlocked.Read(ref _revision);

    public XuiInputGlyphScheme InputGlyphScheme => _inputGlyphScheme;

    public IReadOnlyList<IXuiAssetSource> Sources => _sources;

    public IReadOnlyList<XuiResolvedFile> Files
    {
        get
        {
            lock (_gate)
            {
                return _fileList;
            }
        }
    }

    public ILocalizationCatalog? Localization
    {
        get
        {
            lock (_gate)
            {
                return _localization;
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

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        foreach (IXuiAssetSource source in _sources)
        {
            await source.RebuildAsync(cancellationToken).ConfigureAwait(false);
        }

        AssetIndexSnapshot snapshot = await Task.Run(
            () => BuildSnapshot(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _files = snapshot.Files;
            _fileList = snapshot.FileList;
            _ddsFiles = snapshot.DdsFiles;
            _ddsCandidates = snapshot.DdsCandidates;
            _textureRegions = snapshot.TextureRegions;
            _fonts = snapshot.Fonts;
            _fontStyles = snapshot.FontStyles;
            _bitmapFontMetrics = snapshot.BitmapFontMetrics;
            _fontGlobalScale = snapshot.FontGlobalScale;
            _visuals = snapshot.Visuals;
            _localization = snapshot.Localization;
            _inputGlyphs = snapshot.InputGlyphs;
            _diagnostics = snapshot.Diagnostics;
        }

        _decodedImages.Clear();
        _assetContents.Clear();
        _resolvedBitmapFonts.Clear();
        Interlocked.Increment(ref _revision);
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

        TextureFileResolution textureFile = FindTextureFile(region);
        XuiResolvedFile? ddsFile = textureFile.File;
        if (ddsFile is null)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET005",
                XuiDiagnosticSeverity.Warning,
                $"DDS '{region.TextureFile}' for image '{imagePath}' from definition '{region.DefinitionPath}' was not found. Searched {DescribeTextureRoots()}."));
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

        if (textureFile.AmbiguousCandidateCount > 1)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET013",
                XuiDiagnosticSeverity.Info,
                $"DDS basename '{Path.GetFileName(region.TextureFile)}' matched {textureFile.AmbiguousCandidateCount} files in '{ddsFile.Root.FullPath}'. '{ddsFile.DisplayPath}' was selected deterministically."));
        }

        string sourceKey = AssetContentKey(ddsFile);
        byte[] ddsBytes = await ReadAssetBytesAsync(
            ddsFile,
            cancellationToken).ConfigureAwait(false);
        string sourceHash = Convert.ToHexString(SHA256.HashData(ddsBytes));
        string cacheHash = ComputeCacheHash(sourceHash, region);
        ResolvedTexture? cached = await ReadCacheAsync(
            cacheHash,
            imagePath,
            requested,
            ddsFile.DisplayPath,
            false,
            diagnostics,
            cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        Lazy<Task<DecodedImage>> lazy = _decodedImages.GetOrAdd(
            sourceKey,
            _ => new Lazy<Task<DecodedImage>>(
                () => DecodeDdsAsync(
                    ddsBytes,
                    ddsFile.DisplayPath,
                    CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        DecodedImage decoded;
        try
        {
            decoded = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            ArgumentException or
            IOException or
            NotSupportedException or
            FormatException)
        {
            _decodedImages.TryRemove(sourceKey, out _);
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET012",
                XuiDiagnosticSeverity.Warning,
                $"DDS '{ddsFile.DisplayPath}' could not be decoded for image '{imagePath}': {exception.Message}"));
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
                ddsFile.DisplayPath,
                sourceHash,
                true,
                diagnostics);
        }
        catch
        {
            _decodedImages.TryRemove(sourceKey, out _);
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
            ddsFile.DisplayPath,
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
        FontResolution resolution;
        bool hasBitmapFont;
        lock (_gate)
        {
            resolution = ResolveFontResolution(requested);
            hasBitmapFont = HasBitmapFont(resolution);
        }

        XuiFontDefinition? definition = resolution.Definition;
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

        bool approximate = string.IsNullOrWhiteSpace(mapped) && !hasBitmapFont;
        double baseSize = requestedSize > 0
            ? requestedSize
            : (definition?.BaseSize ?? 16) * resolution.Scale;
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

    public XuiTextMeasurement MeasureText(
        string fontId,
        string text,
        double requestedSize,
        double maximumWidth,
        bool multiline,
        bool uppercase,
        double characterSpacingAdjust = 0)
    {
        string requested = fontId?.Trim() ?? string.Empty;
        string content = uppercase
            ? (text ?? string.Empty).ToUpperInvariant()
            : text ?? string.Empty;
        FontResolution resolution;
        XuiBitmapFontMetrics? metrics;
        double globalScale;
        lock (_gate)
        {
            resolution = ResolveFontResolution(requested);
            metrics = FindBitmapMetrics(resolution.Definition);
            globalScale = _fontGlobalScale;
        }

        if (resolution.Definition is not null && metrics is not null)
        {
            double scale = requestedSize > 0
                ? requestedSize / Math.Max(1, metrics.FontHeight)
                : resolution.Scale *
                  globalScale *
                  resolution.Definition.HeightScale;
            return MeasureRunes(
                content,
                metrics,
                Math.Max(0.01, scale),
                resolution.CharacterSpacing + characterSpacingAdjust,
                resolution.SpecialSignsScale,
                maximumWidth,
                multiline);
        }

        ResolvedFont fallback = ResolveFont(requested, requestedSize);
        double size = Math.Max(1, fallback.Size);
        double estimatedAdvance = Math.Max(
            0,
            (size * 0.55) + characterSpacingAdjust);
        return MeasureEstimated(
            content,
            estimatedAdvance,
            size,
            maximumWidth,
            multiline);
    }

    public string ResolveText(string keyOrLiteral)
    {
        if (string.IsNullOrEmpty(keyOrLiteral))
        {
            return keyOrLiteral;
        }

        lock (_gate)
        {
            if (_localization?.TryResolve(
                    keyOrLiteral,
                    out string direct) == true)
            {
                return direct;
            }

            return ResolveTextMarkup(keyOrLiteral);
        }
    }

    private string ResolveTextMarkup(string text)
    {
        int first = text.IndexOf('&');
        if (first < 0)
        {
            return text;
        }

        StringBuilder result = new(text.Length);
        int cursor = 0;
        while (first >= 0)
        {
            int close = text.IndexOf('&', first + 1);
            if (close < 0)
            {
                break;
            }

            _ = result.Append(text, cursor, first - cursor);
            string token = text[(first + 1)..close];
            if (_localization?.TryResolve(token, out string localized) == true)
            {
                _ = result.Append(localized);
            }
            else if (_inputGlyphs.TryResolve(token, out string glyph))
            {
                _ = result.Append(glyph);
            }
            else
            {
                _ = result.Append('&').Append(token).Append('&');
            }

            cursor = close + 1;
            first = text.IndexOf('&', cursor);
        }

        _ = result.Append(text, cursor, text.Length - cursor);
        return result.ToString();
    }

    private static XuiTextMeasurement MeasureRunes(
        string content,
        XuiBitmapFontMetrics metrics,
        double scale,
        double characterSpacing,
        double specialSignsScale,
        double maximumWidth,
        bool multiline)
    {
        double widthLimit = maximumWidth > 0
            ? maximumWidth
            : double.PositiveInfinity;
        double currentWidth = 0;
        double measuredWidth = 0;
        int lines = 1;
        foreach (Rune rune in content.EnumerateRunes())
        {
            if (rune.Value == '\r')
            {
                continue;
            }

            if (rune.Value == '\n')
            {
                if (!multiline)
                {
                    break;
                }

                measuredWidth = Math.Max(measuredWidth, currentWidth);
                currentWidth = 0;
                lines++;
                continue;
            }

            XuiBitmapGlyph? glyph =
                metrics.Glyphs.GetValueOrDefault(rune.Value) ??
                metrics.Glyphs.GetValueOrDefault('?');
            if (glyph is null)
            {
                continue;
            }

            double glyphScale = scale *
                                (glyph.IsSpecial
                                    ? specialSignsScale
                                    : 1);
            double advance = Math.Max(
                0,
                (glyph.Advance + characterSpacing) * glyphScale);
            if (multiline &&
                currentWidth > 0 &&
                currentWidth + advance > widthLimit)
            {
                measuredWidth = Math.Max(measuredWidth, currentWidth);
                currentWidth = 0;
                lines++;
            }

            currentWidth += advance;
        }

        measuredWidth = Math.Max(measuredWidth, currentWidth);
        return new XuiTextMeasurement(
            measuredWidth,
            metrics.FontHeight * scale * lines,
            lines,
            IsExact: true);
    }

    private static XuiTextMeasurement MeasureEstimated(
        string content,
        double advance,
        double lineHeight,
        double maximumWidth,
        bool multiline)
    {
        double widthLimit = maximumWidth > 0
            ? maximumWidth
            : double.PositiveInfinity;
        double currentWidth = 0;
        double measuredWidth = 0;
        int lines = 1;
        foreach (Rune rune in content.EnumerateRunes())
        {
            if (rune.Value == '\r')
            {
                continue;
            }

            if (rune.Value == '\n')
            {
                if (!multiline)
                {
                    break;
                }

                measuredWidth = Math.Max(measuredWidth, currentWidth);
                currentWidth = 0;
                lines++;
                continue;
            }

            if (multiline &&
                currentWidth > 0 &&
                currentWidth + advance > widthLimit)
            {
                measuredWidth = Math.Max(measuredWidth, currentWidth);
                currentWidth = 0;
                lines++;
            }

            currentWidth += advance;
        }

        measuredWidth = Math.Max(measuredWidth, currentWidth);
        return new XuiTextMeasurement(
            measuredWidth,
            lineHeight * lines,
            lines,
            IsExact: false);
    }

    public ValueTask<ResolvedBitmapFont?> ResolveBitmapFontAsync(
        string fontId,
        CancellationToken cancellationToken = default)
    {
        string requested = fontId?.Trim() ?? string.Empty;
        if (requested.Length == 0)
        {
            return ValueTask.FromResult<ResolvedBitmapFont?>(null);
        }

        Lazy<Task<ResolvedBitmapFont?>> lazy = _resolvedBitmapFonts.GetOrAdd(
            requested,
            key => new Lazy<Task<ResolvedBitmapFont?>>(
                () => ResolveBitmapFontCoreAsync(key, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return new ValueTask<ResolvedBitmapFont?>(
            lazy.Value.WaitAsync(cancellationToken));
    }

    private async Task<ResolvedBitmapFont?> ResolveBitmapFontCoreAsync(
        string requested,
        CancellationToken cancellationToken)
    {
        FontResolution resolution;
        XuiBitmapFontMetrics? metrics;
        XuiResolvedFile? atlasFile;
        double globalScale;
        lock (_gate)
        {
            resolution = ResolveFontResolution(requested);
            metrics = FindBitmapMetrics(resolution.Definition);
            atlasFile = FindFontAtlas(resolution.Definition);
            globalScale = _fontGlobalScale;
        }

        if (resolution.Definition is null ||
            metrics is null ||
            atlasFile is null)
        {
            return null;
        }

        byte[] bytes = await ReadAssetBytesAsync(
            atlasFile,
            cancellationToken).ConfigureAwait(false);
        string contentHash = Convert.ToHexString(SHA256.HashData(bytes));
        DecodedImage decoded;
        try
        {
            decoded = await DecodeDdsAsync(
                bytes,
                atlasFile.DisplayPath,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            IOException or
            NotSupportedException or
            FormatException)
        {
            return new ResolvedBitmapFont(
                requested,
                resolution.Definition.EngineId,
                1,
                metrics.FontHeight,
                resolution.CharacterSpacing,
                resolution.SpecialSignsScale,
                metrics,
                1,
                1,
                [0, 0, 0, 0],
                atlasFile.DisplayPath,
                contentHash,
                [
                    new XuiDiagnostic(
                        "XUI-FONT005",
                        XuiDiagnosticSeverity.Warning,
                        $"Bitmap font atlas '{atlasFile.DisplayPath}' could not be decoded: {exception.Message}"),
                ]);
        }

        double size = Math.Max(
            1,
            metrics.FontHeight *
            resolution.Scale *
            globalScale *
            resolution.Definition.HeightScale);
        return new ResolvedBitmapFont(
            requested,
            resolution.Definition.EngineId,
            size,
            metrics.FontHeight,
            resolution.CharacterSpacing,
            resolution.SpecialSignsScale,
            metrics,
            decoded.Width,
            decoded.Height,
            decoded.Bgra,
            atlasFile.DisplayPath,
            contentHash,
            []);
    }

    private FontResolution ResolveFontResolution(string requested)
    {
        XuiFontDefinition? direct = _fonts.GetValueOrDefault(requested);
        if (direct is not null)
        {
            return new FontResolution(direct, 1, 0, 1);
        }

        string current = requested;
        double scale = 1;
        double characterSpacing = 0;
        double specialSignsScale = 1;
        HashSet<string> visited = new(StringComparer.Ordinal);
        for (int depth = 0; depth < 11 && visited.Add(current); depth++)
        {
            XuiFontStyle? style = _fontStyles.GetValueOrDefault(current);
            if (style is null)
            {
                return new FontResolution(
                    _fonts.GetValueOrDefault(current),
                    scale,
                    characterSpacing,
                    specialSignsScale);
            }

            scale *= style.Scale;
            characterSpacing += style.CharacterSpacing;
            specialSignsScale *= style.SpecialSignsScale;
            current = style.EngineFontId;
            if (!style.IsAlias)
            {
                return new FontResolution(
                    _fonts.GetValueOrDefault(current),
                    scale,
                    characterSpacing,
                    specialSignsScale);
            }
        }

        return new FontResolution(
            null,
            scale,
            characterSpacing,
            specialSignsScale);
    }

    private bool HasBitmapFont(FontResolution resolution) =>
        FindBitmapMetrics(resolution.Definition) is not null &&
        FindFontAtlas(resolution.Definition) is not null;

    private XuiBitmapFontMetrics? FindBitmapMetrics(
        XuiFontDefinition? definition)
    {
        if (definition is null)
        {
            return null;
        }

        string id = string.Create(
            CultureInfo.InvariantCulture,
            $"{definition.Family}_{definition.BaseSize:0}");
        return _bitmapFontMetrics.GetValueOrDefault(id);
    }

    private XuiResolvedFile? FindFontAtlas(XuiFontDefinition? definition)
    {
        if (string.IsNullOrWhiteSpace(definition?.TextureAlias))
        {
            return null;
        }

        return _ddsFiles.GetValueOrDefault(
            Path.GetFileName(definition.TextureAlias));
    }

    private AssetIndexSnapshot BuildSnapshot(CancellationToken cancellationToken)
    {
        Dictionary<string, XuiResolvedFile> files = new(StringComparer.OrdinalIgnoreCase);
        List<XuiResolvedFile> fileList = [];
        Dictionary<string, XuiResolvedFile> ddsFiles = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<XuiResolvedFile>> ddsCandidates =
            new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, XuiTextureRegion> regions = new(StringComparer.Ordinal);
        Dictionary<string, XuiFontDefinition> fonts = new(StringComparer.Ordinal);
        Dictionary<string, XuiFontStyle> fontStyles = new(StringComparer.Ordinal);
        Dictionary<string, XuiBitmapFontMetrics> bitmapFonts =
            new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, XuiVisualTemplate> visuals = new(StringComparer.Ordinal);
        List<XuiDiagnostic> diagnostics = [];
        List<(string Path, string Text)> fontSources = [];
        List<(string Locale, XuiResolvedFile File)> localizationSources = [];
        List<XuiResolvedFile> inputGlyphSources = [];

        void IndexFile(XuiResolvedFile resolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string extension = Path.GetExtension(resolved.RelativePath);
            if (!IsIndexedExtension(extension))
            {
                return;
            }

            string relative = NormalizeKey(resolved.RelativePath);
            files.TryAdd(relative, resolved);
            files.TryAdd(Path.GetFileName(relative), resolved);
            fileList.Add(resolved);
            if (extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                string ddsName = Path.GetFileName(relative);
                ddsFiles.TryAdd(ddsName, resolved);
                if (!ddsCandidates.TryGetValue(
                        ddsName,
                        out List<XuiResolvedFile>? candidates))
                {
                    candidates = [];
                    ddsCandidates.Add(ddsName, candidates);
                }

                candidates.Add(resolved);
                return;
            }

            string fileName = Path.GetFileName(relative);
            if (extension.Equals(".xui", StringComparison.OrdinalIgnoreCase))
            {
                IndexVisualLibrary(
                    resolved,
                    visuals,
                    diagnostics,
                    cancellationToken);
            }

            if (extension.Equals(".def", StringComparison.OrdinalIgnoreCase) ||
                (extension.Equals(".scr", StringComparison.OrdinalIgnoreCase) &&
                 relative.Contains(
                     $"{Path.DirectorySeparatorChar}texturedefs{Path.DirectorySeparatorChar}",
                     StringComparison.OrdinalIgnoreCase)))
            {
                string text = ReadText(resolved, diagnostics, cancellationToken);
                TextureDefinitionParseResult parsed =
                    TextureDefinitionParser.Parse(text, resolved.DisplayPath);
                diagnostics.AddRange(parsed.Diagnostics);
                foreach (XuiTextureRegion region in parsed.Regions)
                {
                    regions.TryAdd(
                        region.Name,
                        region with
                        {
                            DefinitionRoot = resolved.Root,
                            DefinitionRelativePath = resolved.RelativePath,
                        });
                }
            }

            if (fileName.Equals("basicfonts.scr", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("fontstyles", StringComparison.OrdinalIgnoreCase))
            {
                fontSources.Add((
                    resolved.DisplayPath,
                    ReadText(resolved, diagnostics, cancellationToken)));
            }

            if (extension.Equals(".fm", StringComparison.OrdinalIgnoreCase))
            {
                BitmapFontParseResult parsed = BitmapFontParser.Parse(
                    ReadText(resolved, diagnostics, cancellationToken),
                    resolved.DisplayPath);
                diagnostics.AddRange(parsed.Diagnostics);
                if (parsed.Metrics is not null)
                {
                    bitmapFonts.TryAdd(parsed.Metrics.Id, parsed.Metrics);
                }
            }

            if (fileName.Equals(
                    "common_texts_all.bin",
                    StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".scr", StringComparison.OrdinalIgnoreCase) &&
                IsLocalizationSource(relative))
            {
                localizationSources.Add((
                    InferLocale(resolved) ?? _locale,
                    resolved));
            }

            if (IsSelectedInputGlyphCatalog(fileName))
            {
                inputGlyphSources.Add(resolved);
            }
        }

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
                IndexFile(resolved);
            }
        }

        foreach (IXuiAssetSource source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.AddRange(source.Diagnostics);
            string rootPath = source is IDyingLightInstallIndex install
                ? install.Profile.FullPath
                : Path.GetDirectoryName(
                      source.Entries.Count == 0
                          ? null
                          : source.Entries[0].Origin.ContainerPath) ??
                  Environment.CurrentDirectory;
            XuiAssetRoot root = new(
                rootPath,
                XuiAssetRootKind.DyingLightInstall,
                true);
            foreach (XuiAssetEntry entry in source.Entries.OrderBy(
                         static entry => entry.VirtualPath,
                         StringComparer.OrdinalIgnoreCase))
            {
                XuiResolvedFile resolved = new(
                    entry.Origin.DisplayPath,
                    root,
                    entry.VirtualPath,
                    entry);
                IndexFile(resolved);
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

        ILocalizationCatalog? localization = BuildLocalization(
            localizationSources,
            diagnostics,
            cancellationToken);
        InputGlyphCatalog inputGlyphs = BuildInputGlyphs(
            inputGlyphSources,
            diagnostics,
            cancellationToken);
        return new AssetIndexSnapshot(
            files,
            fileList,
            ddsFiles,
            ddsCandidates.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<XuiResolvedFile>)pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase),
            regions,
            fonts,
            fontStyles,
            bitmapFonts,
            parsedFonts.GlobalScale,
            visuals,
            localization,
            inputGlyphs,
            diagnostics);
    }

    private static void IndexVisualLibrary(
        XuiResolvedFile file,
        Dictionary<string, XuiVisualTemplate> visuals,
        List<XuiDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = file.ReadAllBytesAsync(cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (IOException exception)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET008",
                XuiDiagnosticSeverity.Warning,
                $"Could not read '{file.DisplayPath}': {exception.Message}"));
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET008",
                XuiDiagnosticSeverity.Warning,
                $"Could not read '{file.DisplayPath}': {exception.Message}"));
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
                    $"An XuiVisual in '{file.DisplayPath}' has no Id and cannot be resolved.",
                    syntax.Span,
                    syntax.Key));
                continue;
            }

            XuiVisualTemplate template = new(
                id,
                syntax,
                tree.Source,
                file.DisplayPath,
                file.Root,
                XuiTimelineParser.Parse(syntax, tree.Source));
            if (!visuals.TryAdd(id, template) &&
                string.Equals(
                    visuals[id].SourcePath,
                    file.DisplayPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-ASSET010",
                    XuiDiagnosticSeverity.Warning,
                    $"Visual library '{file.DisplayPath}' declares duplicate template Id '{id}'. The first declaration wins.",
                    syntax.Span,
                    syntax.Key));
            }
        }
    }

    private TextureFileResolution FindTextureFile(XuiTextureRegion region)
    {
        string fileName = Path.GetFileName(region.TextureFile);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new TextureFileResolution(null, 0);
        }

        IReadOnlyList<XuiResolvedFile> indexedCandidates;
        lock (_gate)
        {
            indexedCandidates = _ddsCandidates.GetValueOrDefault(fileName) ?? [];
        }

        string textureRelativePath = NormalizeKey(region.TextureFile);
        string definitionRelativeDirectory = Path.GetDirectoryName(
            NormalizeKey(region.DefinitionRelativePath)) ?? string.Empty;
        string definitionRelativePath = NormalizeKey(
            Path.Combine(definitionRelativeDirectory, textureRelativePath));
        List<(string RootPath, int RootRank, int FirstIndex, XuiResolvedFile[] Files)> groups =
            indexedCandidates
                .Select(static (file, index) => (File: file, Index: index))
                .GroupBy(
                    static item => item.File.Root.FullPath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => (
                    RootPath: group.Key,
                    RootRank: GetRootRank(group.First().File.Root),
                    FirstIndex: group.Min(static item => item.Index),
                    Files: group
                        .Select(static item => item.File)
                        .OrderBy(
                            static file => NormalizeKey(file.RelativePath),
                            StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
                .OrderBy(static group => group.RootRank)
                .ThenBy(static group => group.FirstIndex)
                .ToList();

        foreach ((string rootPath, _, _, XuiResolvedFile[] files) in groups)
        {
            bool ownsDefinition =
                region.DefinitionRoot is not null &&
                string.Equals(
                    rootPath,
                    region.DefinitionRoot.FullPath,
                    StringComparison.OrdinalIgnoreCase);
            if (ownsDefinition)
            {
                XuiResolvedFile[] definitionRelativeMatches = files
                    .Where(file => string.Equals(
                        NormalizeKey(file.RelativePath),
                        definitionRelativePath,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (definitionRelativeMatches.Length != 0)
                {
                    return new TextureFileResolution(
                        definitionRelativeMatches[0],
                        definitionRelativeMatches.Length);
                }
            }

            XuiResolvedFile[] projectRelativeMatches = files
                .Where(file => string.Equals(
                    NormalizeKey(file.RelativePath),
                    textureRelativePath,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (projectRelativeMatches.Length != 0)
            {
                return new TextureFileResolution(
                    projectRelativeMatches[0],
                    projectRelativeMatches.Length);
            }

            if (files.Length != 0)
            {
                return new TextureFileResolution(files[0], files.Length);
            }
        }

        if (region.DefinitionRoot is null)
        {
            string definitionDirectory =
                Path.GetDirectoryName(region.DefinitionPath) ?? string.Empty;
            string adjacent = Path.Combine(definitionDirectory, region.TextureFile);
            if (File.Exists(adjacent))
            {
                XuiAssetRoot root = new(
                    definitionDirectory,
                    XuiAssetRootKind.ExtractedDyingLight,
                    true);
                return new TextureFileResolution(
                    new XuiResolvedFile(
                        adjacent,
                        root,
                        Path.GetFileName(adjacent)),
                    1);
            }
        }

        return new TextureFileResolution(null, 0);

        int GetRootRank(XuiAssetRoot root)
        {
            for (int index = 0; index < Roots.Count; index++)
            {
                if (string.Equals(
                    Roots[index].FullPath,
                    root.FullPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return Roots.Count + 1;
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
        byte[] bytes,
        string displayPath,
        CancellationToken cancellationToken)
    {
        if (TryDecodeUncompressedDds(
                bytes,
                displayPath,
                cancellationToken,
                out DecodedImage? uncompressed))
        {
            return uncompressed!;
        }

        using MemoryStream stream = new(bytes, writable: false);
        BcDecoder decoder = new();
        Memory2D<ColorRgba32> image = await decoder
            .Decode2DAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        long pixelCount = checked((long)image.Width * image.Height);
        if (pixelCount <= 0 || pixelCount > MaximumDecodedPixels)
        {
            throw new InvalidDataException(
                $"DDS '{displayPath}' has an unsafe decoded size of {image.Width}×{image.Height}.");
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

    private static bool TryDecodeUncompressedDds(
        ReadOnlySpan<byte> bytes,
        string displayPath,
        CancellationToken cancellationToken,
        out DecodedImage? decoded)
    {
        decoded = null;
        const uint ddsMagic = 0x20534444;
        const uint ddsPixelFormatRgb = 0x40;
        if (bytes.Length < 128 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes) != ddsMagic ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 124)
        {
            return false;
        }

        uint pixelFormatFlags =
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[80..]);
        uint bitCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[88..]);
        if ((pixelFormatFlags & ddsPixelFormatRgb) == 0 || bitCount != 32)
        {
            return false;
        }

        int height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes[12..]));
        int width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes[16..]));
        long pixelCount = checked((long)width * height);
        if (width <= 0 ||
            height <= 0 ||
            pixelCount > MaximumDecodedPixels)
        {
            throw new InvalidDataException(
                $"DDS '{displayPath}' has an unsafe decoded size of {width}×{height}.");
        }

        int minimumPitch = checked(width * 4);
        int pitch = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes[20..]));
        if (pitch == 0)
        {
            pitch = minimumPitch;
        }

        if (pitch < minimumPitch)
        {
            throw new InvalidDataException(
                $"DDS '{displayPath}' has an invalid row pitch of {pitch} bytes.");
        }

        long requiredLength = checked(128L + ((long)pitch * height));
        if (requiredLength > bytes.Length)
        {
            throw new InvalidDataException(
                $"DDS '{displayPath}' is truncated: {requiredLength:N0} bytes are required for its base image.");
        }

        uint redMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes[92..]);
        uint greenMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes[96..]);
        uint blueMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes[100..]);
        uint alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(bytes[104..]);
        if (redMask == 0 || greenMask == 0 || blueMask == 0)
        {
            throw new InvalidDataException(
                $"DDS '{displayPath}' has incomplete RGB channel masks.");
        }

        byte[] bgra = GC.AllocateUninitializedArray<byte>(
            checked((int)pixelCount * 4));
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int sourceRow = checked(128 + (y * pitch));
            int destinationRow = checked(y * minimumPitch);
            for (int x = 0; x < width; x++)
            {
                uint pixel = BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(sourceRow + (x * 4), 4));
                int destination = destinationRow + (x * 4);
                bgra[destination] = ExtractMaskedChannel(pixel, blueMask, 0);
                bgra[destination + 1] =
                    ExtractMaskedChannel(pixel, greenMask, 0);
                bgra[destination + 2] =
                    ExtractMaskedChannel(pixel, redMask, 0);
                bgra[destination + 3] =
                    ExtractMaskedChannel(pixel, alphaMask, 255);
            }
        }

        decoded = new DecodedImage(width, height, bgra);
        return true;
    }

    private static byte ExtractMaskedChannel(
        uint pixel,
        uint mask,
        byte missingValue)
    {
        if (mask == 0)
        {
            return missingValue;
        }

        int shift = 0;
        uint shiftedMask = mask;
        while ((shiftedMask & 1) == 0)
        {
            shiftedMask >>= 1;
            shift++;
        }

        int bits = 0;
        uint contiguous = shiftedMask;
        while ((contiguous & 1) != 0)
        {
            contiguous >>= 1;
            bits++;
        }

        if (bits == 0 || contiguous != 0)
        {
            throw new InvalidDataException(
                $"DDS uses unsupported non-contiguous channel mask 0x{mask:X8}.");
        }

        uint maximum = bits == 32
            ? uint.MaxValue
            : (1u << bits) - 1;
        uint value = (pixel & mask) >> shift;
        return (byte)(((ulong)value * 255 + (maximum / 2u)) / maximum);
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

        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            // Cache recency is best-effort and never affects rendering.
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

            TrimCache();
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void TrimCache()
    {
        lock (_cacheGate)
        {
            try
            {
                DirectoryInfo directory = new(_cacheDirectory);
                if (!directory.Exists)
                {
                    return;
                }

                FileInfo[] files = directory
                    .EnumerateFiles("*.bgra", SearchOption.TopDirectoryOnly)
                    .ToArray();
                long total = files.Sum(static file => file.Length);
                foreach (FileInfo file in files
                             .OrderBy(static file => file.LastAccessTimeUtc)
                             .ThenBy(static file => file.LastWriteTimeUtc))
                {
                    if (total <= _maximumCacheBytes)
                    {
                        break;
                    }

                    long length = file.Length;
                    try
                    {
                        file.Delete();
                        total -= length;
                    }
                    catch (Exception exception) when (
                        exception is IOException or
                        UnauthorizedAccessException)
                    {
                        // A busy cache entry can remain until the next trim.
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                // Cache maintenance is best-effort.
            }
        }
    }

    private async Task<byte[]> ReadAssetBytesAsync(
        XuiResolvedFile file,
        CancellationToken cancellationToken)
    {
        string key = AssetContentKey(file);
        Lazy<Task<byte[]>> lazy = _assetContents.GetOrAdd(
            key,
            _ => new Lazy<Task<byte[]>>(
                () => file.ReadAllBytesAsync(CancellationToken.None).AsTask(),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _assetContents.TryRemove(key, out _);
            throw;
        }
    }

    private static string AssetContentKey(XuiResolvedFile file) =>
        file.Entry is null
            ? Path.GetFullPath(file.Path)
            : file.Entry.Origin.DisplayPath;

    private static string ComputeCacheHash(
        string sourceHash,
        XuiTextureRegion region)
    {
        string identity = string.Create(
            CultureInfo.InvariantCulture,
            $"{sourceHash}|{region.DefinitionRoot?.FullPath}|{region.DefinitionRelativePath}|{region.Name}|{region.TextureFile}|{region.Primitive}|{region.SourceRectangle.X}|{region.SourceRectangle.Y}|{region.SourceRectangle.Width}|{region.SourceRectangle.Height}|BGRA32-v2");
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
                subdirectories = Directory
                    .EnumerateDirectories(directory)
                    .OrderByDescending(
                        static path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                files = Directory
                    .EnumerateFiles(directory)
                    .OrderBy(
                        static path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();
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

    private string DescribeTextureRoots()
    {
        string[] roots = Roots
            .Select(static root => root.FullPath)
            .Concat(_sources.Select(static source => source.DisplayName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return roots.Length == 0
            ? "no configured asset roots"
            : string.Join(
                " -> ",
                roots.Select(static root => $"'{root}'"));
    }

    private static bool IsIndexedExtension(string extension) =>
        extension.Equals(".xui", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".def", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".scr", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".fm", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".mat", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".otf", StringComparison.OrdinalIgnoreCase);

    private static string ReadText(
        XuiResolvedFile file,
        List<XuiDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = file.ReadAllBytesAsync(cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            using MemoryStream stream = new(bytes, writable: false);
            using StreamReader reader = new(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-ASSET008",
                XuiDiagnosticSeverity.Warning,
                $"Could not read '{file.DisplayPath}': {exception.Message}"));
            return string.Empty;
        }
    }

    private LocalizationCatalog? BuildLocalization(
        IReadOnlyList<(string Locale, XuiResolvedFile File)> sources,
        List<XuiDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        Dictionary<string, List<LocalizationCatalog>> catalogs =
            new(StringComparer.OrdinalIgnoreCase);
        foreach ((string locale, XuiResolvedFile file) in sources)
        {
            try
            {
                LocalizationCatalog parsed;
                if (Path.GetExtension(file.RelativePath).Equals(
                        ".scr",
                        StringComparison.OrdinalIgnoreCase))
                {
                    parsed = LocalizationCatalogParser.ParseSource(
                        ReadText(file, diagnostics, cancellationToken),
                        locale,
                        sourcePath: file.DisplayPath);
                }
                else
                {
                    byte[] bytes = file.ReadAllBytesAsync(cancellationToken)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                    parsed = LocalizationCatalogParser.Parse(
                        bytes,
                        locale,
                        sourcePath: file.DisplayPath);
                }

                if (!catalogs.TryGetValue(
                        locale,
                        out List<LocalizationCatalog>? localeCatalogs))
                {
                    localeCatalogs = [];
                    catalogs.Add(locale, localeCatalogs);
                }

                localeCatalogs.Add(parsed);
                diagnostics.AddRange(parsed.Diagnostics);
            }
            catch (Exception exception) when (
                exception is IOException or
                InvalidDataException or
                UnauthorizedAccessException)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-LOC001",
                    XuiDiagnosticSeverity.Warning,
                    $"Could not read localization catalog '{file.DisplayPath}': {exception.Message}"));
            }
        }

        LocalizationCatalog? english = MergeLocalizationCatalogs(
            "En",
            catalogs.GetValueOrDefault("En"),
            fallback: null);
        LocalizationCatalog? selected = MergeLocalizationCatalogs(
            _locale,
            catalogs.GetValueOrDefault(_locale),
            string.Equals(_locale, "En", StringComparison.OrdinalIgnoreCase)
                ? null
                : english);
        if (selected is null)
        {
            return english;
        }

        return selected;
    }

    private static LocalizationCatalog? MergeLocalizationCatalogs(
        string locale,
        List<LocalizationCatalog>? catalogs,
        ILocalizationCatalog? fallback)
    {
        if (catalogs is null || catalogs.Count == 0)
        {
            return null;
        }

        Dictionary<string, XuiLocalizedString> merged =
            new(StringComparer.Ordinal);
        List<XuiDiagnostic> diagnostics = [];
        int declarationOrder = 0;

        // Asset roots are indexed in precedence order. Merge from lowest to
        // highest precedence so a loose mod/workspace catalog replaces the
        // installed value while still inheriting every stock string it does
        // not declare.
        for (int catalogIndex = catalogs.Count - 1;
             catalogIndex >= 0;
             catalogIndex--)
        {
            LocalizationCatalog catalog = catalogs[catalogIndex];
            diagnostics.AddRange(catalog.Diagnostics);
            foreach (XuiLocalizedString entry in catalog.Entries)
            {
                merged[entry.Key] = entry with
                {
                    DeclarationOrder = declarationOrder++,
                };
            }
        }

        return new LocalizationCatalog(
            locale,
            merged.Values
                .OrderBy(static entry => entry.DeclarationOrder)
                .ToArray(),
            diagnostics,
            fallback);
    }

    private static InputGlyphCatalog BuildInputGlyphs(
        IReadOnlyList<XuiResolvedFile> sources,
        List<XuiDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        List<(string Source, ReadOnlyMemory<byte> Bytes)> contents = [];
        foreach (XuiResolvedFile file in sources
                     .OrderBy(static file =>
                         Path.GetFileName(file.RelativePath).Contains(
                             "common",
                             StringComparison.OrdinalIgnoreCase)
                             ? 0
                             : 1))
        {
            try
            {
                byte[] bytes = file.ReadAllBytesAsync(cancellationToken)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                contents.Add((file.DisplayPath, bytes));
            }
            catch (Exception exception) when (
                exception is IOException or
                InvalidDataException or
                UnauthorizedAccessException)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-GLYPH001",
                    XuiDiagnosticSeverity.Warning,
                    $"Could not read input glyph catalog '{file.DisplayPath}': {exception.Message}"));
            }
        }

        InputGlyphCatalog catalog = InputGlyphCatalog.Parse(contents);
        diagnostics.AddRange(catalog.Diagnostics);
        return catalog;
    }

    private bool IsSelectedInputGlyphCatalog(string fileName)
    {
        if (fileName.Equals(
                "icons_common.bin",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string selected = _inputGlyphScheme switch
        {
            XuiInputGlyphScheme.Xbox => "icons_xbo.bin",
            XuiInputGlyphScheme.DualShock4 => "icons_ds4.bin",
            _ => "icons_keyboardandmouse.bin",
        };
        return fileName.Equals(selected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? InferLocale(XuiResolvedFile file)
    {
        string normalized = NormalizeKey(file.RelativePath);
        string[] parts = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index + 1 < parts.Length; index++)
        {
            if (!parts[index].Equals(
                    "Locale",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string localeFromPath = parts[index + 1];
            if (localeFromPath.Length is >= 2 and <= 8 &&
                localeFromPath.All(static character =>
                    char.IsLetter(character) ||
                    character is '-' or '_'))
            {
                return DyingLightInstallProfile.NormalizeLocale(
                    localeFromPath);
            }
        }

        string containerName = Path.GetFileNameWithoutExtension(
            file.Entry?.Origin.ContainerPath ?? file.Path);
        if (containerName.Length == 6 &&
            containerName.StartsWith("Data", StringComparison.OrdinalIgnoreCase))
        {
            string locale = containerName[4..];
            if (locale.All(char.IsLetter))
            {
                return DyingLightInstallProfile.NormalizeLocale(locale);
            }
        }

        return null;
    }

    private static bool IsLocalizationSource(string relativePath)
    {
        string normalized = NormalizeKey(relativePath);
        string localeSegment =
            $"{Path.DirectorySeparatorChar}locale{Path.DirectorySeparatorChar}";
        return normalized.Contains(
                   localeSegment,
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(
                   $"locale{Path.DirectorySeparatorChar}",
                   StringComparison.OrdinalIgnoreCase);
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

    private sealed record TextureFileResolution(
        XuiResolvedFile? File,
        int AmbiguousCandidateCount);

    private sealed record FontResolution(
        XuiFontDefinition? Definition,
        double Scale,
        double CharacterSpacing,
        double SpecialSignsScale);

    private sealed record AssetIndexSnapshot(
        Dictionary<string, XuiResolvedFile> Files,
        IReadOnlyList<XuiResolvedFile> FileList,
        Dictionary<string, XuiResolvedFile> DdsFiles,
        Dictionary<string, IReadOnlyList<XuiResolvedFile>> DdsCandidates,
        Dictionary<string, XuiTextureRegion> TextureRegions,
        Dictionary<string, XuiFontDefinition> Fonts,
        Dictionary<string, XuiFontStyle> FontStyles,
        Dictionary<string, XuiBitmapFontMetrics> BitmapFontMetrics,
        double FontGlobalScale,
        Dictionary<string, XuiVisualTemplate> Visuals,
        ILocalizationCatalog? Localization,
        InputGlyphCatalog InputGlyphs,
        IReadOnlyList<XuiDiagnostic> Diagnostics);
}
