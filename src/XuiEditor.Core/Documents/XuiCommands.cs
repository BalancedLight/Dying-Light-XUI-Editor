using System.Net;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Documents;

public interface IXuiCommand
{
    string Description { get; }

    void Execute(XuiDocument document);

    void Undo(XuiDocument document);
}

public sealed class XuiCommandHistory
{
    private readonly XuiDocument _document;
    private readonly Stack<IXuiCommand> _undo = [];
    private readonly Stack<IXuiCommand> _redo = [];

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

    public void Execute(IXuiCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute(_document);
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

        command.Undo(_document);
        _redo.Push(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out IXuiCommand? command))
        {
            return;
        }

        command.Execute(_document);
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

public sealed class XuiTextEditCommand : IXuiCommand
{
    private readonly int _start;
    private readonly string _oldText;
    private readonly string _newText;

    public XuiTextEditCommand(
        string description,
        int start,
        string oldText,
        string newText)
    {
        Description = description;
        _start = start;
        _oldText = oldText;
        _newText = newText;
    }

    public string Description { get; }

    public void Execute(XuiDocument document) =>
        document.ApplyValidatedEdit(_start, _oldText, _newText);

    public void Undo(XuiDocument document) =>
        document.ApplyValidatedEdit(_start, _newText, _oldText);
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
                encoded);
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
            expanded);
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
            normalized);
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
            insertion);
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
            string.Empty);
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
            duplicate);
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
            newText);
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

        List<XuiSyntaxNode> destinationChildren =
            XuiModelReader.VisualChildren(newParent)
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
        return new XuiTextEditCommand(
            $"Reparent {element.Name}",
            0,
            document.Text,
            replacement);
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
