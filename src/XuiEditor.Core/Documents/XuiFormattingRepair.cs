using System.Text;

namespace XuiEditor.Core.Documents;

public sealed record XuiFormattingRepairResult(
    string Text,
    int EscapedAmpersandCount);

public static class XuiFormattingRepair
{
    public static bool TryEscapeBareAmpersands(
        string source,
        out XuiFormattingRepairResult? repair)
    {
        ArgumentNullException.ThrowIfNull(source);

        StringBuilder? builder = null;
        int copiedFrom = 0;
        int escapedAmpersandCount = 0;

        void EscapeAt(int index)
        {
            builder ??= new StringBuilder(source.Length + 16);
            builder.Append(source, copiedFrom, index - copiedFrom);
            builder.Append("&amp;");
            copiedFrom = index + 1;
            escapedAmpersandCount++;
        }

        int cursor = 0;
        while (cursor < source.Length)
        {
            if (source[cursor] == '<')
            {
                if (source.AsSpan(cursor).StartsWith(
                        "<!--",
                        StringComparison.Ordinal))
                {
                    cursor = SkipMarkup(source, cursor + 4, "-->");
                    continue;
                }

                if (source.AsSpan(cursor).StartsWith(
                        "<![CDATA[",
                        StringComparison.Ordinal))
                {
                    cursor = SkipMarkup(source, cursor + 9, "]]>");
                    continue;
                }

                if (source.AsSpan(cursor).StartsWith(
                        "<?",
                        StringComparison.Ordinal))
                {
                    cursor = SkipMarkup(source, cursor + 2, "?>");
                    continue;
                }

                int tagEnd = FindTagEnd(source, cursor + 1);
                EscapeBareAmpersandsInAttributeValues(
                    source,
                    cursor,
                    tagEnd,
                    EscapeAt);
                cursor = tagEnd;
                continue;
            }

            int textEnd = source.IndexOf('<', cursor);
            if (textEnd < 0)
            {
                textEnd = source.Length;
            }

            for (int index = cursor; index < textEnd; index++)
            {
                if (source[index] == '&' && !IsXmlEntityAt(source, index))
                {
                    EscapeAt(index);
                }
            }

            cursor = textEnd;
        }

        if (builder is null)
        {
            repair = null;
            return false;
        }

        builder.Append(source, copiedFrom, source.Length - copiedFrom);
        repair = new XuiFormattingRepairResult(
            builder.ToString(),
            escapedAmpersandCount);
        return true;
    }

    private static void EscapeBareAmpersandsInAttributeValues(
        string source,
        int tagStart,
        int tagEnd,
        Action<int> escapeAt)
    {
        char quote = '\0';
        for (int index = tagStart + 1; index < tagEnd; index++)
        {
            char character = source[index];
            if (quote == '\0')
            {
                if (character is '\'' or '"')
                {
                    quote = character;
                }

                continue;
            }

            if (character == quote)
            {
                quote = '\0';
            }
            else if (character == '&' && !IsXmlEntityAt(source, index))
            {
                escapeAt(index);
            }
        }
    }

    private static int FindTagEnd(string source, int start)
    {
        char quote = '\0';
        for (int index = start; index < source.Length; index++)
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

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '>')
            {
                return index + 1;
            }
        }

        return source.Length;
    }

    private static bool IsXmlEntityAt(string source, int ampersandIndex)
    {
        ReadOnlySpan<char> remaining = source.AsSpan(ampersandIndex);
        if (remaining.StartsWith("&amp;", StringComparison.Ordinal) ||
            remaining.StartsWith("&lt;", StringComparison.Ordinal) ||
            remaining.StartsWith("&gt;", StringComparison.Ordinal) ||
            remaining.StartsWith("&quot;", StringComparison.Ordinal) ||
            remaining.StartsWith("&apos;", StringComparison.Ordinal))
        {
            return true;
        }

        if (remaining.Length < 4 || remaining[1] != '#')
        {
            return false;
        }

        int cursor = 2;
        if (remaining[cursor] == 'x')
        {
            cursor++;
            int digitsStart = cursor;
            while (cursor < remaining.Length &&
                   Uri.IsHexDigit(remaining[cursor]))
            {
                cursor++;
            }

            return cursor > digitsStart &&
                   cursor < remaining.Length &&
                   remaining[cursor] == ';';
        }

        int decimalStart = cursor;
        while (cursor < remaining.Length &&
               char.IsAsciiDigit(remaining[cursor]))
        {
            cursor++;
        }

        return cursor > decimalStart &&
               cursor < remaining.Length &&
               remaining[cursor] == ';';
    }

    private static int SkipMarkup(
        string source,
        int contentStart,
        string terminator)
    {
        int end = source.IndexOf(
            terminator,
            contentStart,
            StringComparison.Ordinal);
        return end < 0 ? source.Length : end + terminator.Length;
    }
}
