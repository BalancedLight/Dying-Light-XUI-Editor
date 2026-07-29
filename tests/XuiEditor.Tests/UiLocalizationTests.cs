using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Schema;
using XuiEditor.Core.Values;
using XuiEditor.Wpf;
using XuiEditor.Wpf.Models;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Tests;

[TestClass]
[DoNotParallelize]
public sealed class UiLocalizationTests
{
    private const string XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly (string Code, string Culture, string NativeName)[]
        ExpectedLanguages =
        [
            ("En", "en-US", "English"),
            ("De", "de-DE", "Deutsch"),
            ("Fr", "fr-FR", "Français"),
            ("It", "it-IT", "Italiano"),
            ("Es", "es-ES", "Español"),
            ("Ru", "ru-RU", "Русский"),
            ("Jp", "ja-JP", "日本語"),
            ("Pl", "pl-PL", "Polski"),
            ("Nl", "nl-NL", "Nederlands"),
            ("Br", "pt-BR", "Português (Brasil)"),
            ("Ko", "ko-KR", "한국어"),
            ("Cn", "zh-CN", "简体中文"),
            ("Tw", "zh-TW", "繁體中文"),
            ("El", "el-GR", "Ελληνικά"),
            ("Tr", "tr-TR", "Türkçe"),
            ("Th", "th-TH", "ไทย"),
            ("Cs", "cs-CZ", "Čeština"),
        ];

    private static readonly string[] ExpectedInspectorHelp =
    [
        "Bold",
        "ClassOverride",
        "ClipChildren",
        "Color",
        "ColorControlSequenceEnabled",
        "Font",
        "FontYOffset",
        "Height",
        "HoldAspectPivotPosition",
        "HoldAspectRatio",
        "HoldAspectRatioX",
        "HorizontalAlign",
        "Id",
        "ImagePath",
        "Italic",
        "KeepHeight",
        "KeepHeightOnParentSizeChange",
        "KeepHeightOnResolutionChange",
        "KeepPosX",
        "KeepPosXOnParentSizeChange",
        "KeepPosXOnResolutionChange",
        "KeepPosY",
        "KeepPosYOnParentSizeChange",
        "KeepPosYOnResolutionChange",
        "KeepWidth",
        "KeepWidthOnParentSizeChange",
        "KeepWidthOnResolutionChange",
        "Material",
        "MultiLine",
        "NavDown",
        "NavLeft",
        "NavRight",
        "NavTabBackward",
        "NavTabForward",
        "NavUp",
        "Opacity",
        "Outline",
        "OutlineColor",
        "OutlineSize",
        "Pivot",
        "PointSize",
        "Position",
        "Rotation",
        "Scale",
        "ScaleWidthByResolution",
        "Shadow",
        "ShadowColor",
        "ShadowOffset",
        "Show",
        "SpecialSignsScale",
        "Strike",
        "Text",
        "TextColor",
        "TextStyle",
        "Underline",
        "Uppercase",
        "VerticalAlign",
        "VerticalAlignDown",
        "Visual",
        "Width",
    ];

