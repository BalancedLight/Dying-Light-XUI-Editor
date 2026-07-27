using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Layout;

namespace XuiEditor.Tests;

[TestClass]
public sealed class InstallAssetTests
{
    private const string InstallPath =
        @"E:\SteamLibrary\steamapps\common\Dying Light";

    [TestMethod]
    [Timeout(30_000)]
    public async Task InstallIndexExposesEveryCurrentStockXuiReadOnly()
    {
        RequireInstall();
        DyingLightInstallIndex index = new(
            new DyingLightInstallProfile(InstallPath, "En"));

        await index.RebuildAsync();

        Assert.HasCount(174, index.StockXuiFiles);
        Assert.IsTrue(index.StockXuiFiles.All(static entry =>
            entry.Origin.IsReadOnly));
        XuiAssetEntry? hud = index.Find("data/menu/hud/hud.xui");
        XuiAssetEntry? btzHud =
            index.Find("data/menu/hud/hud_btz.xui");
        XuiAssetEntry? patchedMap = index.Find(
            "data/maps/forever_foundation/forever_foundation_t2mat.scr");
        Assert.IsNotNull(hud);
        Assert.IsNotNull(btzHud);
        Assert.AreEqual("DataDLC1_0.pak", btzHud.Origin.SourceName);
        Assert.IsNotNull(patchedMap);
        Assert.AreEqual(
            XuiAssetContainerKind.LooseFile,
            patchedMap.Origin.Kind);
        Assert.IsTrue(index.Entries.Any(static entry =>
            entry.VirtualPath.Equals(
                "data/maps/forever_foundation/forever_foundation_t2mat.scr",
                StringComparison.OrdinalIgnoreCase) &&
            entry.Origin.SourceName.Equals(
                "Data2.pak",
                StringComparison.OrdinalIgnoreCase)));
        XuiDocument document = await XuiDocument.OpenAssetAsync(hud);
        Assert.AreEqual("hud.xui", document.DisplayName);
        Assert.IsNull(document.Path);
        Assert.IsTrue(document.Source?.IsReadOnly);
        Assert.IsGreaterThan(1_000, document.Root.DescendantsAndSelf().Count());
    }

