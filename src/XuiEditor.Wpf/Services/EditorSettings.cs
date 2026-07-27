using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using XuiEditor.Core.Assets;

namespace XuiEditor.Wpf.Services;

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

    public bool SnapEnabled { get; set; } = true;

    public double GridSize { get; set; } = 8;

    public string? WorkspaceRoot { get; set; }

    public List<AssetRootSetting> AssetRoots { get; set; } = [];

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
        IsReadOnly || Kind == XuiAssetRootKind.ExtractedDyingLight;

    public XuiAssetRoot ToAssetRoot() =>
        new(Path, Kind, EffectiveIsReadOnly);
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

    private static EditorSettings CreateDefaults()
    {
        EditorSettings settings = new();
        string extraction =
            @"D:\Backups\Assets\Dying Light Extraction\Dying Light Files";
        string textures =
            @"D:\Backups\Assets\Dying Light Extraction\Textures";
        if (Directory.Exists(extraction))
        {
            settings.AssetRoots.Add(new AssetRootSetting
            {
                Path = extraction,
                Kind = XuiAssetRootKind.ExtractedDyingLight,
                IsReadOnly = true,
            });
        }

        if (Directory.Exists(textures))
        {
            settings.AssetRoots.Add(new AssetRootSetting
            {
                Path = textures,
                Kind = XuiAssetRootKind.ExtractedDyingLight,
                IsReadOnly = true,
            });
        }

        return settings;
    }

    private static void Normalize(EditorSettings settings)
    {
        settings.AssetRoots ??= [];
        settings.RecentFiles ??= [];
        settings.FontMappings ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetRootSetting root in settings.AssetRoots)
        {
            if (root.Kind == XuiAssetRootKind.ExtractedDyingLight)
            {
                root.IsReadOnly = true;
            }
        }
    }
}