    private static readonly Regex PlaceholderPattern = new(
        @"\{[^{}\r\n]+\}|%COLOR\([^)\r\n]+\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex ProtectedTokenPattern = new(
        @"%COLOR\([^)\r\n]+\)" +
        @"|\.(?:xui|png|jpg|jpeg|bmp|def|scr|rpack|mat|exe|pak|ttf|otf)" +
        @"|DyingLightGame\.exe|DW\\Data0\.pak|menu_antialias\.mat" +
        @"|(?<![A-Za-z0-9])(?:Dying Light|Chrome 6|" +
        @"Microsoft Testing Platform|" +
        @"ClassOverride|XuiVisual|TextStyle|XML|JSON|PNG|ARGB|" +
        @"BGRA32|UTF-8|WPF|DDS|RP6L|HUD)(?![A-Za-z0-9])" +
        @"|(?<![A-Za-z0-9])(?:XUI|RPACK|PAK)" +
        @"(?=s?(?![A-Za-z0-9]))",
        RegexOptions.CultureInvariant);

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    [TestMethod]
    public void CatalogsHaveIdenticalKeysAndPreserveFormattingContracts()
    {
        string localizationDirectory = Path.Combine(
            RepositoryRoot,
            "src",
            "XuiEditor.Wpf",
            "Localization");
        Dictionary<string, Dictionary<string, string>> catalogs =
            ExpectedLanguages.ToDictionary(
                static language => language.Code,
                language => LoadXamlCatalog(Path.Combine(
                    localizationDirectory,
                    $"Strings.{language.Code}.xaml")),
                StringComparer.Ordinal);

        Dictionary<string, string> english = catalogs["En"];
        Assert.IsTrue(
            english.Count >= 781,
            "The complete catalog unexpectedly lost keys.");
        Assert.IsTrue(english.Values.All(
            static value => !string.IsNullOrWhiteSpace(value)));
        foreach ((string code, _, _) in ExpectedLanguages)
        {
            Dictionary<string, string> catalog = catalogs[code];
            CollectionAssert.AreEquivalent(
                english.Keys.ToArray(),
                catalog.Keys.ToArray(),
                $"{code} must contain exactly the English key set.");
            Assert.IsTrue(
                catalog.Values.All(
                    static value => !string.IsNullOrWhiteSpace(value)),
                $"{code} contains an empty UI value.");
            foreach ((string key, string source) in english)
            {
                string translated = catalog[key];
                CollectionAssert.AreEquivalent(
                    Matches(PlaceholderPattern, source),
                    Matches(PlaceholderPattern, translated),
                    $"{code}/{key} changed a format placeholder.");
                CollectionAssert.AreEquivalent(
                    Matches(ProtectedTokenPattern, source),
                    Matches(ProtectedTokenPattern, translated),
                    $"{code}/{key} changed a protected technical token.");
                Assert.AreEqual(
                    Count(source, '\n'),
                    Count(translated, '\n'),
                    $"{code}/{key} changed its newline contract.");
                Assert.AreEqual(
                    Count(source, '|'),
                    Count(translated, '|'),
                    $"{code}/{key} changed its file-filter separators.");
                Assert.AreEqual(
                    Count(source, '_'),
                    Count(translated, '_'),
                    $"{code}/{key} changed its mnemonic marker count.");
            }
        }

        string coreDirectory = Path.Combine(
            RepositoryRoot,
            "src",
            "XuiEditor.Core");
        string[] diagnosticCodes = Directory.EnumerateFiles(
                coreDirectory,
                "*.cs",
                SearchOption.AllDirectories)
            .SelectMany(path => Regex.Matches(
                    File.ReadAllText(path),
                    @"\bXUI-[A-Z]+[0-9]{3}\b",
                    RegexOptions.CultureInvariant)
                .Select(static match => match.Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.IsTrue(diagnosticCodes.Length > 0);
        foreach (string diagnosticCode in diagnosticCodes)
        {
            Assert.IsTrue(
                english.ContainsKey($"Ui.Diagnostic.{diagnosticCode}"),
                $"{diagnosticCode} has no localized editor summary.");
        }

        string allowlistPath = Path.Combine(
            localizationDirectory,
            "SameAsEnglishAllowlist.json");
        Dictionary<string, string[]> allowlist =
            JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                File.ReadAllText(allowlistPath)) ??
            throw new AssertFailedException(
                "The same-as-English allowlist could not be read.");
        CollectionAssert.AreEquivalent(
            ExpectedLanguages.Select(static language => language.Code)
                .ToArray(),
            allowlist.Keys.ToArray());
        foreach ((string code, _, _) in ExpectedLanguages)
        {
            string[] identical = catalogs[code]
                .Where(pair =>
                    pair.Key != "Ui.FontFamily" &&
                    pair.Value.Equals(
                        english[pair.Key],
                        StringComparison.Ordinal))
                .Select(static pair => pair.Key)
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] allowed = allowlist[code]
                .Order(StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(
                allowed,
                identical,
                $"{code} has an unreviewed same-as-English value.");
        }
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void WindowsCultureMappingAndSettingsKeepUiAndPreviewLocalesSeparate()
    {
        CollectionAssert.AreEqual(
            ExpectedLanguages.Select(static language => language.Code)
                .ToArray(),
            UiLocalization.Languages
                .Select(static language => language.Code)
                .ToArray());
        CollectionAssert.AreEqual(
            ExpectedLanguages.Select(static language => language.Culture)
                .ToArray(),
            UiLocalization.Languages
                .Select(static language => language.CultureName)
                .ToArray());
        CollectionAssert.AreEqual(
            ExpectedLanguages.Select(static language => language.NativeName)
                .ToArray(),
            UiLocalization.Languages
                .Select(static language => language.NativeName)
                .ToArray());

        Assert.AreEqual(
            "De",
            UiLocalization.ResolveAutomatic(
                CultureInfo.GetCultureInfo("de-AT")));
        Assert.AreEqual(
            "Br",
            UiLocalization.ResolveAutomatic(
                CultureInfo.GetCultureInfo("pt-PT")));
        Assert.AreEqual(
            "Cn",
            UiLocalization.ResolveAutomatic(
                CultureInfo.GetCultureInfo("zh-Hans")));
        Assert.AreEqual(
            "Cn",
            UiLocalization.ResolveAutomatic(
                CultureInfo.GetCultureInfo("zh-SG")));
        Assert.AreEqual(
            "Tw",
            UiLocalization.ResolveAutomatic(
                CultureInfo.GetCultureInfo("zh-Hant")));
        Assert.AreEqual(
            "Tw",
            UiLocalization.ResolveAutomatic(
                CultureInfo.GetCultureInfo("zh-HK")));
        Assert.AreEqual(
            "Tw",
            UiLocalization.ResolveAutomatic(
                CultureInfo.GetCultureInfo("zh-MO")));
        Assert.AreEqual(
            "En",
            UiLocalization.ResolveAutomatic(
                CultureInfo.GetCultureInfo("ar-SA")));
        Assert.AreEqual(
            UiLocalization.AutomaticLanguage,
            UiLocalization.NormalizeSelection("not-a-language"));

        EditorSettings migrated = EditorSettingsStore.Deserialize(
            """{"Locale":"Ru"}""");
        Assert.AreEqual("Ru", migrated.Locale);
        Assert.AreEqual(
            UiLocalization.AutomaticLanguage,
            migrated.UiLanguage);

        EditorSettings invalid = EditorSettingsStore.Deserialize(
            """{"Locale":"Jp","UiLanguage":"unknown"}""");
        Assert.AreEqual("Jp", invalid.Locale);
        Assert.AreEqual(
            UiLocalization.AutomaticLanguage,
            invalid.UiLanguage);

        EditorSettings selected = new()
        {
            Locale = "Ru",
            UiLanguage = "De",
        };
        EditorSettings roundTrip = EditorSettingsStore.Deserialize(
            EditorSettingsStore.Serialize(selected));
        Assert.AreEqual("Ru", roundTrip.Locale);
        Assert.AreEqual("De", roundTrip.UiLanguage);

        EnsureApplication();
        try
        {
            UiLocalization.Apply(
                UiLocalization.AutomaticLanguage,
                CultureInfo.GetCultureInfo("zh-Hant"));
            Assert.AreEqual(
                UiLocalization.AutomaticLanguage,
                UiLocalization.SelectedLanguage);
            Assert.AreEqual("Tw", UiLocalization.EffectiveLanguage);
            Assert.AreEqual("Ru", selected.Locale);

            UiLocalization.Apply("En");
            string englishGroup = UiLocalization.EnumOptions(
                new[] { XuiElementPreset.Group })[0].Label;
            UiLocalization.Apply("De");
            LocalizedEnumOption<XuiElementPreset> germanGroup =
                UiLocalization.EnumOptions(
                    new[] { XuiElementPreset.Group })[0];
            Assert.AreEqual(XuiElementPreset.Group, germanGroup.Value);
            Assert.AreNotEqual(englishGroup, germanGroup.Label);
        }
        finally
        {
            UiLocalization.Apply("En");
        }
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void InspectorHelpIsLimitedToTheExactEvidenceWhitelist()
    {
        EnsureApplication();
        UiLocalization.Apply("En");
        string helpPath = Path.Combine(
            RepositoryRoot,
            "tools",
            "xui-property-help.json");
        Dictionary<string, string> canonical =
            JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(helpPath)) ??
            throw new AssertFailedException(
                "The canonical inspector-help catalog could not be read.");

        string[] actual = InspectorHelpText.VerifiedProperties
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected = ExpectedInspectorHelp
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.AreEqual(60, actual.Length);
        CollectionAssert.AreEqual(expected, actual);
        CollectionAssert.AreEquivalent(expected, canonical.Keys.ToArray());

        foreach (string propertyName in expected)
        {
            XuiPropertyDefinition definition =
                XuiClassCatalog.Default.FindProperty(propertyName) ??
                throw new AssertFailedException(
                    $"Catalog property {propertyName} is missing.");
            Assert.AreEqual(
                canonical[propertyName],
                definition.Description,
                $"{propertyName} drifted from the generated schema.");
            Assert.IsTrue(
                InspectorHelpText.HasVerifiedPurpose(definition));
            Assert.AreEqual(
                canonical[propertyName],
                InspectorHelpText.Purpose(definition));
        }

        XuiPropertyDefinition unevidenced =
            XuiClassCatalog.Default.Properties.First(definition =>
                !InspectorHelpText.VerifiedProperties.Contains(
                    definition.Name));
        Assert.IsFalse(
            InspectorHelpText.HasVerifiedPurpose(unevidenced));
        Assert.IsNull(InspectorHelpText.Purpose(unevidenced));

        string unknown = InspectorHelpText.BuildToolTip(
            "ModSpecificFlag",
            definition: null,
            isAuthored: true);
        StringAssert.Contains(unknown, "ModSpecificFlag");
        StringAssert.Contains(
            unknown,
            UiLocalization.Text("Ui.Inspector.UnknownProperty"));

        InspectorPropertyRow row = new(
            "Width",
            "100",
            "Layout",
            isMixed: false,
            isUnknown: false,
            isAuthored: true,
            definition: XuiClassCatalog.Default.FindProperty("Width"));
        StringAssert.Contains(row.EditorToolTip, canonical["Width"]);
        row.Error = "Validation wins";
        Assert.AreEqual("Validation wins", row.EditorToolTip);

        string englishCategory = UiLocalization.Category("Appearance");
        string englishEvidence = UiLocalization.Evidence(
            XuiEvidenceLevel.DyingLightStock);
        try
        {
            foreach ((string code, _, _) in ExpectedLanguages)
            {
                UiLocalization.Apply(code);
                Assert.IsFalse(string.IsNullOrWhiteSpace(
                    UiLocalization.Category("Appearance")));
                Assert.IsFalse(string.IsNullOrWhiteSpace(
                    UiLocalization.Evidence(
                        XuiEvidenceLevel.DyingLightStock)));
                foreach (string propertyName in expected)
                {
                    XuiPropertyDefinition definition =
                        XuiClassCatalog.Default.FindProperty(propertyName)!;
                    Assert.IsFalse(
                        string.IsNullOrWhiteSpace(
                            InspectorHelpText.Purpose(definition)),
                        $"{code}/{propertyName} has no localized help.");
                }
            }

            UiLocalization.Apply("De");
            Assert.AreNotEqual(
                englishCategory,
                UiLocalization.Category("Appearance"));
            Assert.AreNotEqual(
                englishEvidence,
                UiLocalization.Evidence(
                    XuiEvidenceLevel.DyingLightStock));
        }
        finally
        {
            UiLocalization.Apply("En");
        }
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void LiveSwitchRefreshesExistingUiWithoutChangingTheDocument()
    {
        EnsureApplication();
        UiLocalization.Apply("En");
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Id>Root</Id><Width>1280</Width>" +
            "<Height>720</Height></Properties><MyText><Properties>" +
            "<Id>Label</Id><Width>100</Width><Height>30</Height>" +
            "<Text>Hello</Text></Properties></MyText></XuiCanvas>");
        XuiSyntaxNode label =
            XuiModelReader.VisualDescendants(document.Root).Single();
        using MainWindow window = new();
        window.AttachDocumentForTesting(document);
        window.SelectNodeKeysForTesting([label.Key]);
        window.SetInspectorValueForTesting("Width", "110");

        string documentText = document.Text;
        bool dirty = document.IsDirty;
        InspectorPropertyRow englishWidth = window.InspectorProperties
            .Single(static row => row.Name == "Width");
        InspectorPropertyRow englishText = window.InspectorProperties
            .Single(static row => row.Name == "Text");
        HierarchyRow englishHierarchy =
            window.HierarchyRowForTesting(label.Key) ??
            throw new AssertFailedException(
                "The selected hierarchy row was not built.");
        string englishCategory = englishText.Category;
        string englishHelp = englishWidth.ToolTip;
        string englishAutomation =
            englishHierarchy.VisibilityAutomationName;
        string englishUndo = window.UndoHeaderForTesting;
        string englishStatus = window.StatusForTesting;
        XuiDiagnostic diagnostic = new(
            "XUI-LAYOUT005",
            XuiDiagnosticSeverity.Warning,
            "Raw English diagnostic details.");
        string englishDiagnostic =
            UiLocalization.DiagnosticMessage(diagnostic);

        try
        {
            UiLocalization.Apply("De");

            Assert.IsTrue(
                window.Language.IetfLanguageTag.Equals(
                    "de-DE",
                    StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(documentText, document.Text);
            Assert.AreEqual(dirty, document.IsDirty);
            Assert.AreEqual("110", XuiModelReader.GetPropertyValue(
                document.SyntaxTree.FindByKey(label.Key)!,
                document.Text,
                "Width"));

            InspectorPropertyRow germanWidth = window.InspectorProperties
                .Single(static row => row.Name == "Width");
            InspectorPropertyRow germanText = window.InspectorProperties
                .Single(static row => row.Name == "Text");
            HierarchyRow germanHierarchy =
                window.HierarchyRowForTesting(label.Key)!;
            Assert.AreEqual("Width", germanWidth.Name);
            Assert.AreEqual("110", germanWidth.Value);
            Assert.AreNotEqual(englishCategory, germanText.Category);
            Assert.AreNotEqual(englishHelp, germanWidth.ToolTip);
            StringAssert.Contains(germanWidth.ToolTip, "Width");
            Assert.AreNotEqual(
                englishAutomation,
                germanHierarchy.VisibilityAutomationName);
            Assert.AreNotEqual(englishUndo, window.UndoHeaderForTesting);
            StringAssert.Contains(window.UndoHeaderForTesting, "Width");
            Assert.AreNotEqual(englishStatus, window.StatusForTesting);

            string germanDiagnostic =
                UiLocalization.DiagnosticMessage(diagnostic);
            Assert.AreNotEqual(englishDiagnostic, germanDiagnostic);
            StringAssert.Contains(
                germanDiagnostic,
                "Raw English diagnostic details.");

            foreach ((string code, string culture, _) in ExpectedLanguages)
            {
                UiLocalization.Apply(code);
                Assert.AreEqual(code, UiLocalization.EffectiveLanguage);
                Assert.AreEqual(
                    culture.ToUpperInvariant(),
                    window.Language.IetfLanguageTag.ToUpperInvariant());
                Assert.AreEqual(documentText, document.Text);
                Assert.AreEqual(dirty, document.IsDirty);
                Assert.AreEqual(
                    "Width",
                    window.InspectorProperties.Single(
                        static row => row.Name == "Width").Name);
            }
        }
        finally
        {
            UiLocalization.Apply("En");
        }
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void EveryLocaleConstructsAndRendersTheMainWindowAndAllDialogs()
    {
        EnsureApplication();
        string temporary = Directory.CreateTempSubdirectory(
            "xui-localization-tests-").FullName;
        try
        {
            XuiPropertyDefinition width =
                XuiClassCatalog.Default.FindProperty("Width")!;
            XuiCatalogPropertySelection[] selections =
            [
                new(width, "100", IsAuthored: true),
            ];
            XuiWorkspaceResourceService workspace = new(temporary);
            DyingLightInstallIndex index = new(
                new DyingLightInstallProfile(temporary, "Ru"));

            foreach ((string code, string culture, _) in ExpectedLanguages)
            {
                UiLocalization.Apply(code);
                Assert.IsInstanceOfType<FontFamily>(
                    Application.Current.TryFindResource("Ui.FontFamily"));

                using MainWindow main = new();
                XuiDocument sampleDocument = XuiDocument.FromText(
                    "<XuiCanvas><Properties><Id>Root</Id>" +
                    "<Width>1280</Width><Height>720</Height></Properties>" +
                    "<MyText><Properties><Id>LocalizedPreview</Id>" +
                    "<Width>420</Width><Height>60</Height>" +
                    "<Text>Localized UI preview</Text>" +
                    "</Properties></MyText></XuiCanvas>");
                XuiSyntaxNode sampleText =
                    XuiModelReader.VisualDescendants(
                        sampleDocument.Root).Single();
                main.AttachDocumentForTesting(sampleDocument);
                main.SelectNodeKeysForTesting([sampleText.Key]);
                Window[] dialogs =
                [
                    new AssetRootsWindow(new EditorSettings()),
                    new AddXuiElementWindow(
                        "Root",
                        new XuiVector2(1280, 720),
                        static _ => "NewElement"),
                    new AddXuiPropertyWindow(
                        "Root",
                        [width]),
                    new CopyXuiPropertiesWindow(
                        "Root",
                        "XuiCanvas",
                        selections),
                    new GridSettingsWindow(new EditorSettings()),
                    new ReferenceReplacementWindow(
                        workspace,
                        "OldReference"),
                    new StockXuiBrowserWindow(index),
                ];
                try
                {
                    RenderWindow(main, culture, code);
                    string? screenshotDirectory = Environment.GetEnvironmentVariable(
                        "XUI_EDITOR_LOCALIZATION_SCREENSHOT_DIR");
                    if (!string.IsNullOrWhiteSpace(screenshotDirectory))
                    {
                        Directory.CreateDirectory(screenshotDirectory);
                        SaveWindowPng(
                            main,
                            Path.Combine(
                                screenshotDirectory,
                                $"MainWindow.{code}.png"));
                    }

                    foreach (Window dialog in dialogs)
                    {
                        RenderWindow(dialog, culture, code);
                    }
                }
                finally
                {
                    foreach (Window dialog in dialogs)
                    {
                        dialog.Close();
                    }
                }
            }
        }
        finally
        {
            UiLocalization.Apply("En");
            Directory.Delete(temporary, recursive: true);
        }
    }

    [TestMethod]
    public void EditorOwnedSourceLiteralsMustUseLocalizationResources()
    {
        string wpfDirectory = Path.Combine(
            RepositoryRoot,
            "src",
            "XuiEditor.Wpf");
        List<string> xamlFailures = [];
        HashSet<string> localizableAttributes = new(
            [
                "Title",
                "Header",
                "Content",
                "Text",
                "ToolTip",
                "AutomationProperties.Name",
            ],
            StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(
                     wpfDirectory,
                     "*Window.xaml",
                     SearchOption.TopDirectoryOnly))
        {
            XDocument document = XDocument.Load(
                path,
                LoadOptions.SetLineInfo);
            foreach (XAttribute attribute in document
                         .Descendants()
                         .Attributes()
                         .Where(attribute =>
                             localizableAttributes.Contains(
                                 attribute.Name.LocalName)))
            {
                string value = attribute.Value;
                if (IsAllowedXamlLiteral(value))
                {
                    continue;
                }

                int line = ((IXmlLineInfo)attribute).LineNumber;
                xamlFailures.Add(
                    $"{Path.GetFileName(path)}:{line}: " +
                    $"{attribute.Name.LocalName}={value}");
            }
        }

        Assert.AreEqual(
            0,
            xamlFailures.Count,
            "Unlocalized XAML literals:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, xamlFailures));

        Regex directUiAssignment = new(
            @"\.(?:Text|Title|Header|ToolTip|Content)\s*=\s*" +
            @"""(?:\\.|[^""])*""|" +
            @"\bFilter\s*=\s*""(?:\\.|[^""])*""",
            RegexOptions.CultureInvariant);
        Regex directMessageBox = new(
            @"MessageBox\.Show\s*\(\s*(?:this\s*,\s*)?""",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        Regex directStatus = new(
            @"\bSet(?:Asset)?Status\s*\(\s*""(?!Ui\.)",
            RegexOptions.CultureInvariant);
        List<string> sourceFailures = [];
        foreach (string path in Directory.EnumerateFiles(
                     wpfDirectory,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(path);
            foreach (Regex pattern in new[]
                     {
                         directUiAssignment,
                         directMessageBox,
                         directStatus,
                     })
            {
                foreach (Match match in pattern.Matches(source))
                {
                    if (IsAllowedCSharpLiteral(match.Value))
                    {
                        continue;
                    }

                    int line = 1 + source.AsSpan(
                            0,
                            match.Index)
                        .Count('\n');
                    sourceFailures.Add(
                        $"{Path.GetRelativePath(RepositoryRoot, path)}:" +
                        $"{line}: {match.Value}");
                }
            }
        }

        Assert.AreEqual(
            0,
            sourceFailures.Count,
            "Unlocalized C# UI literals:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, sourceFailures));
    }

    [STATestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public void CoreMessagesKeepEnglishFallbacksAndCanBeReformatted()
    {
        EnsureApplication();
        UiLocalization.Apply("En");
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width>" +
            "<Height>100</Height></Properties></XuiCanvas>");
        XuiSyntaxNode width =
            document.Root.FirstElement("Properties")!
                .FirstElement("Width")!;
        IXuiCommand command =
            XuiCommandFactory.SetElementValue(
                document,
                width,
                "120");
        Assert.AreEqual("Set Width", command.Description);
        Assert.IsNotNull(command.DescriptionDescriptor);
        Assert.AreEqual(
            "Set Width",
            UiLocalization.Message(
                command.DescriptionDescriptor,
                command.Description));

        document.Execute(command);
        Assert.AreEqual(
            command.Description,
            document.History.UndoDescription);
        Assert.AreSame(
            command.DescriptionDescriptor,
            document.History.UndoDescriptionDescriptor);

        XuiDocument batchDocument = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width>" +
            "<Height>100</Height></Properties></XuiCanvas>");
        XuiSyntaxNode properties =
            batchDocument.Root.FirstElement("Properties")!;
        XuiMessageDescriptor batchDescriptor = new(
            "Ui.Command.ResizeElement",
            "Resize element");
        batchDocument.ExecuteBatch(
            "Resize element",
            () =>
            {
                batchDocument.Execute(
                    XuiCommandFactory.SetElementValue(
                        batchDocument,
                        properties.FirstElement("Width")!,
                        "120"));
                properties =
                    batchDocument.Root.FirstElement("Properties")!;
                batchDocument.Execute(
                    XuiCommandFactory.SetElementValue(
                        batchDocument,
                        properties.FirstElement("Height")!,
                        "140"));
            },
            batchDescriptor);
        Assert.AreEqual(
            "Resize element",
            batchDocument.History.UndoDescription);
        Assert.AreSame(
            batchDescriptor,
            batchDocument.History.UndoDescriptionDescriptor);
        try
        {
            UiLocalization.Apply("De");
            string localized = UiLocalization.Message(
                document.History.UndoDescriptionDescriptor,
                document.History.UndoDescription!);
            Assert.AreNotEqual(command.Description, localized);
            StringAssert.Contains(localized, "Width");
            Assert.AreNotEqual(
                "Resize element",
                UiLocalization.Message(
                    batchDocument.History.UndoDescriptionDescriptor,
                    batchDocument.History.UndoDescription!));
            Assert.AreEqual(
                "Set Width",
                command.Description,
                "The source-compatible Core fallback must remain English.");
        }
        finally
        {
            UiLocalization.Apply("En");
        }
    }

    private static App EnsureApplication()
    {
        App application = Application.Current as App ?? new App();
        if (application.Resources.Count == 0)
        {
            application.InitializeComponent();
        }

        return application;
    }

    private static Dictionary<string, string> LoadXamlCatalog(string path)
    {
        XDocument document = XDocument.Load(
            path,
            LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        XNamespace xaml = XamlNamespace;
        return document.Root?.Elements().ToDictionary(
            element =>
                element.Attribute(xaml + "Key")?.Value ??
                throw new AssertFailedException(
                    $"{path} contains a resource without x:Key."),
            static element => element.Value,
            StringComparer.Ordinal) ??
            throw new AssertFailedException(
                $"{path} has no ResourceDictionary root.");
    }

    private static string[] Matches(Regex pattern, string value) =>
        pattern.Matches(value)
            .Select(static match => match.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static int Count(string value, char character) =>
        value.Count(candidate => candidate == character);

    private static void RenderWindow(
        Window window,
        string expectedCulture,
        string languageCode)
    {
        Assert.IsTrue(
            expectedCulture.Equals(
                window.Language.IetfLanguageTag,
                StringComparison.OrdinalIgnoreCase),
            $"{languageCode}/{window.GetType().Name} has the wrong language. " +
            $"Expected {expectedCulture}; actual " +
            $"{window.Language.IetfLanguageTag}.");
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(window.Title),
            $"{languageCode}/{window.GetType().Name} has an empty title.");
        FrameworkElement content =
            window.Content as FrameworkElement ??
            throw new AssertFailedException(
                $"{window.GetType().Name} has no renderable content.");
        Size size = new(1100, 760);
        content.Measure(size);
        content.Arrange(new Rect(new Point(), size));
        content.UpdateLayout();
        RenderTargetBitmap bitmap = new(
            320,
            200,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(content);
        Assert.AreEqual(320, bitmap.PixelWidth);
        Assert.AreEqual(200, bitmap.PixelHeight);
    }

    private static void SaveWindowPng(Window window, string path)
    {
        FrameworkElement content = (FrameworkElement)window.Content;
        Size size = new(1500, 930);
        content.Measure(size);
        content.Arrange(new Rect(new Point(), size));
        content.UpdateLayout();
        RenderTargetBitmap bitmap = new(
            1500,
            930,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(content);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static bool IsAllowedXamlLiteral(string value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith('{'))
        {
            return true;
        }

        if (value is "stop" or "goto" or "gotoandstop" or
            "gotoandplay")
        {
            return true;
        }

        return Regex.IsMatch(
            value,
            @"^[_\s\d%×.,+\-−←→↑↓↖↗↙↘•⧉|/()]+$",
            RegexOptions.CultureInvariant);
    }

    private static bool IsAllowedCSharpLiteral(string match) =>
        match is
            ".Text = \"0xffffffff\"" or
            ".Text = \"boxed_l_10\"" or
            ".Text = \"ButtonV\"";

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "XuiEditor.Wpf",
                    "Localization",
                    "Strings.En.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the XUI Editor repository root.");
    }
}
