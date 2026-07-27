using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XuiEditor.Core.Documents;

namespace XuiEditor.Tests;

[TestClass]
public sealed class DocumentRoundTripTests
{
    private const string LosslessFixture =
        "<?xui tool='legacy'?>\r\n" +
        "<XuiCanvas version=\"000c\" odd='kept'>\r\n" +
        "  <!-- authored comment -->\r\n" +
        "  <Properties>\r\n" +
        "    <Width>1280.000000</Width>\r\n" +
        "    <Width>1280.000000</Width>\r\n" +
        "    <UnknownThing custom=\"yes\"> raw &amp; value </UnknownThing>\r\n" +
        "  </Properties>\r\n" +
        "</XuiCanvas>\r\n";

    [TestMethod]
    public async Task SavingUntouchedDocumentDoesNotRewriteBytes()
    {
        using TestDirectory directory = new();
        string path = directory.File("stock.xui");
        byte[] original = new UTF8Encoding(false).GetBytes(LosslessFixture);
        await File.WriteAllBytesAsync(path, original);

        XuiDocument document = await XuiDocument.OpenAsync(path);
        XuiSaveResult result = await document.SaveAsync();

        Assert.AreEqual(XuiSaveDisposition.Unchanged, result.Disposition);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
        Assert.IsFalse(File.Exists(path + ".bak"));
    }

    [TestMethod]
    public async Task OnePropertyEditPreservesUnrelatedRepresentation()
    {
        using TestDirectory directory = new();
        string path = directory.File("edit.xui");
        await File.WriteAllTextAsync(
            path,
            LosslessFixture,
            new UTF8Encoding(false));
        XuiDocument document = await XuiDocument.OpenAsync(path);
        XuiPropertyEntry width = XuiModelReader.GetProperties(
                document.Root,
                document.Text)
            .Last(static property => property.Name == "Width");

        document.Execute(XuiCommandFactory.SetElementValue(
            document,
            width.Element,
            "640.500000"));
        await document.SaveAsync();

        string actual = await File.ReadAllTextAsync(path);
        string expected = LosslessFixture.Replace(
            "    <Width>1280.000000</Width>\r\n" +
            "    <UnknownThing",
            "    <Width>640.500000</Width>\r\n" +
            "    <UnknownThing",
            StringComparison.Ordinal);
        Assert.AreEqual(expected, actual);
        Assert.IsTrue(File.Exists(path + ".bak"));
        Assert.AreEqual(LosslessFixture, await File.ReadAllTextAsync(path + ".bak"));
    }

