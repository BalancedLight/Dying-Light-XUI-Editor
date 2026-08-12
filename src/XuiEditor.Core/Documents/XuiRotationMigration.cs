using XuiEditor.Core.Values;

namespace XuiEditor.Core.Documents;

public static class XuiRotationMigration
{
    public static int NormalizeForGame(XuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<XuiTextPatch> patches = BuildDocumentPatches(
            document.Root,
            document.Text);
        if (patches.Count == 0)
        {
            return 0;
        }

        document.Execute(new XuiTextPatchCommand(
            document,
            "Repair Dying Light rotations",
            patches));
        return patches.Count;
    }

    public static string NormalizeKeyFrameXml(
        string rawXml,
        IEnumerable<int> rotationPropertyIndexes,
        XuiTextFormat format)
    {
        ArgumentNullException.ThrowIfNull(rawXml);
        ArgumentNullException.ThrowIfNull(rotationPropertyIndexes);
        XuiDocument fragment = XuiDocument.FromText(rawXml, format: format);
        List<XuiSyntaxNode> props = fragment.Root.Elements("Prop").ToList();
        List<XuiTextPatch> patches = [];
        foreach (int index in rotationPropertyIndexes.Distinct())
        {
            if (index < 0 || index >= props.Count)
            {
                continue;
            }

            AddPatch(fragment.Text, props[index], patches);
        }

        return ApplyPatches(fragment.Text, patches);
    }

    private static List<XuiTextPatch> BuildDocumentPatches(
        XuiSyntaxNode root,
        string source)
    {
        List<XuiTextPatch> patches = [];
        foreach (XuiSyntaxNode rotation in root
                     .DescendantsAndSelf()
                     .Where(static node =>
                         node.Name == "Rotation" &&
                         node.Parent?.Name == "Properties"))
        {
            AddPatch(source, rotation, patches);
        }

        foreach (XuiSyntaxNode timeline in root
                     .DescendantsAndSelf()
                     .Where(static node => node.Name == "Timeline"))
        {
            int[] rotationIndexes = timeline.Elements("TimelineProp")
                .Select(static (node, index) => (Node: node, Index: index))
                .Where(pair => string.Equals(
                    pair.Node.GetDecodedValue(source).Trim(),
                    "Rotation",
                    StringComparison.Ordinal))
                .Select(static pair => pair.Index)
                .ToArray();
            if (rotationIndexes.Length == 0)
            {
                continue;
            }

            foreach (XuiSyntaxNode keyFrame in timeline.Elements("KeyFrame"))
            {
                List<XuiSyntaxNode> props = keyFrame.Elements("Prop").ToList();
                foreach (int index in rotationIndexes)
                {
                    if (index < props.Count)
                    {
                        AddPatch(source, props[index], patches);
                    }
                }
            }
        }

        return patches;
    }

    private static void AddPatch(
        string source,
        XuiSyntaxNode element,
        List<XuiTextPatch> patches)
    {
        string authored = element.GetDecodedValue(source);
        if (!XuiRotationCodec.TryNormalizeForSave(
                authored,
                out string canonical,
                out XuiRotationSourceKind sourceKind))
        {
            throw new InvalidOperationException(
                $"Rotation value '{authored}' is not a finite scalar, " +
                "three-component Euler value, or non-zero quaternion.");
        }

        bool needsRepair = sourceKind != XuiRotationSourceKind.Quaternion ||
                           !string.Equals(
                               authored.Trim(),
                               canonical,
                               StringComparison.Ordinal);
        if (!needsRepair ||
            !element.TryGetContentSpan(out var span))
        {
            return;
        }

        patches.Add(new XuiTextPatch(
            span.Start,
            source.Substring(span.Start, span.Length),
            canonical));
    }

    private static string ApplyPatches(
        string source,
        IReadOnlyList<XuiTextPatch> patches)
    {
        string result = source;
        foreach (XuiTextPatch patch in patches
                     .OrderByDescending(static patch => patch.Start))
        {
            result = string.Concat(
                result.AsSpan(0, patch.Start),
                patch.ReplacementText,
                result.AsSpan(patch.Start + patch.ExpectedText.Length));
        }

        return result;
    }
}
