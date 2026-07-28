using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Documents;

public enum XuiSyntaxKind
{
    Document,
    Element,
    Text,
    Comment,
    CData,
    ProcessingInstruction,
}

public sealed record XuiAttributeSyntax(
    string Name,
    string RawValue,
    string Value,
    SourceSpan FullSpan,
    SourceSpan ValueSpan,
    char Quote);

public sealed class XuiSyntaxNode
{
    private readonly List<XuiSyntaxNode> _children = [];
    private readonly List<XuiAttributeSyntax> _attributes = [];
    private ReadOnlyCollection<XuiSyntaxNode>? _readOnlyChildren;
    private ReadOnlyCollection<XuiAttributeSyntax>? _readOnlyAttributes;

    internal XuiSyntaxNode(XuiSyntaxKind kind, string name, int start)
    {
        Kind = kind;
        Name = name;
        Start = start;
    }

    public XuiSyntaxKind Kind { get; }

    public string Name { get; }

    public string Key { get; internal set; } = string.Empty;

    public int Start { get; internal set; }

    public int End { get; internal set; }

    public int StartTagEnd { get; internal set; }

    public int ContentStart { get; internal set; }

    public int ContentEnd { get; internal set; }

    public int EndTagStart { get; internal set; } = -1;

    public bool IsSelfClosing { get; internal set; }

    public SourceSpan Span => new(Start, checked(End - Start));

    public XuiSyntaxNode? Parent { get; internal set; }

    public IReadOnlyList<XuiSyntaxNode> Children =>
        _readOnlyChildren ??= _children.AsReadOnly();

    public IReadOnlyList<XuiAttributeSyntax> Attributes =>
        _readOnlyAttributes ??= _attributes.AsReadOnly();

    internal void AddChild(XuiSyntaxNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    internal void AddAttribute(XuiAttributeSyntax attribute) => _attributes.Add(attribute);

    public IEnumerable<XuiSyntaxNode> ElementChildren =>
        _children.Where(static child => child.Kind == XuiSyntaxKind.Element);

    public IEnumerable<XuiSyntaxNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (XuiSyntaxNode child in _children)
        {
            foreach (XuiSyntaxNode descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }

    public XuiSyntaxNode? FirstElement(string name) =>
        ElementChildren.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.Ordinal));

    public IEnumerable<XuiSyntaxNode> Elements(string name) =>
        ElementChildren.Where(child => string.Equals(child.Name, name, StringComparison.Ordinal));

    public XuiAttributeSyntax? Attribute(string name) =>
        _attributes.LastOrDefault(attribute =>
            string.Equals(attribute.Name, name, StringComparison.Ordinal));

    public bool TryGetContentSpan(out SourceSpan span)
    {
        if (Kind != XuiSyntaxKind.Element || IsSelfClosing || EndTagStart < 0)
        {
            span = default;
            return false;
        }

        span = new SourceSpan(ContentStart, checked(ContentEnd - ContentStart));
        return true;
    }

    public string GetRawContent(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return TryGetContentSpan(out SourceSpan span)
            ? source.Substring(span.Start, span.Length)
            : string.Empty;
    }

    public string GetDecodedValue(string source) =>
        WebUtility.HtmlDecode(GetRawContent(source).Trim());
}

public sealed record XuiTextFormat(
    Encoding Encoding,
    bool HasByteOrderMark,
    string NewLine)
{
    public byte[] Encode(string text)
    {
        byte[] content = Encoding.GetBytes(text);
        if (!HasByteOrderMark)
        {
            return content;
        }

        byte[] preamble = Encoding.GetPreamble();
        byte[] bytes = new byte[preamble.Length + content.Length];
        preamble.CopyTo(bytes, 0);
        content.CopyTo(bytes, preamble.Length);
        return bytes;
    }
}

public sealed class XuiSyntaxTree
{
    private readonly Dictionary<string, XuiSyntaxNode> _nodesByKey;
    private readonly Dictionary<int, XuiSyntaxNode> _nodesByStart;

    internal XuiSyntaxTree(
        string source,
        XuiSyntaxNode document,
        XuiSyntaxNode root,
        XuiTextFormat format)
    {
        Source = source;
        Document = document;
        Root = root;
        Format = format;
        _nodesByKey = new Dictionary<string, XuiSyntaxNode>(
            StringComparer.Ordinal);
        _nodesByStart = [];
        foreach (XuiSyntaxNode node in document.DescendantsAndSelf())
        {
            if (!_nodesByKey.TryAdd(node.Key, node))
            {
                throw new XuiParseException(
                    $"The parser produced duplicate syntax key '{node.Key}'.");
            }

        }

        foreach (XuiSyntaxNode node in root.DescendantsAndSelf())
        {
            _nodesByStart.TryAdd(node.Start, node);
        }

        foreach (XuiSyntaxNode node in document.DescendantsAndSelf())
        {
            _nodesByStart.TryAdd(node.Start, node);
        }
    }

    public string Source { get; }

    public XuiSyntaxNode Document { get; }

    public XuiSyntaxNode Root { get; }

    public XuiTextFormat Format { get; }

    public XuiSyntaxNode? FindByKey(string key) =>
        _nodesByKey.GetValueOrDefault(key);

    public XuiSyntaxNode? FindByStart(int start) =>
        _nodesByStart.GetValueOrDefault(start);
}

public sealed class XuiParseException : Exception
{
    public XuiParseException(string message, SourceSpan? span = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Span = span;
    }

    public SourceSpan? Span { get; }
}
