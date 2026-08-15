using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Schema;

namespace XuiEditor.Tests;

[TestClass]
public sealed class PressKeyCatalogTests
{
    private static readonly (string Id, int Canonical, int? RuntimeAlias)[]
        ExpectedValues =
        [
            ("None", 22594, null),
            ("PadA", 22528, 3840),
            ("PadB", 22529, 3841),
            ("PadX", 22530, 3842),
            ("PadY", 22531, 3843),
            ("PadAOrStart", 22592, null),
            ("PadBOrBack", 22593, null),
            ("PadRShoulder", 22532, 3845),
            ("PadLShoulder", 22533, 3844),
            ("PadRTrigger", 22535, 7941),
            ("PadLTrigger", 22534, 7938),
            ("DPadUp", 22544, 3850),
            ("DPadDown", 22545, 3851),
            ("DPadLeft", 22546, 3852),
            ("DPadRight", 22547, 3853),
            ("PadStart", 22548, 3847),
            ("PadBack", 22549, 3846),
            ("PadLThumbPress", 22550, 3848),
            ("PadRThumbPress", 22551, 3849),
        ];

    private static readonly (
        string Id,
        string KeyboardHint,
        string KeyboardLabel,
        string GamepadHint,
        string GamepadLabel,
        bool UsesKeyboardBackground)[] ExpectedPreviewMappings =
        [
            ("None", "", "", "", "", false),
            ("PadA", "&[PC_ENTER]&", "Enter", "&[A]&", "A", false),
            ("PadB", "&[PC_ESC]&", "Esc", "&[B]&", "B", false),
            ("PadX", "F", "F", "&[X]&", "X", true),
            ("PadY", "C", "C", "&[Y]&", "Y", true),
            ("PadAOrStart", "&[PC_ENTER]&", "Enter", "&[A]&", "A / Start", false),
            ("PadBOrBack", "&[PC_ESC]&", "Esc", "&[B]&", "B / Back", false),
            ("PadRShoulder", "E", "E", "&[RB]&", "RB", true),
            ("PadLShoulder", "Q", "Q", "&[LB]&", "LB", true),
            ("PadRTrigger", "&[PC_RT]&", "F7", "&[RT]&", "RT", true),
            ("PadLTrigger", "&[PC_LT]&", "F6", "&[LT]&", "LT", true),
            ("DPadUp", "&[ArrowUp]&", "Up Arrow", "&[DpadUp]&", "D-pad Up", false),
            ("DPadDown", "&[ArrowDown]&", "Down Arrow", "&[DpadDown]&", "D-pad Down", false),
            ("DPadLeft", "&[ArrowLeft]&", "Left Arrow", "&[DpadLeft]&", "D-pad Left", false),
            ("DPadRight", "&[ArrowRight]&", "Right Arrow", "&[DpadRight]&", "D-pad Right", false),
            ("PadStart", "&[PC_START]&", "F4", "&[Start]&", "Start", true),
            ("PadBack", "&[PC_BACK]&", "F5", "&[Back]&", "Back", true),
            ("PadLThumbPress", "&[PC_LThumb]&", "Z", "&[L3]&", "L3", true),
            ("PadRThumbPress", "&[PC_RThumb]&", "X", "&[R3]&", "R3", true),
        ];

    [TestMethod]
    public void ExactPressKeyTableResolvesCanonicalValuesAndRuntimeAliases()
    {
        Assert.HasCount(19, XuiPressKeyCatalog.Options);
        Assert.AreEqual(
            19,
            XuiPressKeyCatalog.Options
                .Select(static option => option.CanonicalSerializedValue)
                .Distinct()
                .Count());

        foreach ((string id, int canonical, int? runtimeAlias) in
                 ExpectedValues)
        {
            XuiPressKeyOption option = XuiPressKeyCatalog.Options.Single(
                candidate => candidate.Id == id);
            Assert.AreEqual(canonical, option.CanonicalSerializedValue, id);
            Assert.AreEqual(
                canonical.ToString(CultureInfo.InvariantCulture),
                option.CanonicalText,
                id);
            Assert.IsTrue(XuiPressKeyCatalog.TryResolve(
                canonical,
                out XuiPressKeyOption canonicalMatch));
            Assert.AreSame(option, canonicalMatch, id);
            Assert.AreEqual(
                option.CanonicalText,
                XuiPressKeyCatalog.Canonicalize(canonicalMatch),
                id);

            if (runtimeAlias is int alias)
            {
                Assert.IsTrue(XuiPressKeyCatalog.TryResolve(
                    alias,
                    out XuiPressKeyOption aliasMatch));
                Assert.AreSame(option, aliasMatch, id);
                Assert.AreEqual(option.CanonicalText, aliasMatch.CanonicalText);
            }
            else
            {
                Assert.HasCount(0, option.RuntimeAliases, id);
            }
        }

        Assert.IsFalse(XuiPressKeyCatalog.TryResolve(123456, out _));
        Assert.IsFalse(XuiPressKeyCatalog.TryResolve("not-a-number", out _));
    }

    [TestMethod]
    public void PressKeyPreviewMappingsMatchTheRecoveredEditorBindings()
    {
        foreach ((
                     string id,
                     string keyboardHint,
                     string keyboardLabel,
                     string gamepadHint,
                     string gamepadLabel,
                     bool usesKeyboardBackground) in ExpectedPreviewMappings)
        {
            XuiPressKeyOption option = XuiPressKeyCatalog.Options.Single(
                candidate => candidate.Id == id);
            Assert.AreEqual(keyboardHint, option.KeyboardHint, id);
            Assert.AreEqual(keyboardLabel, option.KeyboardLabel, id);
            Assert.AreEqual(gamepadHint, option.GamepadHint, id);
            Assert.AreEqual(gamepadLabel, option.GamepadLabel, id);
            Assert.AreEqual(
                usesKeyboardBackground,
                option.KeyboardHintUsesSeparateBackground,
                id);
        }
    }

    [TestMethod]
    public void PressKeyCatalogMetadataSelectsThePaletteAndNoneDefault()
    {
        XuiPropertyDefinition definition =
            XuiClassCatalog.Default.FindProperty("PressKey")!;
        Assert.AreEqual(XuiPropertyType.WholeNumber, definition.Type);
        Assert.AreEqual("Navigation", definition.Category);
        Assert.AreEqual("22594", definition.DefaultValue);
        Assert.IsFalse(definition.IsAdvanced);
        Assert.AreEqual(
            XuiEvidenceLevel.DyingLightBinary,
            definition.Evidence);
        Assert.AreEqual(XuiPreviewSupport.Exact, definition.PreviewSupport);
        Assert.AreEqual(
            XuiPropertyEditorKind.PressKeyPalette,
            definition.EditorKind);

        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><AdvButton><Properties><Id>Button</Id>" +
            "</Properties></AdvButton></XuiCanvas>");
        XuiSyntaxNode button = XuiModelReader.VisualDescendants(document.Root)
            .Single(static node => node.Name == "AdvButton");
        XuiCatalogPropertySelection selection = XuiClassCatalog.Default
            .SelectProperties([button], document.Text, includeAdvanced: false)
            .Single(static property =>
                property.Definition.Name == "PressKey");
        Assert.IsFalse(selection.IsAuthored);
        Assert.AreEqual("22594", selection.EffectiveValue);
    }
}
