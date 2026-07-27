using System.Text;
using System.Xml;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Documents;

public sealed class XuiSyntaxParser
{
    public const int MaximumDepth = 512;
    public const int MaximumNodeCount = 500_000;
    public const int MaximumDocumentBytes = 64 * 1024 * 1024;
    private readonly bool _validateXml;

    public XuiSyntaxParser(bool validateXml = true)
    {
        _validateXml = validateXml;
    }

    public XuiSyntaxTree Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumDocumentBytes)
        {
            throw new XuiParseException(
                $"The XUI document is larger than the {MaximumDocumentBytes / (1024 * 1024)} MiB safety limit.");
        }

        XuiTextFormat format = DetectEncoding(bytes);
        int preambleLength = format.HasByteOrderMark ? format.Encoding.GetPreamble().Length : 0;
        string source;
        try
        {
            source = format.Encoding.GetString(bytes[preambleLength..]);
        }
        catch (DecoderFallbackException exception)
        {
            throw new XuiParseException("The XUI document contains invalid encoded text.", null, exception);
        }

        return Parse(source, format);
    }

    public XuiSyntaxTree Parse(string source, XuiTextFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Encoding.UTF8.GetByteCount(source) > MaximumDocumentBytes)
        {
            throw new XuiParseException(
                $"The XUI document is larger than the {MaximumDocumentBytes / (1024 * 1024)} MiB safety limit.");
        }

        if (_validateXml)
        {
            ValidateXml(source);
        }
        XuiTextFormat actualFormat = format ?? new XuiTextFormat(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            HasByteOrderMark: false,
            DetectNewLine(source));

        XuiSyntaxNode document = new(XuiSyntaxKind.Document, "#document", 0)
        {
            End = source.Length,
            ContentStart = 0,
            ContentEnd = source.Length,
            StartTagEnd = 0,
            Key = "/",
        };

        Stack<XuiSyntaxNode> stack = new();
        stack.Push(document);
        int nodeCount = 1;
        int offset = 0;
        XuiSyntaxNode? root = null;

        while (offset < source.Length)
        {
            if (source[offset] != '<')
            {
                int textEnd = source.IndexOf('<', offset);
                if (textEnd < 0)
                {
                    textEnd = source.Length;
                }

                AddLeaf(stack.Peek(), XuiSyntaxKind.Text, "#text", offset, textEnd, ref nodeCount);
                offset = textEnd;
                continue;
            }

            if (source.AsSpan(offset).StartsWith("<!--", StringComparison.Ordinal))
            {
                int end = FindRequired(source, "-->", offset + 4) + 3;
                AddLeaf(stack.Peek(), XuiSyntaxKind.Comment, "#comment", offset, end, ref nodeCount);
                offset = end;
                continue;
            }

            if (source.AsSpan(offset).StartsWith("<![CDATA[", StringComparison.Ordinal))
            {
                int end = FindRequired(source, "]]>", offset + 9) + 3;
                AddLeaf(stack.Peek(), XuiSyntaxKind.CData, "#cdata", offset, end, ref nodeCount);
                offset = end;
                continue;
            }

            if (source.AsSpan(offset).StartsWith("<?", StringComparison.Ordinal))
            {
                int end = FindRequired(source, "?>", offset + 2) + 2;
                AddLeaf(
                    stack.Peek(),
                    XuiSyntaxKind.ProcessingInstruction,
                    "#processing",
                    offset,
                    end,
                    ref nodeCount);
                offset = end;
                continue;
            }

            if (source.AsSpan(offset).StartsWith("<!", StringComparison.Ordinal))
            {
                throw new XuiParseException(
                    "DTD and declaration nodes are not supported in XUI documents.",
                    new SourceSpan(offset, Math.Min(2, source.Length - offset)));
            }

            if (source.AsSpan(offset).StartsWith("</", StringComparison.Ordinal))
            {
                int nameStart = offset + 2;
                int nameEnd = ScanName(source, nameStart);
                string name = source[nameStart..nameEnd];
                int tagEnd = FindTagEnd(source, nameEnd);

                if (stack.Count <= 1)
                {
                    throw new XuiParseException(
                        $"Unexpected closing element '{name}'.",
                        new SourceSpan(offset, tagEnd - offset));
                }

                XuiSyntaxNode current = stack.Pop();
                if (!string.Equals(current.Name, name, StringComparison.Ordinal))
                {
                    throw new XuiParseException(
                        $"Element '{current.Name}' is closed by '{name}'.",
                        new SourceSpan(offset, tagEnd - offset));
                }

                current.ContentEnd = offset;
                current.EndTagStart = offset;
                current.End = tagEnd;
                offset = tagEnd;
                continue;
            }

            int startName = offset + 1;
            int endName = ScanName(source, startName);
            string elementName = source[startName..endName];
            int startTagEnd = FindTagEnd(source, endName);
            int lastContentCharacter = startTagEnd - 2;
            while (lastContentCharacter >= endName &&
                   char.IsWhiteSpace(source[lastContentCharacter]))
            {
                lastContentCharacter--;
            }

            bool selfClosing = lastContentCharacter >= endName &&
                               source[lastContentCharacter] == '/';
            XuiSyntaxNode element = new(XuiSyntaxKind.Element, elementName, offset)
            {
                StartTagEnd = startTagEnd,
                ContentStart = startTagEnd,
                IsSelfClosing = selfClosing,
            };
            ParseAttributes(source, element, endName, startTagEnd);
            AddNode(stack.Peek(), element, ref nodeCount);
            if (stack.Peek() == document)
            {
                if (root is not null)
                {
                    throw new XuiParseException(
                        "An XUI document must contain exactly one root element.",
                        element.Span);
                }

                root = element;
            }

            if (selfClosing)
            {
                element.ContentEnd = startTagEnd;
                element.End = startTagEnd;
            }
            else
            {
                if (stack.Count >= MaximumDepth)
                {
                    throw new XuiParseException(
                        $"The XUI document exceeds the maximum depth of {MaximumDepth}.",
                        new SourceSpan(offset, startTagEnd - offset));
                }

                stack.Push(element);
            }

            offset = startTagEnd;
        }

        if (stack.Count != 1)
        {
            XuiSyntaxNode unclosed = stack.Peek();
            throw new XuiParseException(
                $"Element '{unclosed.Name}' is not closed.",
                new SourceSpan(unclosed.Start, Math.Max(1, unclosed.StartTagEnd - unclosed.Start)));
        }

        if (root is null)
        {
            throw new XuiParseException("The XUI document does not contain a root element.");
        }

        AssignKeys(document, "/");
        return new XuiSyntaxTree(source, document, root, actualFormat);
    }

    private static void ValidateXml(string source)
    {
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CheckCharacters = true,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            IgnoreWhitespace = false,
            MaxCharactersInDocument = MaximumDocumentBytes,
            MaxCharactersFromEntities = 0,
            CloseInput = true,
        };

        try
        {
            using StringReader textReader = new(source);
            using XmlReader reader = XmlReader.Create(textReader, settings);
            while (reader.Read())
            {
            }
        }
        catch (XmlException exception)
        {
            throw new XuiParseException(
                $"Invalid XUI XML at line {exception.LineNumber}, column {exception.LinePosition}: {exception.Message}",
                null,
                exception);
        }
    }

    private static void ParseAttributes(
        string source,
        XuiSyntaxNode element,
        int offset,
        int startTagEnd)
    {
        int limit = startTagEnd - 1;
        while (offset < limit)
        {
            while (offset < limit && char.IsWhiteSpace(source[offset]))
            {
                offset++;
            }

            if (offset >= limit || source[offset] == '/')
            {
                return;
            }

            int attributeStart = offset;
            int nameEnd = ScanName(source, offset);
            string name = source[offset..nameEnd];
            offset = nameEnd;
            while (offset < limit && char.IsWhiteSpace(source[offset]))
            {
                offset++;
            }

            if (offset >= limit || source[offset] != '=')
            {
                throw new XuiParseException(
                    $"Attribute '{name}' is missing '='.",
                    new SourceSpan(attributeStart, Math.Max(1, offset - attributeStart)));
            }

            offset++;
            while (offset < limit && char.IsWhiteSpace(source[offset]))
            {
                offset++;
            }

            if (offset >= limit || (source[offset] != '"' && source[offset] != '\''))
            {
                throw new XuiParseException(
                    $"Attribute '{name}' must use quotes.",
                    new SourceSpan(attributeStart, Math.Max(1, offset - attributeStart)));
            }

            char quote = source[offset++];
            int valueStart = offset;
            int valueEnd = source.IndexOf(quote, valueStart);
            if (valueEnd < 0 || valueEnd >= limit)
            {
                throw new XuiParseException(
                    $"Attribute '{name}' has no closing quote.",
                    new SourceSpan(attributeStart, Math.Max(1, limit - attributeStart)));
            }

            string rawValue = source[valueStart..valueEnd];
            offset = valueEnd + 1;
            element.AddAttribute(new XuiAttributeSyntax(
                name,
                rawValue,
                System.Net.WebUtility.HtmlDecode(rawValue),
                new SourceSpan(attributeStart, offset - attributeStart),
                new SourceSpan(valueStart, valueEnd - valueStart),
                quote));
        }
    }

    private static int ScanName(string source, int offset)
    {
        int start = offset;
        while (offset < source.Length)
        {
            char character = source[offset];
            if (!(char.IsLetterOrDigit(character) ||
                  character is '_' or '-' or ':' or '.'))
            {
                break;
            }

            offset++;
        }

        if (offset == start)
        {
            throw new XuiParseException(
                "Expected an XML name.",
                new SourceSpan(start, Math.Min(1, source.Length - start)));
        }

        return offset;
    }

    private static int FindTagEnd(string source, int offset)
    {
        char quote = '\0';
        for (int index = offset; index < source.Length; index++)
        {
            char character = source[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character == '>')
            {
                return index + 1;
            }
        }

        throw new XuiParseException(
            "An XML start tag is not closed.",
            new SourceSpan(offset, Math.Max(0, source.Length - offset)));
    }

    private static int FindRequired(string source, string marker, int offset)
    {
        int result = source.IndexOf(marker, offset, StringComparison.Ordinal);
        if (result < 0)
        {
            throw new XuiParseException(
                $"The XML construct beginning at offset {offset} is not terminated.",
                new SourceSpan(offset, Math.Max(0, source.Length - offset)));
        }

        return result;
    }

    private static void AddLeaf(
        XuiSyntaxNode parent,
        XuiSyntaxKind kind,
        string name,
        int start,
        int end,
        ref int nodeCount)
    {
        XuiSyntaxNode leaf = new(kind, name, start)
        {
            End = end,
            StartTagEnd = start,
            ContentStart = start,
            ContentEnd = end,
        };
        AddNode(parent, leaf, ref nodeCount);
    }

    private static void AddNode(
        XuiSyntaxNode parent,
        XuiSyntaxNode child,
        ref int nodeCount)
    {
        nodeCount++;
        if (nodeCount > MaximumNodeCount)
        {
            throw new XuiParseException(
                $"The XUI document exceeds the maximum node count of {MaximumNodeCount}.",
                child.Span);
        }

        parent.AddChild(child);
    }

    private static void AssignKeys(XuiSyntaxNode parent, string parentKey)
    {
        int elementIndex = 0;
        int triviaIndex = 0;
        foreach (XuiSyntaxNode child in parent.Children)
        {
            string suffix;
            if (child.Kind == XuiSyntaxKind.Element)
            {
                suffix = $"e{elementIndex++}";
            }
            else
            {
                suffix = $"t{triviaIndex++}";
            }

            child.Key = parentKey == "/" ? $"/{suffix}" : $"{parentKey}/{suffix}";
            AssignKeys(child, child.Key);
        }
    }

    private static XuiTextFormat DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            return new XuiTextFormat(
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
                HasByteOrderMark: true,
                DetectNewLine(Encoding.UTF8.GetString(bytes[Encoding.UTF8.Preamble.Length..])));
        }

        if (bytes.StartsWith(Encoding.Unicode.Preamble))
        {
            Encoding encoding = new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: true,
                throwOnInvalidBytes: true);
            return new XuiTextFormat(
                encoding,
                HasByteOrderMark: true,
                DetectNewLine(encoding.GetString(bytes[encoding.GetPreamble().Length..])));
        }

        if (bytes.StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            Encoding encoding = new UnicodeEncoding(
                bigEndian: true,
                byteOrderMark: true,
                throwOnInvalidBytes: true);
            return new XuiTextFormat(
                encoding,
                HasByteOrderMark: true,
                DetectNewLine(encoding.GetString(bytes[encoding.GetPreamble().Length..])));
        }

        Encoding utf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        return new XuiTextFormat(
            utf8,
            HasByteOrderMark: false,
            DetectNewLine(utf8.GetString(bytes)));
    }

    private static string DetectNewLine(string source)
    {
        int lineFeed = source.IndexOf('\n');
        if (lineFeed > 0 && source[lineFeed - 1] == '\r')
        {
            return "\r\n";
        }

        return lineFeed >= 0 ? "\n" : Environment.NewLine;
    }
}