    [TestMethod]
    [Timeout(90_000)]
    public async Task InstallResolverLoadsHudDwLocalizationAndBitmapFonts()
    {
        RequireInstall();
        DyingLightInstallIndex index = new(
            new DyingLightInstallProfile(InstallPath, "En"));
        DyingLightAssetResolver resolver = new(
            [],
            sources: [index],
            locale: "En");

        await resolver.RebuildAsync();
        ResolvedTexture? texture =
            await resolver.ResolveTextureAsync("aggro_skull");
        ResolvedBitmapFont? font =
            await resolver.ResolveBitmapFontAsync("boxed_m_21");

        Assert.IsNotNull(texture);
        Assert.AreEqual(20, texture.Width);
        Assert.AreEqual(20, texture.Height);
        Assert.IsFalse(texture.IsApproximation);
        Assert.IsTrue(texture.Definition.TextureFile.Contains(
            "hud_dw",
            StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(texture.SourcePath.Contains(
            "hud_dw",
            StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(texture.BgraPixels.Any(static value => value != 0));
        Assert.IsNotNull(resolver.Localization);
        Assert.HasCount(20_098, resolver.Localization.Entries);
        Assert.AreEqual(
            "Instant Escape",
            resolver.ResolveText("&Hud_FastBreak&"));
        Assert.IsNotNull(font);
        Assert.AreEqual(2048, font.AtlasWidth);
        Assert.AreEqual(4096, font.AtlasHeight);
        Assert.IsGreaterThan(400, font.Metrics.Glyphs.Count);
        Assert.IsFalse(font.Diagnostics.Any());
        XuiTextMeasurement measurement = resolver.MeasureText(
            "boxed_m_21",
            "HUNTING GOON",
            requestedSize: 0,
            maximumWidth: 300,
            multiline: false,
            uppercase: true);
        Assert.IsTrue(measurement.IsExact);
        Assert.IsGreaterThan(0, measurement.Width);
        Assert.IsGreaterThan(0, measurement.Height);
        Assert.AreEqual(1, measurement.LineCount);

        XuiAssetEntry hudEntry =
            index.Find("data/menu/hud/hud.xui")!;
        XuiDocument hud = await XuiDocument.OpenAssetAsync(hudEntry);
        XuiPreviewScenario gameplay =
            XuiPreviewScenarioCatalog.Defaults.Single(static scenario =>
                scenario.Id == "gameplay");
        XuiRenderFrame frame = DyingLightLayoutEngine.Evaluate(
            hud,
            XuiViewport.Default,
            0,
            resolver,
            new XuiRenderContext(gameplay));
        XuiTimelineSet timelines = XuiTimelineParser.Parse(hud);
        XuiRenderNode hpUnits = frame.Nodes.Single(static node =>
            node.Id == "T_Hp0");
        XuiRenderNode quest = frame.Nodes.Single(static node =>
            node.Id == "T_QuestName");
        XuiRenderNode healthBar = frame.Nodes.Single(static node =>
            node.Id == "I_Health0");
        XuiRenderNode medkitBackground = frame.Nodes.Single(static node =>
            node.Id == "I_MedpacksBack");
        Assert.IsTrue(hpUnits.IsShown);
        Assert.AreEqual(1, hpUnits.Opacity, 0.001);
        Assert.AreEqual("5", hpUnits.Text);
        Assert.IsTrue(quest.IsShown);
        Assert.AreEqual("HUNTING GOON", quest.Text);
        Assert.IsLessThan(320, quest.WorldBounds.Y);
        Assert.IsTrue(healthBar.IsShown);
        Assert.AreEqual("stat_bar_gradient", healthBar.ImagePath);
        Assert.IsNotNull(
            resolver.ResolveTextureDefinition(healthBar.ImagePath));
        Assert.IsTrue(medkitBackground.IsShown);
        Assert.AreEqual("white", medkitBackground.ImagePath);
        Assert.IsFalse(timelines.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-TL005"));
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task EveryInstalledStockXuiParsesAndEvaluatesReadOnly()
    {
        RequireInstall();
        DyingLightInstallIndex index = new(
            new DyingLightInstallProfile(InstallPath, "En"));
        DyingLightAssetResolver resolver = new(
            [],
            sources: [index],
            locale: "En");
        await resolver.RebuildAsync();

        List<string> failures = [];
        List<string> malformedStockFiles = [];
        foreach (XuiAssetEntry entry in index.StockXuiFiles)
        {
            try
            {
                XuiDocument document = await XuiDocument.OpenAssetAsync(entry);
                string original = document.Text;
                _ = XuiTimelineParser.Parse(document);
                _ = DyingLightLayoutEngine.Evaluate(
                    document,
                    XuiViewport.Default,
                    0,
                    resolver);

                Assert.IsTrue(document.Source?.IsReadOnly, entry.VirtualPath);
                Assert.IsNull(document.Path, entry.VirtualPath);
                Assert.AreEqual(original, document.Text, entry.VirtualPath);
            }
            catch (XuiParseException)
                when (entry.VirtualPath.Equals(
                    "data/menu/hud/hud_btz.xui",
                    StringComparison.OrdinalIgnoreCase))
            {
                malformedStockFiles.Add(entry.VirtualPath);
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"{entry.VirtualPath}: {exception.GetType().Name}: " +
                    exception.Message);
            }
        }

        Assert.IsEmpty(
            failures,
            string.Join(Environment.NewLine, failures.Take(20)));
        Assert.HasCount(1, malformedStockFiles);
        Assert.AreEqual(
            "data/menu/hud/hud_btz.xui",
            malformedStockFiles[0]);
    }

    private static void RequireInstall()
    {
        if (!DyingLightInstallIndex.LooksLikeInstall(InstallPath))
        {
            Assert.Inconclusive(
                "The external Dying Light installation is not available.");
        }
    }
}
