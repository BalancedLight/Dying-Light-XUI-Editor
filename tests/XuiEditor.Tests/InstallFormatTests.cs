using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Assets;

namespace XuiEditor.Tests;

[TestClass]
public sealed class InstallFormatTests
{
    [TestMethod]
    public async Task Rp6ReaderReassemblesCompressedResourceItems()
    {
        using TestDirectory directory = new();
        byte[] expected = Encoding.ASCII.GetBytes(
            "managed RP6L extraction");
        string path = directory.File("menu_PC.rpack");
        await File.WriteAllBytesAsync(path, BuildRp6(expected, "hud_dw"));

        Rp6Reader reader = Rp6Reader.Open(path);
        Rp6ResourceDescriptor resource = reader.Resources.Single();
        byte[] actual = await reader.ReadResourceAsync(resource);

        Assert.AreEqual("hud_dw", resource.Name);
        Assert.AreEqual(32, resource.PayloadType);
        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task ConfiguredDefinitionAndRpackSourcesResolveTogether()
    {
        using TestDirectory directory = new();
        string definition = directory.File("custom.def");
        string rpack = directory.File("custom_PC.rpack");
        await File.WriteAllTextAsync(
            definition,
            """
            Texture("custom_hud.dds",4,4)
            {
                Whole("custom_hud")
            }
            """);
        byte[] textureResource = new byte[151 + 8];
        BinaryPrimitives.WriteUInt16LittleEndian(textureResource, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(
            textureResource.AsSpan(2),
            4);
        BinaryPrimitives.WriteUInt16LittleEndian(
            textureResource.AsSpan(8),
            1);
        BinaryPrimitives.WriteInt32LittleEndian(
            textureResource.AsSpan(12),
            17);
        textureResource.AsSpan(151).Fill(0x5a);
        await File.WriteAllBytesAsync(
            rpack,
            BuildRp6(textureResource, "custom_hud"));
        ConfiguredAssetSource definitionSource = new(
            definition,
            XuiConfiguredAssetSourceKind.TextureDefinitionFile);
        ConfiguredAssetSource rpackSource = new(
            rpack,
            XuiConfiguredAssetSourceKind.Rp6ResourcePack);
        DyingLightAssetResolver resolver = new(
            [],
            directory.File("cache"),
            sources: [definitionSource, rpackSource]);

        await resolver.RebuildAsync();
        ResolvedTexture? texture =
            await resolver.ResolveTextureAsync("custom_hud");

        Assert.IsNotNull(texture);
        Assert.AreEqual(4, texture.Width);
        Assert.AreEqual(4, texture.Height);
        Assert.AreEqual(
            XuiAssetContainerKind.Rp6Resource,
            resolver.ResolveFile("custom_hud.dds")!.Entry!.Origin.Kind);
        Assert.AreEqual(
            Path.GetDirectoryName(definition),
            resolver.ResolveTextureDefinition("custom_hud")!
                .DefinitionRoot!.FullPath);
        StringAssert.Contains(texture.SourcePath, "custom_PC.rpack");
    }

    [TestMethod]
    public void DyingLightTextureMetadataBecomesAStandardDds()
    {
        byte[] resource = new byte[151 + 8];
        BinaryPrimitives.WriteUInt16LittleEndian(resource, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(resource.AsSpan(2), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(resource.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(resource.AsSpan(12), 17);
        resource.AsSpan(151).Fill(0x5a);

        byte[] dds = DyingLightDdsBuilder.Build(resource);

        Assert.AreEqual("DDS ", Encoding.ASCII.GetString(dds, 0, 4));
        Assert.AreEqual("DXT1", Encoding.ASCII.GetString(dds, 84, 4));
        Assert.AreEqual(136, dds.Length);
        Assert.IsTrue(dds.AsSpan(128).ToArray().All(static value =>
            value == 0x5a));
    }

    [TestMethod]
    public void LocalizationCatalogPreservesOrderAndUsesLastDuplicate()
    {
        byte[] bytes = BuildStringCatalog(
        [
            ("Hud_Title", "First"),
            ("Hud_Title", "Last"),
            ("Hud_Count", "42"),
        ]);

        LocalizationCatalog catalog = LocalizationCatalogParser.Parse(
            bytes,
            "En",
            sourcePath: "common_texts_all.bin");

        Assert.HasCount(3, catalog.Entries);
        Assert.AreEqual("Last", catalog.ResolveOrOriginal("Hud_Title"));
        Assert.AreEqual("Unknown", catalog.ResolveOrOriginal("Unknown"));
        Assert.IsTrue(catalog.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "XUI-LOC002"));
    }

    [TestMethod]
    public void BitmapFontMetricsKeepPrivateInputGlyphs()
    {
        BitmapFontParseResult parsed = BitmapFontParser.Parse(
            """
            Name("Boxed")
            MapWidth(64)
            MapHeight(64)
            FontHeight(16)
            Char(65,8,0,0,8,16,0)
            SpecialCharHeight(57344,16,8,0,24,16,0,2)
            """,
            "boxed.fm");

        Assert.IsNotNull(parsed.Metrics);
        Assert.HasCount(2, parsed.Metrics.Glyphs);
        Assert.IsTrue(parsed.Metrics.Glyphs[57344].IsSpecial);
        Assert.AreEqual(2, parsed.Metrics.Glyphs[57344].VerticalOffset);
    }

    [TestMethod]
    public async Task InstallIndexIncludesPatchDlcLocaleAndLooseLayers()
    {
        using TestDirectory directory = new();
        string install = directory.File("Dying Light");
        string dw = Path.Combine(install, "DW");
        string dlc = Path.Combine(install, "DW_DLC1");
        Directory.CreateDirectory(Path.Combine(
            dw,
            "Data",
            "menu",
            "scr"));
        Directory.CreateDirectory(dlc);
        await File.WriteAllBytesAsync(
            Path.Combine(install, "DyingLightGame.exe"),
            []);
        WritePak(
            Path.Combine(dw, "Data0.pak"),
            ("data/menu/scr/base.xui", MinimalXui("Base")));
        WritePak(
            Path.Combine(dw, "Data2.pak"),
            ("data/patch/patch.scr", "Version(2)"));
        WritePak(
            Path.Combine(dw, "DataEn.pak"),
            ("data/menu/texts/en.scr", "Locale(\"En\")"));
        WritePak(
            Path.Combine(dw, "DataFr.pak"),
            ("data/menu/texts/fr.scr", "Locale(\"Fr\")"));
        WritePak(
            Path.Combine(dw, "DataDe.pak"),
            ("data/menu/texts/de.scr", "Locale(\"De\")"));
        WritePak(
            Path.Combine(dlc, "DataDLC1_0.pak"),
            ("data/menu/hud/dlc.xui", MinimalXui("Dlc")));
        await File.WriteAllTextAsync(
            Path.Combine(dw, "Data", "menu", "scr", "base.xui"),
            MinimalXui("LooseBase"));

        DyingLightInstallIndex index = new(
            new DyingLightInstallProfile(install, "Fr"));
        await index.RebuildAsync();

        Assert.HasCount(2, index.StockXuiFiles);
        Assert.AreEqual(
            XuiAssetContainerKind.LooseFile,
            index.Find("data/menu/scr/base.xui")!.Origin.Kind);
        Assert.AreEqual(
            "DataDLC1_0.pak",
            index.Find("data/menu/hud/dlc.xui")!.Origin.SourceName);
        Assert.IsTrue(index.Entries.Any(static entry =>
            entry.Origin.SourceName == "Data2.pak"));
        Assert.IsTrue(index.Entries.Any(static entry =>
            entry.Origin.SourceName == "DataFr.pak"));
        Assert.IsTrue(index.Entries.Any(static entry =>
            entry.Origin.SourceName == "DataEn.pak"));
        Assert.IsFalse(index.Entries.Any(static entry =>
            entry.Origin.SourceName == "DataDe.pak"));
    }

    private static byte[] BuildRp6(byte[] payload, string name)
    {
        byte[] packed;
        using (MemoryStream compressed = new())
        {
            using (ZLibStream zlib = new(
                       compressed,
                       CompressionLevel.SmallestSize,
                       leaveOpen: true))
            {
                zlib.Write(payload);
            }

            packed = compressed.ToArray();
        }

        byte[] nameBlob = Encoding.UTF8.GetBytes(name + "\0");
        const int headerSize = 36;
        const int tablesSize = 20 + 16 + 12 + 4;
        int chunkOffset = headerSize + tablesSize + nameBlob.Length;
        byte[] result = new byte[chunkOffset + packed.Length];
        "RP6L"u8.CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), 1);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), 1);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(20), 1);
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(24),
            nameBlob.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), 1);

        int cursor = headerSize;
        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(cursor + 4),
            (uint)chunkOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(cursor + 8),
            (uint)payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(cursor + 12),
            packed.Length);
        cursor += 20;

        result[cursor] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(cursor + 4),
            0);
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(cursor + 8),
            payload.Length);
        cursor += 16;

        BinaryPrimitives.WriteInt16LittleEndian(
            result.AsSpan(cursor),
            1);
        BinaryPrimitives.WriteInt16LittleEndian(
            result.AsSpan(cursor + 2),
            32);
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(cursor + 4),
            0);
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(cursor + 8),
            0);
        cursor += 12;

        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(cursor),
            0);
        cursor += 4;
        nameBlob.CopyTo(result, cursor);
        packed.CopyTo(result, chunkOffset);
        return result;
    }

    private static byte[] BuildStringCatalog(
        IReadOnlyList<(string Key, string Value)> entries)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(
            stream,
            Encoding.UTF8,
            leaveOpen: true);
        writer.Write(1);
        writer.Write(entries.Count);
        foreach ((string key, string value) in entries)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            writer.Write((ushort)keyBytes.Length);
            writer.Write(keyBytes);
            writer.Write((ushort)value.Length);
            writer.Write(Encoding.Unicode.GetBytes(value));
        }

        return stream.ToArray();
    }

    private static void WritePak(
        string path,
        params (string Path, string Content)[] entries)
    {
        using ZipArchive archive = ZipFile.Open(
            path,
            ZipArchiveMode.Create);
        foreach ((string entryPath, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryPath);
            using StreamWriter writer = new(
                entry.Open(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
        }
    }

    private static string MinimalXui(string id) =>
        $"<XuiCanvas><Properties><Id>{id}</Id></Properties></XuiCanvas>";
}
