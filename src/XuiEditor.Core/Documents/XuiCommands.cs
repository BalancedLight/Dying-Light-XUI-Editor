using System.Net;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Documents;

public interface IXuiCommand
{
    string Description { get; }

    XuiMessageDescriptor? DescriptionDescriptor => null;

    void Execute(XuiDocument document);

    void Undo(XuiDocument document);
}

public sealed class XuiCommandHistory
{
    private readonly XuiDocument _document;
    private readonly Stack<IXuiCommand> _undo = [];
    private readonly Stack<IXuiCommand> _redo = [];
    private List<IXuiCommand>? _activeBatch;

    public XuiCommandHistory(XuiDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoDescription => _undo.TryPeek(out IXuiCommand? command)
        ? command.Description
        : null;

    public string? RedoDescription => _redo.TryPeek(out IXuiCommand? command)
        ? command.Description
        : null;

    public XuiMessageDescriptor? UndoDescriptionDescriptor =>
        _undo.TryPeek(out IXuiCommand? command)
            ? command.DescriptionDescriptor
            : null;

    public XuiMessageDescriptor? RedoDescriptionDescriptor =>
        _redo.TryPeek(out IXuiCommand? command)
            ? command.DescriptionDescriptor
            : null;

    public void Execute(IXuiCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute(_document);
        if (_activeBatch is not null)
        {
            _activeBatch.Add(command);
            return;
        }

        _undo.Push(command);
        _redo.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ExecuteBatch(
        string description,
        Action edits,
        XuiMessageDescriptor? descriptionDescriptor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(edits);
        if (_activeBatch is not null)
        {
            edits();
            return;
        }

        List<IXuiCommand> commands = [];
        _activeBatch = commands;
        try
        {
            edits();
        }
        catch
        {
            for (int index = commands.Count - 1; index >= 0; index--)
            {
                commands[index].Undo(_document);
            }

            throw;
        }
        finally
        {
            _activeBatch = null;
        }

        if (commands.Count == 0)
        {
            return;
        }

        IXuiCommand command = commands.Count == 1
            ? commands[0]
            : new XuiCompositeCommand(
                description,
                commands,
                descriptionDescriptor);
        _undo.Push(command);
        _redo.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!_undo.TryPop(out IXuiCommand? command))
        {
            return;
        }

        try
        {
            command.Undo(_document);
        }
        catch
        {
            _undo.Push(command);
            throw;
        }

        _redo.Push(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out IXuiCommand? command))
        {
            return;
        }

        try
        {
            command.Execute(_document);
        }
        catch
        {
            _redo.Push(command);
            throw;
        }

        _undo.Push(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? HistoryChanged;
}

public sealed class XuiCompositeCommand : IXuiCommand
{
    private readonly IXuiCommand[] _commands;

    public XuiCompositeCommand(
        string description,
        IReadOnlyList<IXuiCommand> commands,
        XuiMessageDescriptor? descriptionDescriptor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
        {
            throw new ArgumentException(
                "A composite XUI command must contain at least one edit.",
                nameof(commands));
        }

        Description = description;
        DescriptionDescriptor = descriptionDescriptor;
        _commands = commands.ToArray();
    }

    public string Description { get; }

    public XuiMessageDescriptor? DescriptionDescriptor { get; }

    public void Execute(XuiDocument document)
    {
        int completed = 0;
        try
        {
            for (; completed < _commands.Length; completed++)
            {
                _commands[completed].Execute(document);
            }
        }
        catch
        {
            for (int index = completed - 1; index >= 0; index--)
            {
                _commands[index].Undo(document);
            }

            throw;
        }
    }

    public void Undo(XuiDocument document)
    {
        int firstUndone = _commands.Length;
        try
        {
            for (int index = _commands.Length - 1; index >= 0; index--)
            {
                _commands[index].Undo(document);
                firstUndone = index;
            }
        }
        catch
        {
            for (int index = firstUndone; index < _commands.Length; index++)
            {
                _commands[index].Execute(document);
            }

            throw;
        }
    }
}

public sealed class XuiTextEditCommand : IXuiCommand
{
    private readonly int _start;
    private readonly string _oldText;
    private readonly string _newText;

    public XuiTextEditCommand(
        string description,
        int start,
        string oldText,
        string newText,
        XuiMessageDescriptor? descriptionDescriptor = null)
    {
        Description = description;
        _start = start;
        _oldText = oldText;
        _newText = newText;
        DescriptionDescriptor = descriptionDescriptor;
    }

    public string Description { get; }

    public XuiMessageDescriptor? DescriptionDescriptor { get; }

    public void Execute(XuiDocument document) =>
        document.ApplyValidatedEdit(_start, _oldText, _newText);

    public void Undo(XuiDocument document) =>
        document.ApplyValidatedEdit(_start, _newText, _oldText);
}

public sealed record XuiTextPatch(
    int Start,
    string ExpectedText,
    string ReplacementText);

/// <summary>
/// Applies a set of non-overlapping source patches against one document
/// revision.  The patches are composed in descending source order and the
/// resulting XML is parsed once, so creation of a complete animation is both
/// transactional and a single undo step without rewriting unrelated bytes.
/// </summary>
public sealed class XuiTextPatchCommand : IXuiCommand
{
    private readonly string _before;
    private readonly string _after;

    public XuiTextPatchCommand(
        XuiDocument document,
        string description,
        IReadOnlyList<XuiTextPatch> patches,
        XuiMessageDescriptor? descriptionDescriptor = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(patches);
        if (patches.Count == 0)
        {
            throw new ArgumentException(
                "A text-patch command requires at least one patch.",
                nameof(patches));
        }

        Description = description;
        DescriptionDescriptor = descriptionDescriptor;
        _before = document.Text;
        _after = Compose(_before, patches);
    }

    public string Description { get; }

    public XuiMessageDescriptor? DescriptionDescriptor { get; }

    public void Execute(XuiDocument document) =>
        document.ApplyValidatedEdit(0, _before, _after);

    public void Undo(XuiDocument document) =>
        document.ApplyValidatedEdit(0, _after, _before);

    private static string Compose(
        string source,
        IReadOnlyList<XuiTextPatch> patches)
    {
        XuiTextPatch[] ordered = patches
            .OrderByDescending(static patch => patch.Start)
            .ToArray();
        int previousStart = source.Length;
        string candidate = source;
        foreach (XuiTextPatch patch in ordered)
        {
            if (patch.Start < 0 ||
                patch.Start > source.Length ||
                patch.ExpectedText.Length > source.Length - patch.Start ||
                !source.AsSpan(patch.Start, patch.ExpectedText.Length)
                    .SequenceEqual(patch.ExpectedText))
            {
                throw new InvalidOperationException(
                    "An animation edit no longer matches the current XUI document revision.");
            }

            int patchEnd = patch.Start + patch.ExpectedText.Length;
            if (patchEnd > previousStart)
            {
                throw new InvalidOperationException(
                    "Animation source patches overlap.");
            }

            candidate = string.Concat(
                candidate.AsSpan(0, patch.Start),
                patch.ReplacementText,
                candidate.AsSpan(patchEnd));
            previousStart = patch.Start;
        }

        return candidate;
    }
}

public static class XuiCommandFactory
{
    public static IXuiCommand SetElementValue(
        XuiDocument document,
        XuiSyntaxNode element,
        string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(value);
        string encoded = EncodeXmlText(value);
        if (element.TryGetContentSpan(out SourceSpan contentSpan))
        {
            string previous = document.Text.Substring(contentSpan.Start, contentSpan.Length);
            return new XuiTextEditCommand(
                $"Set {element.Name}",
                contentSpan.Start,
                previous,
                encoded,
                new XuiMessageDescriptor(
                    "Ui.Command.Set",
                    "Set {0}",
                    element.Name));
        }

        if (!element.IsSelfClosing)
        {
            throw new InvalidOperationException(
                $"Element '{element.Name}' does not have an editable content span.");
        }

        string raw = document.Text.Substring(element.Start, element.End - element.Start);
        int slash = raw.LastIndexOf("/>", StringComparison.Ordinal);
        if (slash < 0)
        {
            throw new InvalidOperationException(
                $"Self-closing element '{element.Name}' is malformed.");
        }

        string expanded = string.Concat(
            raw[..slash],
            ">",
            encoded,
            "</",
            element.Name,
            ">");
        return new XuiTextEditCommand(
            $"Set {element.Name}",
            element.Start,
            raw,
            expanded,
            new XuiMessageDescriptor(
                "Ui.Command.Set",
                "Set {0}",
                element.Name));
    }

    public static IXuiCommand ReplaceElementXml(
        XuiDocument document,
        XuiSyntaxNode element,
        string rawXml)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawXml);

        string normalized = rawXml.ReplaceLineEndings(document.Format.NewLine);
        XuiSyntaxTree fragment = new XuiSyntaxParser().Parse(
            normalized,
            document.Format);
        if (fragment.Root.Kind != XuiSyntaxKind.Element)
        {
            throw new InvalidDataException(
                "Raw XML replacement must contain exactly one root element.");
        }

        string oldText = document.Text.Substring(
            element.Start,
            element.End - element.Start);
        return new XuiTextEditCommand(
            $"Replace raw XML for {element.Name}",
            element.Start,
            oldText,
            normalized,
            new XuiMessageDescriptor(
                "Ui.Command.ReplaceRawXml",
                "Replace raw XML for {0}",
                element.Name));
    }

