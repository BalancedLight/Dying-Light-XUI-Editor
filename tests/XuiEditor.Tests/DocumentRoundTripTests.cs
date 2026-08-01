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
    public void SyntaxIndexesMatchTraversalWithDuplicateAuthoredIds()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Id>Same</Id></Properties>" +
            "<AdvGroup><Properties><Id>Same</Id></Properties>" +
            "<MyImage><Properties><Id>Same</Id></Properties></MyImage>" +
            "</AdvGroup><!-- retained --></XuiCanvas>");
        XuiSyntaxNode[] traversed = document.SyntaxTree.Document
            .DescendantsAndSelf()
            .ToArray();

        foreach (XuiSyntaxNode node in traversed)
        {
            Assert.AreSame(
                node,
                document.SyntaxTree.FindByKey(node.Key),
                node.Key);
        }

        foreach (XuiSyntaxNode node in document.Root.DescendantsAndSelf())
        {
            Assert.AreSame(
                node,
                document.SyntaxTree.FindByStart(node.Start),
                node.Key);
        }
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
    public async Task ExplicitSaveOverwritesAnExternalModification()
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

        XuiSaveResult result = await document.SaveAsync();

        Assert.AreEqual(XuiSaveDisposition.Saved, result.Disposition);
        StringAssert.Contains(await File.ReadAllTextAsync(path), "<Width>2</Width>");
        Assert.IsFalse(document.IsDirty);
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
    public async Task WritableWorkspaceInsideProtectedRootCanBeSaved()
    {
        using TestDirectory directory = new();
        string protectedRoot = directory.File("game");
        string workspace = Path.Combine(
            protectedRoot,
            "DevTools",
            "workshop",
            "ExampleProject");
        Directory.CreateDirectory(workspace);
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>2</Width></Properties></XuiCanvas>",
            options: new XuiDocumentOptions([protectedRoot], [workspace]));
        string workspacePath = Path.Combine(workspace, "screen.xui");

        XuiSaveResult result = await document.SaveAsync(workspacePath);

        Assert.AreEqual(workspacePath, result.Path);
        StringAssert.Contains(
            await File.ReadAllTextAsync(workspacePath),
            "<Width>2</Width>");
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

    [TestMethod]
    public void ElementPresetsCreateTypedDyingLightNodes()
    {
        Dictionary<XuiElementPreset, string> expectedNames = new()
        {
            [XuiElementPreset.Group] = "AdvGroup",
            [XuiElementPreset.Image] = "MyImage",
            [XuiElementPreset.Text] = "MyText",
            [XuiElementPreset.Rectangle] = "IUIAARectangle",
            [XuiElementPreset.Button] = "AdvButton",
        };

        foreach ((XuiElementPreset preset, string expectedName) in
                 expectedNames)
        {
            string raw = XuiElementFactory.CreateXml(
                new XuiElementCreationRequest
                {
                    Preset = preset,
                    Id = "New_" + preset,
                    Width = 25,
                    Height = 15,
                    Position = new Core.Values.XuiVector3(3, 4, 0),
                    Text = "Hello & goodbye",
                    ImagePath = "custom_image",
                    Color = "0xff123456",
                    Font = "boxed_l_10",
                    Visual = "ButtonV",
                },
                "\n");
            XuiDocument fragment = XuiDocument.FromText(raw);

            Assert.AreEqual(expectedName, fragment.Root.Name);
            Assert.AreEqual(
                "New_" + preset,
                XuiModelReader.GetId(fragment.Root, fragment.Text));
            Assert.AreEqual(
                "3.000000,4.000000,0.000000",
                XuiModelReader.GetPropertyValue(
                    fragment.Root,
                    fragment.Text,
                    "Position"));
        }
    }

    [TestMethod]
    public void VisualChildInsertionPrecedesTimelinesAndIsLosslesslyUndoable()
    {
        const string source =
            "<XuiCanvas version='000c'>\r\n" +
            "  <Properties><Width>100</Width><Height>100</Height></Properties>\r\n" +
            "  <!-- retain this comment -->\r\n" +
            "  <Timelines><NamedFrames /></Timelines>\r\n" +
            "</XuiCanvas>\r\n";
        XuiDocument document = XuiDocument.FromText(source);
        string child = XuiElementFactory.CreateXml(
            new XuiElementCreationRequest
            {
                Preset = XuiElementPreset.Image,
                Id = "I_New",
                Width = 20,
                Height = 10,
                Position = new Core.Values.XuiVector3(5, 6, 0),
                ImagePath = "white",
                Color = "0xffabcdef",
            },
            document.Format.NewLine);

        document.Execute(XuiCommandFactory.InsertVisualChildXml(
            document,
            document.Root,
            child));

        int image = document.Text.IndexOf("<MyImage>", StringComparison.Ordinal);
        int timelines = document.Text.IndexOf(
            "<Timelines>",
            StringComparison.Ordinal);
        Assert.IsGreaterThan(0, image);
        Assert.IsGreaterThan(image, timelines);
        StringAssert.Contains(document.Text, "<!-- retain this comment -->");
        Assert.AreEqual(
            "I_New",
            XuiModelReader.GetId(
                XuiModelReader.VisualDescendants(document.Root).Single(),
                document.Text));

        document.Undo();
        Assert.AreEqual(source, document.Text);
        document.Redo();
        StringAssert.Contains(document.Text, "<Id>I_New</Id>");
    }

    [TestMethod]
    public void VisualChildInsertionExpandsSelfClosingParent()
    {
        XuiDocument document = XuiDocument.FromText(
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><AdvGroup /></XuiCanvas>");
        XuiSyntaxNode parent =
            XuiModelReader.VisualChildren(document.Root).Single();
        string child = XuiElementFactory.CreateXml(
            new XuiElementCreationRequest
            {
                Preset = XuiElementPreset.Text,
                Id = "T_Child",
                Width = 80,
                Height = 20,
                Position = default,
                Text = "Child",
            },
            document.Format.NewLine);

        document.Execute(XuiCommandFactory.InsertVisualChildXml(
            document,
            parent,
            child));

        XuiSyntaxNode currentParent =
            XuiModelReader.VisualChildren(document.Root).Single();
        Assert.IsFalse(currentParent.IsSelfClosing);
        Assert.AreEqual(
            "T_Child",
            XuiModelReader.GetId(
                XuiModelReader.VisualChildren(currentParent).Single(),
                document.Text));
    }

    [TestMethod]
    public void VisualChildInsertionRejectsDuplicateIdsTransactionally()
    {
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>100</Height>" +
            "</Properties><MyImage><Properties><Id>Existing</Id>" +
            "</Properties></MyImage></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        string duplicate =
            "<AdvGroup><Properties><Id>Existing</Id></Properties></AdvGroup>";

        Assert.ThrowsExactly<InvalidDataException>(() =>
            XuiCommandFactory.InsertVisualChildXml(
                document,
                document.Root,
                duplicate));
        Assert.AreEqual(source, document.Text);
        Assert.IsFalse(document.History.CanUndo);
    }

    [TestMethod]
    public void VisualParentWrapPreservesTheChildAndIsUndoable()
    {
        const string source =
            "<XuiCanvas version='000c'>\r\n" +
            "  <Properties><Width>100</Width><Height>80</Height></Properties>\r\n" +
            "  <!-- keep this comment -->\r\n" +
            "  <MyImage custom=\"keep\"><Properties><Id>Child</Id>" +
            "<Width>20</Width><Height>10</Height><Position>4,5,0</Position>" +
            "</Properties></MyImage>\r\n" +
            "  <MyText><Properties><Id>Sibling</Id></Properties></MyText>\r\n" +
            "</XuiCanvas>\r\n";
        XuiDocument document = XuiDocument.FromText(source);
        XuiSyntaxNode child =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Child");
        string wrapper = XuiElementFactory.CreateXml(
            new XuiElementCreationRequest
            {
                Preset = XuiElementPreset.Group,
                Id = "G_Wrapper",
                Width = 100,
                Height = 80,
                Position = default,
            },
            document.Format.NewLine);

        document.Execute(XuiCommandFactory.WrapWithVisualParentXml(
            document,
            child,
            wrapper));

        XuiSyntaxNode currentChild =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Child");
        Assert.AreEqual(
            "G_Wrapper",
            XuiModelReader.GetId(currentChild.Parent!, document.Text));
        StringAssert.Contains(document.Text, "custom=\"keep\"");
        StringAssert.Contains(document.Text, "<!-- keep this comment -->");
        StringAssert.Contains(document.Text, "<Id>Sibling</Id>");
        Assert.AreEqual(
            "4,5,0",
            XuiModelReader.GetPropertyValue(
                currentChild,
                document.Text,
                "Position"));

        document.Undo();
        Assert.AreEqual(source, document.Text);
        document.Redo();
        StringAssert.Contains(document.Text, "<Id>G_Wrapper</Id>");
    }

    [TestMethod]
    public void VisualParentWrapRejectsDuplicateIdsWithoutChangingSource()
    {
        const string source =
            "<XuiCanvas><Properties><Width>100</Width><Height>80</Height>" +
            "</Properties><AdvGroup><Properties><Id>Existing</Id>" +
            "</Properties></AdvGroup><MyImage><Properties><Id>Child</Id>" +
            "</Properties></MyImage></XuiCanvas>";
        XuiDocument document = XuiDocument.FromText(source);
        XuiSyntaxNode child =
            XuiModelReader.VisualDescendants(document.Root).Single(node =>
                XuiModelReader.GetId(node, document.Text) == "Child");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            XuiCommandFactory.WrapWithVisualParentXml(
                document,
                child,
                "<AdvGroup><Properties><Id>Existing</Id></Properties></AdvGroup>"));
        Assert.AreEqual(source, document.Text);
        Assert.IsFalse(document.History.CanUndo);
    }
}
