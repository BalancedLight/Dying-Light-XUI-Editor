using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Wpf.Services;

public sealed record UiLanguageDefinition(
    string Code,
    string CultureName,
    string NativeName);

public sealed record LocalizedEnumOption<T>(
    T Value,
    string Label)
    where T : struct, Enum
{
    public override string ToString() => Label;
}

public static class UiLocalization
{
    public const string AutomaticLanguage = "Auto";

    private const string ResourcePrefix =
        "XuiEditor.Wpf.Localization.Strings.";
    private static readonly UiLanguageDefinition[] Definitions =
    [
        new("En", "en-US", "English"),
        new("De", "de-DE", "Deutsch"),
        new("Fr", "fr-FR", "Français"),
        new("It", "it-IT", "Italiano"),
        new("Es", "es-ES", "Español"),
        new("Ru", "ru-RU", "Русский"),
        new("Jp", "ja-JP", "日本語"),
        new("Pl", "pl-PL", "Polski"),
        new("Nl", "nl-NL", "Nederlands"),
        new("Br", "pt-BR", "Português (Brasil)"),
        new("Ko", "ko-KR", "한국어"),
        new("Cn", "zh-CN", "简体中文"),
        new("Tw", "zh-TW", "繁體中文"),
        new("El", "el-GR", "Ελληνικά"),
        new("Tr", "tr-TR", "Türkçe"),
        new("Th", "th-TH", "ไทย"),
        new("Cs", "cs-CZ", "Čeština"),
    ];
    private static readonly Dictionary<string, UiLanguageDefinition>
        DefinitionsByCode = Definitions.ToDictionary(
            static definition => definition.Code,
            StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<Dictionary<string, string>>
        EnglishStrings = new(() => LoadStrings("En"));
    private static ResourceDictionary? _activeDictionary;

    public static IReadOnlyList<UiLanguageDefinition> Languages => Definitions;

    public static string SelectedLanguage { get; private set; } =
        AutomaticLanguage;

    public static string EffectiveLanguage { get; private set; } = "En";

    public static CultureInfo Culture { get; private set; } =
        CultureInfo.GetCultureInfo("en-US");

    public static XmlLanguage XmlLanguage =>
        XmlLanguage.GetLanguage(Culture.IetfLanguageTag);

    public static event EventHandler? LanguageChanged;

    public static void EnsureApplied(string? selection = null)
    {
        if (_activeDictionary is null)
        {
            Apply(selection ?? AutomaticLanguage);
        }
    }

    public static string NormalizeSelection(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) ||
            code.Equals(
                AutomaticLanguage,
                StringComparison.OrdinalIgnoreCase))
        {
            return AutomaticLanguage;
        }