    public static IXuiCommand AddProperty(
        XuiDocument document,
        XuiSyntaxNode owner,
        string name,
        string value)
    {
        ValidateXmlName(name);
        XuiSyntaxNode? properties = owner.FirstElement("Properties");
        if (properties is null || properties.EndTagStart < 0)
        {
            throw new InvalidOperationException(
                $"Element '{owner.Name}' has no editable Properties block.");
        }

        string indentation = DetectChildIndentation(document.Text, properties);
        string insertion = string.Concat(
            document.Format.NewLine,
            indentation,
            "<",
            name,
            ">",
            EncodeXmlText(value),
            "</",
            name,
            ">");

        int insertionOffset = properties.EndTagStart;
        XuiSyntaxNode? lastElement = properties.ElementChildren.LastOrDefault();
        if (lastElement is not null)
        {
            insertionOffset = lastElement.End;
        }

        return new XuiTextEditCommand(
            $"Add {name}",
            insertionOffset,
            string.Empty,
            insertion,
            new XuiMessageDescriptor(
                "Ui.Command.Add",
                "Add {0}",
                name));
    }

    public static IXuiCommand RemoveElement(
        XuiDocument document,
        XuiSyntaxNode element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);
        SourceSpan span = ExpandToIndentation(document.Text, element.Span);
        string oldText = document.Text.Substring(span.Start, span.Length);
        return new XuiTextEditCommand(
            $"Remove {element.Name}",
            span.Start,
            oldText,
            string.Empty,
            new XuiMessageDescriptor(
                "Ui.Command.Remove",
                "Remove {0}",
                element.Name));
    }

    public static IXuiCommand DuplicateElement(
        XuiDocument document,
        XuiSyntaxNode element)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);
        string raw = document.Text.Substring(element.Start, element.End - element.Start);
        string indentation = GetLineIndentation(document.Text, element.Start);
        string duplicate = string.Concat(document.Format.NewLine, indentation, raw);
        return new XuiTextEditCommand(
            $"Duplicate {element.Name}",
            element.End,
            string.Empty,
            duplicate,
            new XuiMessageDescriptor(
                "Ui.Command.Duplicate",
                "Duplicate {0}",
                element.Name));
    }

    public static IXuiCommand InsertChildXml(
        XuiDocument document,
        XuiSyntaxNode parent,
        string rawXml,
        string description)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawXml);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (parent.IsSelfClosing || parent.EndTagStart < 0)
        {
            throw new InvalidOperationException(
                $"Element '{parent.Name}' cannot receive child XML.");
        }

        string indentation = DetectChildIndentation(document.Text, parent);
        string normalized = rawXml.ReplaceLineEndings(document.Format.NewLine);
        string[] lines = normalized.Split(
            document.Format.NewLine,
            StringSplitOptions.None);
        string indented = string.Join(
            document.Format.NewLine,
            lines.Select(line => indentation + line));
        string insertion = string.Concat(
            document.Format.NewLine,
            indented);
        int offset = parent.EndTagStart;
        XuiSyntaxNode? last = parent.ElementChildren.LastOrDefault();
        if (last is not null)
        {
            offset = last.End;
        }

        return new XuiTextEditCommand(
            description,
            offset,
            string.Empty,
            insertion);
    }

    public static IXuiCommand InsertVisualChildXml(
        XuiDocument document,
        XuiSyntaxNode parent,
        string rawXml,
        string description = "Add XUI element")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawXml);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (parent.Kind != XuiSyntaxKind.Element ||
            XuiModelReader.IsStructural(parent))
        {
            throw new InvalidOperationException(
                $"Element '{parent.Name}' cannot receive visual children.");
        }

        string normalized = rawXml
            .ReplaceLineEndings(document.Format.NewLine)
            .Trim();
        XuiSyntaxTree fragment = new XuiSyntaxParser().Parse(
            normalized,
            document.Format);
        XuiSyntaxNode child = fragment.Root;
        if (child.Kind != XuiSyntaxKind.Element ||
            XuiModelReader.IsStructural(child))
        {
            throw new InvalidDataException(
                "A visual child must contain exactly one non-structural XUI element.");
        }

        string? rootId = XuiModelReader.GetId(child, normalized);
        if (string.IsNullOrWhiteSpace(rootId))
        {
            throw new InvalidDataException(
                "A new visual element requires a Properties/Id value.");
        }

        HashSet<string> existingIds = document.Root
            .DescendantsAndSelf()
            .Where(static node =>
                node.Kind == XuiSyntaxKind.Element &&
                !XuiModelReader.IsStructural(node))
            .Select(node => XuiModelReader.GetId(node, document.Text))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> fragmentIds = new(StringComparer.Ordinal);
        foreach (XuiSyntaxNode visual in child
                     .DescendantsAndSelf()
                     .Where(static node =>
                         node.Kind == XuiSyntaxKind.Element &&
                         !XuiModelReader.IsStructural(node)))
        {
            string? id = XuiModelReader.GetId(visual, normalized);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!fragmentIds.Add(id))
            {
                throw new InvalidDataException(
                    $"The new element contains duplicate Id '{id}'.");
            }

            if (existingIds.Contains(id))
            {
                throw new InvalidDataException(
                    $"Id '{id}' already exists in this XUI document.");
            }
        }

        string parentIndent = GetLineIndentation(
            document.Text,
            parent.Start);
        string childIndent = DetectChildIndentation(
            document.Text,
            parent);
        string indented = IndentFragment(
            normalized,
            childIndent,
            document.Format.NewLine);
        if (parent.IsSelfClosing)
        {
            string oldText = document.Text.Substring(
                parent.Start,
                parent.End - parent.Start);
            int slash = oldText.LastIndexOf("/>", StringComparison.Ordinal);
            if (slash < 0)
            {
                throw new InvalidDataException(
                    $"Self-closing element '{parent.Name}' is malformed.");
            }

            string expanded = string.Concat(
                oldText[..slash].TrimEnd(),
                ">",
                document.Format.NewLine,
                indented,
                document.Format.NewLine,
                parentIndent,
                "</",
                parent.Name,
                ">");
            return new XuiTextEditCommand(
                description,
                parent.Start,
                oldText,
                expanded);
        }

        if (parent.EndTagStart < 0)
        {
            throw new InvalidOperationException(
                $"Element '{parent.Name}' cannot receive child XML.");
        }

        XuiSyntaxNode? structuralBoundary = parent.ElementChildren
            .FirstOrDefault(static element =>
                element.Name is "Timelines" or "NamedFrames");
        int boundary = structuralBoundary?.Start ?? parent.EndTagStart;
        XuiSyntaxNode? lastVisual = XuiModelReader.VisualChildren(parent)
            .Where(childNode => childNode.End <= boundary)
            .LastOrDefault();
        XuiSyntaxNode? properties = parent.FirstElement("Properties");
        int insertionOffset = lastVisual?.End ??
                              (properties is not null &&
                               properties.End <= boundary
                                  ? properties.End
                                  : parent.StartTagEnd);
        string insertion = string.Concat(
            document.Format.NewLine,
            indented);
        return new XuiTextEditCommand(
            description,
            insertionOffset,
            string.Empty,
            insertion);
    }

    public static IXuiCommand WrapWithVisualParentXml(
        XuiDocument document,
        XuiSyntaxNode element,
        string rawParentXml,
        string description = "Add XUI parent")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawParentXml);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (element.Kind != XuiSyntaxKind.Element ||
            XuiModelReader.IsStructural(element) ||
            element.Parent is null ||
            element.Parent.Kind == XuiSyntaxKind.Document)
        {
            throw new InvalidOperationException(
                "The XUI document root cannot be wrapped in a visual parent.");
        }

        string normalized = rawParentXml
            .ReplaceLineEndings(document.Format.NewLine)
            .Trim();
        XuiSyntaxTree fragment = new XuiSyntaxParser().Parse(
            normalized,
            document.Format);
        XuiSyntaxNode wrapper = fragment.Root;
        if (wrapper.Kind != XuiSyntaxKind.Element ||
            XuiModelReader.IsStructural(wrapper) ||
            wrapper.Name.Equals(
                "XuiCanvas",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A new parent must be one non-structural visual element.");
        }

        string? wrapperId = XuiModelReader.GetId(wrapper, normalized);
        if (string.IsNullOrWhiteSpace(wrapperId))
        {
            throw new InvalidDataException(
                "A new visual parent requires a Properties/Id value.");
        }

        HashSet<string> existingIds = document.Root
            .DescendantsAndSelf()
            .Where(node =>
                node.Kind == XuiSyntaxKind.Element &&
                !XuiModelReader.IsStructural(node) &&
                (node.Start < element.Start ||
                 node.End > element.End))
            .Select(node => XuiModelReader.GetId(node, document.Text))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> wrapperIds = new(StringComparer.Ordinal);
        foreach (XuiSyntaxNode visual in wrapper
                     .DescendantsAndSelf()
                     .Where(static node =>
                         node.Kind == XuiSyntaxKind.Element &&
                         !XuiModelReader.IsStructural(node)))
        {
            string? id = XuiModelReader.GetId(visual, normalized);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!wrapperIds.Add(id))
            {
                throw new InvalidDataException(
                    $"The new parent contains duplicate Id '{id}'.");
            }

            if (existingIds.Contains(id))
            {
                throw new InvalidDataException(
                    $"Id '{id}' already exists in this XUI document.");
            }
        }

        string oldText = document.Text.Substring(
            element.Start,
            element.End - element.Start);
        XuiDocument wrapperDocument = XuiDocument.FromText(
            normalized,
            format: document.Format);
        wrapperDocument.Execute(InsertVisualChildXml(
            wrapperDocument,
            wrapperDocument.Root,
            oldText,
            description));
        return new XuiTextEditCommand(
            description,
            element.Start,
            oldText,
            wrapperDocument.Text);
    }

    public static IXuiCommand MoveSibling(
        XuiDocument document,
        XuiSyntaxNode element,
        int direction)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                "Direction must be -1 or 1.");
        }

        XuiSyntaxNode parent = element.Parent ??
            throw new InvalidOperationException("The document root cannot be reordered.");
        List<XuiSyntaxNode> siblings = parent.ElementChildren
            .Where(static child => !XuiModelReader.IsStructural(child))
            .ToList();
        int index = siblings.IndexOf(element);
        int otherIndex = index + direction;
        if (index < 0 || otherIndex < 0 || otherIndex >= siblings.Count)
        {
            throw new InvalidOperationException(
                direction < 0
                    ? "The element is already first in declaration order."
                    : "The element is already last in declaration order.");
        }

        XuiSyntaxNode first = direction < 0 ? siblings[otherIndex] : element;
        XuiSyntaxNode second = direction < 0 ? element : siblings[otherIndex];
        string firstRaw = document.Text.Substring(first.Start, first.End - first.Start);
        string between = document.Text.Substring(
            first.End,
            second.Start - first.End);
        string secondRaw = document.Text.Substring(second.Start, second.End - second.Start);
        string oldText = document.Text.Substring(
            first.Start,
            second.End - first.Start);
        string newText = string.Concat(secondRaw, between, firstRaw);
        return new XuiTextEditCommand(
            direction < 0
                ? $"Move {element.Name} up"
                : $"Move {element.Name} down",
            first.Start,
            oldText,
            newText,
            new XuiMessageDescriptor(
                direction < 0
                    ? "Ui.Command.MoveUp"
                    : "Ui.Command.MoveDown",
                direction < 0
                    ? "Move {0} up"
                    : "Move {0} down",
                element.Name));
    }

    public static IXuiCommand ReparentElement(
        XuiDocument document,
        XuiSyntaxNode element,
        XuiSyntaxNode newParent,
        int childIndex = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(newParent);
        if (element.Parent is null)
        {
            throw new InvalidOperationException(
                "The document root cannot be reparented.");
        }

        if (newParent == element ||
            element.DescendantsAndSelf().Contains(newParent))
        {
            throw new InvalidOperationException(
                "An element cannot be reparented into itself or one of its descendants.");
        }

        if (newParent.IsSelfClosing || newParent.EndTagStart < 0)
        {
            throw new InvalidOperationException(
                $"Element '{newParent.Name}' cannot receive children.");
        }

        List<XuiSyntaxNode> originalDestinationChildren =
            XuiModelReader.VisualChildren(newParent).ToList();
        bool isSiblingReorder = element.Parent == newParent;
        int originalChildIndex = isSiblingReorder
            ? originalDestinationChildren.IndexOf(element)
            : -1;
        List<XuiSyntaxNode> destinationChildren =
            originalDestinationChildren
                .Where(child => child != element)
                .ToList();
        int destinationIndex = Math.Clamp(
            childIndex,
            0,
            destinationChildren.Count);
        SourceSpan removal = ExpandToIndentation(
            document.Text,
            element.Span);
        string raw = document.Text.Substring(
            element.Start,
            element.End - element.Start);
        string destinationIndent = DetectChildIndentation(
            document.Text,
            newParent);
        string sourceIndent = GetLineIndentation(
            document.Text,
            element.Start);
        string reindented = ReindentElement(
            raw,
            sourceIndent,
            destinationIndent,
            document.Format.NewLine);

        int insertionOffset;
        string insertion;
        if (destinationIndex < destinationChildren.Count)
        {
            insertionOffset = destinationChildren[destinationIndex].Start;
            insertion = string.Concat(
                reindented,
                document.Format.NewLine,
                destinationIndent);
        }
        else if (destinationChildren.Count > 0)
        {
            insertionOffset = destinationChildren[^1].End;
            insertion = string.Concat(
                document.Format.NewLine,
                destinationIndent,
                reindented);
        }
        else
        {
            insertionOffset = newParent.EndTagStart;
            insertion = string.Concat(
                document.Format.NewLine,
                destinationIndent,
                reindented);
        }

        if (insertionOffset >= removal.Start &&
            insertionOffset <= removal.End)
        {
            throw new InvalidOperationException(
                "The requested reparent operation does not change the hierarchy.");
        }

        string withoutElement = document.Text.Remove(
            removal.Start,
            removal.Length);
        int adjustedInsertion = insertionOffset > removal.End
            ? insertionOffset - removal.Length
            : insertionOffset;
        string replacement = withoutElement.Insert(
            adjustedInsertion,
            insertion);
        bool movedUp = isSiblingReorder &&
                       destinationIndex < originalChildIndex;
        return new XuiTextEditCommand(
            isSiblingReorder
                ? movedUp
                    ? $"Move {element.Name} up"
                    : $"Move {element.Name} down"
                : $"Reparent {element.Name}",
            0,
            document.Text,
            replacement,
            new XuiMessageDescriptor(
                isSiblingReorder
                    ? movedUp
                        ? "Ui.Command.MoveUp"
                        : "Ui.Command.MoveDown"
                    : "Ui.Command.Reparent",
                isSiblingReorder
                    ? movedUp
                        ? "Move {0} up"
                        : "Move {0} down"
                    : "Reparent {0}",
                element.Name));
    }

    private static string EncodeXmlText(string value) =>
        WebUtility.HtmlEncode(value)
            .Replace("&#39;", "&apos;", StringComparison.Ordinal);

    private static void ValidateXmlName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            !(char.IsLetter(name[0]) || name[0] is '_' or ':') ||
            name.Any(character =>
                !(char.IsLetterOrDigit(character) ||
                  character is '_' or '-' or ':' or '.')))
        {
            throw new ArgumentException($"'{name}' is not a valid XML element name.", nameof(name));
        }
    }

    private static string DetectChildIndentation(string source, XuiSyntaxNode parent)
    {
        XuiSyntaxNode? firstElement = parent.ElementChildren.FirstOrDefault();
        if (firstElement is not null)
        {
            return GetLineIndentation(source, firstElement.Start);
        }

        return GetLineIndentation(source, parent.Start) + DetectIndentUnit(source);
    }

    private static string DetectIndentUnit(string source)
    {
        foreach (string line in source.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            int count = 0;
            while (count < line.Length && line[count] == ' ')
            {
                count++;
            }

            if (count > 0)
            {
                return new string(' ', count);
            }

            if (line.StartsWith('\t'))
            {
                return "\t";
            }
        }

        return "  ";
    }

    private static string GetLineIndentation(string source, int offset)
    {
        int lineStart = offset;
        while (lineStart > 0 && source[lineStart - 1] is not '\r' and not '\n')
        {
            lineStart--;
        }

        int end = lineStart;
        while (end < source.Length && source[end] is ' ' or '\t')
        {
            end++;
        }

        return source[lineStart..end];
    }

    private static string ReindentElement(
        string raw,
        string sourceIndent,
        string destinationIndent,
        string newline)
    {
        string[] lines = raw.ReplaceLineEndings(newline).Split(
            newline,
            StringSplitOptions.None);
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index];
            string relative = sourceIndent.Length > 0 &&
                              line.StartsWith(
                                  sourceIndent,
                                  StringComparison.Ordinal)
                ? line[sourceIndent.Length..]
                : line;
            lines[index] = destinationIndent + relative;
        }

        return string.Join(newline, lines);
    }

    private static string IndentFragment(
        string raw,
        string indentation,
        string newline)
    {
        string[] lines = raw
            .ReplaceLineEndings(newline)
            .Split(newline, StringSplitOptions.None);
        return string.Join(
            newline,
            lines.Select(line => indentation + line));
    }

    private static SourceSpan ExpandToIndentation(string source, SourceSpan span)
    {
        int start = span.Start;
        int lineStart = start;
        while (lineStart > 0 && source[lineStart - 1] is not '\r' and not '\n')
        {
            lineStart--;
        }

        bool onlyIndent = true;
        foreach (char character in source.AsSpan(lineStart, start - lineStart))
        {
            if (character is not ' ' and not '\t')
            {
                onlyIndent = false;
                break;
            }
        }
        if (onlyIndent)
        {
            start = lineStart;
        }

        int end = span.End;
        if (end < source.Length && source[end] == '\r')
        {
            end++;
        }

        if (end < source.Length && source[end] == '\n')
        {
            end++;
        }

        return new SourceSpan(start, end - start);
    }
}