    [TestMethod]
    public async Task Utf16BomAndLineEndingsSurviveAnEdit()
    {
        using TestDirectory directory = new();
        string path = directory.File("utf16.xui");
        UnicodeEncoding encoding = new(
            bigEndian: false,
            byteOrderMark: true,
            throwOnInvalidBytes: true);
        string source =
            "<XuiCanvas>\n<Properties>\n<Width>12</Width>\n</Properties>\n</XuiCanvas>\n";
        await File.WriteAllBytesAsync(
            path,
            encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray());
        XuiDocument document = await XuiDocument.OpenAsync(path);
        XuiPropertyEntry width = XuiModelReader.GetProperty(
            document.Root,
            document.Text,
            "Width")!;

        document.Execute(XuiCommandFactory.SetElementValue(
            document,
            width.Element,
            "14"));
        await document.SaveAsync();

        byte[] bytes = await File.ReadAllBytesAsync(path);
        Assert.IsTrue(bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble));
        string decoded = encoding.GetString(bytes[encoding.GetPreamble().Length..]);
        Assert.AreEqual(source.Replace(">12<", ">14<", StringComparison.Ordinal), decoded);
        Assert.IsFalse(decoded.Contains('\r'));
    }

    [TestMethod]
    [DataRow("<XuiCanvas><Broken></XuiCanvas>")]
    [DataRow("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///x'>]><XuiCanvas>&e;</XuiCanvas>")]
    [DataRow("<XuiCanvas>&notDefined;</XuiCanvas>")]
    public void MalformedOrEntityXmlFailsClosed(string source)
    {
        Assert.ThrowsExactly<XuiParseException>(() =>
            XuiDocument.FromText(source));
    }

    [TestMethod]
    public void ExcessiveNestingFailsClosed()
    {
        string source =
            string.Concat(Enumerable.Repeat("<N>", XuiSyntaxParser.MaximumDepth + 2)) +
            string.Concat(Enumerable.Repeat("</N>", XuiSyntaxParser.MaximumDepth + 2));
        Assert.ThrowsExactly<XuiParseException>(() =>
            XuiDocument.FromText(source));
    }

    [TestMethod]
    public async Task ExternalModificationBlocksAtomicSave()
    {
        using TestDirectory directory = new();
        string path = directory.File("conflict.xui");
        await File.WriteAllTextAsync(path, "<XuiCanvas><Properties><Width>1</Width></Properties></XuiCanvas>");
        XuiDocument document = await XuiDocument.OpenAsync(path);
        XuiPropertyEntry width = XuiModelReader.GetProperty(
            document.Root,
            document.Text,
            "Width")!;
        document.Execute(XuiCommandFactory.SetElementValue(
            document,
            width.Element,
            "2"));
        await File.WriteAllTextAsync(path, "<XuiCanvas><Properties><Width>3</Width></Properties></XuiCanvas>");

        IOException exception = await Assert.ThrowsExactlyAsync<IOException>(
            () => document.SaveAsync());
        StringAssert.Contains(exception.Message, "changed on disk");
        StringAssert.Contains(await File.ReadAllTextAsync(path), "<Width>3</Width>");
    }

    [TestMethod]
    public async Task ProtectedAssetRootRequiresSaveAs()
    {
        using TestDirectory directory = new();
        string protectedRoot = directory.File("game");
        string workspace = directory.File("workspace");
        Directory.CreateDirectory(protectedRoot);
        Directory.CreateDirectory(workspace);
        string sourcePath = System.IO.Path.Combine(protectedRoot, "screen.xui");
        await File.WriteAllTextAsync(
            sourcePath,
            "<XuiCanvas><Properties><Width>1</Width></Properties></XuiCanvas>");
        XuiDocument document = await XuiDocument.OpenAsync(
            sourcePath,
            new XuiDocumentOptions([protectedRoot]));
        XuiPropertyEntry width = XuiModelReader.GetProperty(
            document.Root,
            document.Text,
            "Width")!;
        document.Execute(XuiCommandFactory.SetElementValue(
            document,
            width.Element,
            "2"));

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
            () => document.SaveAsync());
        string workspacePath = System.IO.Path.Combine(workspace, "screen.xui");
        XuiSaveResult result = await document.SaveAsync(workspacePath);
        Assert.AreEqual(workspacePath, result.Path);
        StringAssert.Contains(await File.ReadAllTextAsync(workspacePath), "<Width>2</Width>");
    }

    [TestMethod]
    public void UndoRedoUsesTheSameValidatedPatch()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>1</Width></Properties></XuiCanvas>");
        XuiPropertyEntry width = XuiModelReader.GetProperty(
            document.Root,
            document.Text,
            "Width")!;
        document.Execute(XuiCommandFactory.SetElementValue(
            document,
            width.Element,
            "2"));
        StringAssert.Contains(document.Text, "<Width>2</Width>");

        document.Undo();
        StringAssert.Contains(document.Text, "<Width>1</Width>");
        document.Redo();
        StringAssert.Contains(document.Text, "<Width>2</Width>");
    }

    [TestMethod]
    public void BatchEditsUndoAndRedoAsOneCommand()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>1</Width><Height>2</Height></Properties></XuiCanvas>");

        document.ExecuteBatch("Resize element", () =>
        {
            XuiPropertyEntry width = XuiModelReader.GetProperty(
                document.Root,
                document.Text,
                "Width")!;
            document.Execute(XuiCommandFactory.SetElementValue(
                document,
                width.Element,
                "10"));
            XuiPropertyEntry height = XuiModelReader.GetProperty(
                document.Root,
                document.Text,
                "Height")!;
            document.Execute(XuiCommandFactory.SetElementValue(
                document,
                height.Element,
                "20"));
        });

        StringAssert.Contains(document.Text, "<Width>10</Width>");
        StringAssert.Contains(document.Text, "<Height>20</Height>");
        Assert.AreEqual("Resize element", document.History.UndoDescription);

        document.Undo();
        StringAssert.Contains(document.Text, "<Width>1</Width>");
        StringAssert.Contains(document.Text, "<Height>2</Height>");
        Assert.IsFalse(document.History.CanUndo);

        document.Redo();
        StringAssert.Contains(document.Text, "<Width>10</Width>");
        StringAssert.Contains(document.Text, "<Height>20</Height>");
    }

    [TestMethod]
    public void FailedBatchRollsBackEveryCompletedEdit()
    {
        const string source =
            "<XuiCanvas><Properties><Width>1</Width><Height>2</Height></Properties></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        IXuiCommand width = XuiCommandFactory.SetElementValue(
            document,
            XuiModelReader.GetProperty(
                document.Root,
                document.Text,
                "Width")!.Element,
            "100");
        IXuiCommand staleHeight = XuiCommandFactory.SetElementValue(
            document,
            XuiModelReader.GetProperty(
                document.Root,
                document.Text,
                "Height")!.Element,
            "200");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            document.ExecuteBatch("Invalid resize", () =>
            {
                document.Execute(width);
                document.Execute(staleHeight);
            }));

        Assert.AreEqual(source, document.Text);
        Assert.IsFalse(document.History.CanUndo);
        Assert.IsFalse(document.History.CanRedo);
    }

    [TestMethod]
    public void RawElementReplacementIsTransactionalAndUndoable()
    {
        string source =
            "<XuiCanvas><Properties><Width>1</Width></Properties>" +
            "<MyImage custom=\"keep\"><Properties><Id>A</Id>" +
            "<ImagePath>old</ImagePath></Properties></MyImage>" +
            "</XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        XuiSyntaxNode image = XuiModelReader.VisualDescendants(document.Root)
            .Single();
        string replacement =
            "<MyText custom=\"still-here\"><Properties><Id>A</Id>" +
            "<Text>Hello</Text></Properties></MyText>";

        document.Execute(XuiCommandFactory.ReplaceElementXml(
            document,
            image,
            replacement));

        StringAssert.Contains(document.Text, replacement);
        Assert.ThrowsExactly<XuiParseException>(() =>
            XuiCommandFactory.ReplaceElementXml(
                document,
                document.Root,
                "<XuiCanvas><broken></XuiCanvas>"));
        StringAssert.Contains(document.Text, replacement);

        document.Undo();
        Assert.AreEqual(source, document.Text);
        document.Redo();
        StringAssert.Contains(document.Text, replacement);
    }

    [TestMethod]
    public void ReparentPreservesUnrelatedBytesAndIsUndoable()
    {
        string source =
            "<XuiCanvas version='000c'>\r\n" +
            "  <!-- keep this exactly -->\r\n" +
            "  <AdvGroup><Properties><Id>A</Id></Properties>\r\n" +
            "    <MyImage odd=\"yes\"><Properties><Id>Child</Id></Properties></MyImage>\r\n" +
            "  </AdvGroup>\r\n" +
            "  <AdvGroup><Properties><Id>B</Id></Properties></AdvGroup>\r\n" +
            "</XuiCanvas>\r\n";
        XuiDocument document = XuiDocument.FromText(source);
        XuiSyntaxNode child = XuiModelReader.VisualDescendants(document.Root)
            .Single(node => XuiModelReader.GetId(node, document.Text) == "Child");
        XuiSyntaxNode destination = XuiModelReader.VisualDescendants(document.Root)
            .Single(node => XuiModelReader.GetId(node, document.Text) == "B");

        document.Execute(XuiCommandFactory.ReparentElement(
            document,
            child,
            destination));

        StringAssert.Contains(document.Text, "<!-- keep this exactly -->");
        StringAssert.Contains(document.Text, "<MyImage odd=\"yes\">");
        XuiSyntaxNode moved = XuiModelReader.VisualDescendants(document.Root)
            .Single(node => XuiModelReader.GetId(node, document.Text) == "Child");
        Assert.AreEqual(
            "B",
            XuiModelReader.GetId(moved.Parent!, document.Text));

        document.Undo();
        Assert.AreEqual(source, document.Text);
        document.Redo();
        moved = XuiModelReader.VisualDescendants(document.Root)
            .Single(node => XuiModelReader.GetId(node, document.Text) == "Child");
        Assert.AreEqual(
            "B",
            XuiModelReader.GetId(moved.Parent!, document.Text));
    }
}