        return DefinitionsByCode.TryGetValue(
            code.Trim(),
            out UiLanguageDefinition? definition)
            ? definition.Code
            : AutomaticLanguage;
    }

    public static string ResolveAutomatic(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        string name = culture.Name;
        string language = culture.TwoLetterISOLanguageName;
        if (language.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            string script = culture.IetfLanguageTag;
            return script.Contains(
                       "Hant",
                       StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(
                       "-TW",
                       StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(
                       "-HK",
                       StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(
                       "-MO",
                       StringComparison.OrdinalIgnoreCase)
                ? "Tw"
                : "Cn";
        }

        return language.ToLowerInvariant() switch
        {
            "de" => "De",
            "fr" => "Fr",
            "it" => "It",
            "es" => "Es",
            "ru" => "Ru",
            "ja" => "Jp",
            "pl" => "Pl",
            "nl" => "Nl",
            "pt" => "Br",
            "ko" => "Ko",
            "el" => "El",
            "tr" => "Tr",
            "th" => "Th",
            "cs" => "Cs",
            _ => "En",
        };
    }

    public static void Apply(
        string? selection,
        CultureInfo? automaticCulture = null)
    {
        string normalized = NormalizeSelection(selection);
        string effective = normalized == AutomaticLanguage
            ? ResolveAutomatic(
                automaticCulture ?? CultureInfo.CurrentUICulture)
            : normalized;
        UiLanguageDefinition definition = DefinitionsByCode[effective];
        ResourceDictionary dictionary;
        try
        {
            dictionary = LoadResourceDictionary(effective);
        }
        catch (Exception exception) when (
            (exception is IOException or
                InvalidOperationException or
                XamlParseException) &&
            !effective.Equals("En", StringComparison.OrdinalIgnoreCase))
        {
            effective = "En";
            definition = DefinitionsByCode[effective];
            dictionary = LoadResourceDictionary(effective);
        }

        Culture = CultureInfo.GetCultureInfo(definition.CultureName);
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;

        Application? application = Application.Current;
        if (application is not null)
        {
            if (_activeDictionary is not null)
            {
                application.Resources.MergedDictionaries.Remove(
                    _activeDictionary);
            }

            application.Resources.MergedDictionaries.Add(dictionary);
            if (application.Dispatcher.CheckAccess())
            {
                foreach (Window window in application.Windows)
                {
                    window.Language = XmlLanguage;
                }
            }
        }

        SelectedLanguage = normalized;
        EffectiveLanguage = effective;
        _activeDictionary = dictionary;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Text(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Application.Current?.TryFindResource(key) is string value)
        {
            return value;
        }

        return EnglishStrings.Value.TryGetValue(key, out string? fallback)
            ? fallback
             : key;
    }

    public static string EnglishText(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return EnglishStrings.Value.TryGetValue(key, out string? value)
            ? value
            : key;
    }

    public static bool TryText(string key, out string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Application.Current?.TryFindResource(key) is string localized)
        {
            value = localized;
            return true;
        }

        if (EnglishStrings.Value.TryGetValue(key, out string? fallback))
        {
            value = fallback;
            return true;
        }

        value = null;
        return false;
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(Culture, Text(key), arguments);

    public static string Message(
        XuiMessageDescriptor? descriptor,
        string englishFallback)
    {
        ArgumentNullException.ThrowIfNull(englishFallback);
        if (descriptor is null)
        {
            return englishFallback;
        }

        string? template = TryText(descriptor.Key, out string? localized)
            ? localized
            : descriptor.EnglishFallback;
        return descriptor.Format(Culture, template);
    }

    public static string DiagnosticMessage(XuiDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (diagnostic.MessageDescriptor is not null)
        {
            return Message(
                diagnostic.MessageDescriptor,
                diagnostic.Message);
        }

        string key = $"Ui.Diagnostic.{diagnostic.Code}";
        if (EffectiveLanguage != "En" &&
            TryText(key, out string? summary) &&
            !string.IsNullOrWhiteSpace(summary))
        {
            return string.Concat(
                summary,
                Environment.NewLine,
                diagnostic.Message);
        }

        return diagnostic.Message;
    }

    public static string Category(string category) =>
        Text($"Ui.Schema.Category.{CategoryKey(category)}");

    public static string Evidence(XuiEditor.Core.Schema.XuiEvidenceLevel evidence) =>
        Text($"Ui.Schema.Evidence.{evidence}");

    public static string PreviewSupport(
        XuiEditor.Core.Schema.XuiPreviewSupport support) =>
        Text($"Ui.Schema.PreviewSupport.{support}");

    public static string PropertyType(XuiEditor.Core.Schema.XuiPropertyType type) =>
        Text($"Ui.Schema.PropertyType.{type}");

    public static IReadOnlyList<LocalizedEnumOption<T>> EnumOptions<T>(
        IEnumerable<T> values)
        where T : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Select(value =>
                new LocalizedEnumOption<T>(
                    value,
                    Text($"Ui.Enum.{typeof(T).Name}.{value}")))
            .ToArray();
    }

    private static string CategoryKey(string category) =>
        category switch
        {
            "Text / Image" => "TextImage",
            "Raw / Unknown" => "RawUnknown",
            _ => category.Replace(" ", string.Empty, StringComparison.Ordinal),
        };

    private static ResourceDictionary LoadResourceDictionary(string code)
    {
        ResourceDictionary dictionary = new()
        {
            Source = new Uri(
                $"/DyingLightXuiEditor;component/Localization/Strings.{code}.xaml",
                UriKind.Relative),
        };
        return dictionary;
    }

    private static Dictionary<string, string> LoadStrings(
        string code)
    {
        Assembly assembly = typeof(UiLocalization).Assembly;
        string suffix = $"{ResourcePrefix}{code}.json";
        string resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(
                suffix,
                StringComparison.Ordinal)) ??
            throw new InvalidOperationException(
                $"Embedded UI localization catalog '{code}' was not found.");
        using Stream stream =
            assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"Embedded UI localization catalog '{code}' could not be opened.");
        Dictionary<string, string>? catalog =
            JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        if (catalog is null || catalog.Count == 0)
        {
            throw new InvalidOperationException(
                $"Embedded UI localization catalog '{code}' is empty.");
        }

        return catalog;
    }
}
