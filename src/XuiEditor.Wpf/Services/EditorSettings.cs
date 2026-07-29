using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using XuiEditor.Core.Assets;

namespace XuiEditor.Wpf.Services;

public enum XuiGridTier
{
    Minor,
    Major,
    Coarse,
}

public sealed class EditorSettings
{
    public double WindowWidth { get; set; } = 1500;

    public double WindowHeight { get; set; } = 930;

    public double HierarchyWidth { get; set; } = 300;

    public double InspectorWidth { get; set; } = 360;

    public double TimelineHeight { get; set; } = 250;

    public bool ShowGrid { get; set; } = true;

    public bool ShowSafeArea { get; set; } = true;

    public bool ShowUnknownBounds { get; set; } = true;

    public bool ShowAdvancedInspector { get; set; }

    public bool PreservePivotVisualPosition { get; set; }

    public bool SnapEnabled { get; set; } = true;

    public double GridSize { get; set; } = 8;

    public double MajorGridSize { get; set; } = 32;

    public double CoarseGridSize { get; set; } = 128;

    [JsonConverter(typeof(JsonStringEnumConverter<XuiGridTier>))]
    public XuiGridTier SnapGridTier { get; set; } = XuiGridTier.Minor;

    public string MinorGridColor { get; set; } = "#20363B40";

    public string MajorGridColor { get; set; } = "#40535A62";

    public string CoarseGridColor { get; set; } = "#60707882";

    public bool ShowParentMask { get; set; }

    public bool GrayOutsideSelectedGroup { get; set; }

    public bool ForceShowCurrentGroup { get; set; }

    public bool ShowDesignTimeElements { get; set; } = true;

    public bool ShowNavigationConnections { get; set; }

    public bool ShowAllNavigationConnections { get; set; }

    public string? WorkspaceRoot { get; set; }

    public string? DyingLightInstallPath { get; set; }

    public string Locale { get; set; } = "En";

    [JsonConverter(typeof(JsonStringEnumConverter<XuiInputGlyphScheme>))]
    public XuiInputGlyphScheme InputGlyphScheme { get; set; } =
        XuiInputGlyphScheme.KeyboardAndMouse;

    public string PreviewScenarioId { get; set; } = "authored";

    public double ReferenceOverlayOpacity { get; set; } = 0.5;

    public List<AssetRootSetting> AssetRoots { get; set; } = [];

    public List<AdditionalAssetSourceSetting> AdditionalAssetSources { get; set; } =
        [];

    public List<string> RecentFiles { get; set; } = [];

    public Dictionary<string, string> FontMappings { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AssetRootSetting
{
    public string Path { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<XuiAssetRootKind>))]
    public XuiAssetRootKind Kind { get; set; }

    public bool IsReadOnly { get; set; }

    [JsonIgnore]
    public bool EffectiveIsReadOnly =>
        IsReadOnly ||
        Kind is XuiAssetRootKind.ExtractedDyingLight or
            XuiAssetRootKind.DyingLightInstall or
            XuiAssetRootKind.AdditionalTextureDefinitions or
            XuiAssetRootKind.Rp6ResourcePack;

    public XuiAssetRoot ToAssetRoot() =>
        new(Path, Kind, EffectiveIsReadOnly);
}

public sealed class AdditionalAssetSourceSetting
{
    public string Path { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<XuiConfiguredAssetSourceKind>))]
    public XuiConfiguredAssetSourceKind Kind { get; set; }

    [JsonIgnore]
    public string DisplayKind => Kind switch
    {
        XuiConfiguredAssetSourceKind.TextureDefinitionFile =>
            "Texture definitions",
        XuiConfiguredAssetSourceKind.Rp6ResourcePack =>
            "RPACK",
        _ => Kind.ToString(),
    };

    public ConfiguredAssetSource ToAssetSource() =>
        new(Path, Kind);
}

public static class EditorSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string ApplicationDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DyingLightXuiEditor");

    public static string SettingsPath =>
        Path.Combine(ApplicationDirectory, "settings.json");

    public static EditorSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return CreateDefaults();
            }

            string json = File.ReadAllText(SettingsPath);
            return Deserialize(json);
        }
        catch (JsonException)
        {
            return CreateDefaults();
        }
        catch (IOException)
        {
            return CreateDefaults();
        }
    }

    public static async Task SaveAsync(
        EditorSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(ApplicationDirectory);
        string json = Serialize(settings);
        string temporary = SettingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                json,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    internal static string Serialize(EditorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        return JsonSerializer.Serialize(settings, JsonOptions);
    }

    internal static EditorSettings Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        EditorSettings settings =
            JsonSerializer.Deserialize<EditorSettings>(json, JsonOptions) ??
            CreateDefaults();
        Normalize(settings);
        return settings;
    }

    private static EditorSettings CreateDefaults() =>
        new()
        {
            DyingLightInstallPath = FindDyingLightInstall(),
        };

    private static void Normalize(EditorSettings settings)
    {
        settings.AssetRoots ??= [];
        settings.AdditionalAssetSources ??= [];
        settings.RecentFiles ??= [];
        settings.Locale = DyingLightInstallProfile.NormalizeLocale(
            settings.Locale);
        settings.PreviewScenarioId =
            string.IsNullOrWhiteSpace(settings.PreviewScenarioId)
                ? "authored"
                : settings.PreviewScenarioId.Trim();
        settings.ReferenceOverlayOpacity = Math.Clamp(
            settings.ReferenceOverlayOpacity,
            0,
            1);
        settings.GridSize = NormalizeGridSize(settings.GridSize, 8);
        settings.MajorGridSize = NormalizeGridSize(
            settings.MajorGridSize,
            Math.Max(32, settings.GridSize * 4));
        settings.CoarseGridSize = NormalizeGridSize(
            settings.CoarseGridSize,
            Math.Max(128, settings.MajorGridSize * 4));
        settings.FontMappings ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetRootSetting root in settings.AssetRoots)
        {
            if (root.Kind is XuiAssetRootKind.ExtractedDyingLight or
                XuiAssetRootKind.DyingLightInstall or
                XuiAssetRootKind.AdditionalTextureDefinitions or
                XuiAssetRootKind.Rp6ResourcePack)
            {
                root.IsReadOnly = true;
            }
        }
    }

    private static double NormalizeGridSize(double value, double fallback) =>
        double.IsFinite(value) && value > 0
            ? Math.Clamp(value, 0.25, 4096)
            : fallback;

    private static string? FindDyingLightInstall()
    {
        List<string> candidates = [];
        string? explicitPath =
            Environment.GetEnvironmentVariable("DYING_LIGHT_INSTALL");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        string programFilesX86 =
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(Path.Combine(
                programFilesX86,
                "Steam",
                "steamapps",
                "common",
                "Dying Light"));
        }

        foreach (DriveInfo drive in DriveInfo.GetDrives()
                     .Where(static drive =>
                         drive.IsReady &&
                         drive.DriveType is DriveType.Fixed or DriveType.Removable))
        {
            candidates.Add(Path.Combine(
                drive.RootDirectory.FullName,
                "SteamLibrary",
                "steamapps",
                "common",
                "Dying Light"));
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(DyingLightInstallIndex.LooksLikeInstall);
    }
}
