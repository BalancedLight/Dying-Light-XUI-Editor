using System.Globalization;
using System.Text;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Layout;

public readonly record struct XuiTextColorRun(
    int Start,
    int Length,
    XuiColor Color)
{
    public int End => checked(Start + Length);
}

public sealed record XuiColorControlParseResult(
    string DisplayText,
    IReadOnlyList<XuiTextColorRun> ColorRuns,
    int ValidSequenceCount,
    int MalformedSequenceCount)
{
    public bool HasValidSequences => ValidSequenceCount > 0;

    public bool HasMalformedSequences => MalformedSequenceCount > 0;

    public bool HasMarkup => HasValidSequences || HasMalformedSequences;
}

public sealed record XuiTextPresentation(
    string Text,
    IReadOnlyList<XuiTextColorRun> ColorRuns);

public static class XuiColorControlSequenceParser
{
    private const string Prefix = "%COLOR(";

    public static XuiColorControlParseResult Parse(
        string text,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return new XuiColorControlParseResult(text, [], 0, 0);
        }

        if (text.IndexOf(
                Prefix,
                StringComparison.OrdinalIgnoreCase) < 0)
        {
            return new XuiColorControlParseResult(text, [], 0, 0);
        }

        StringBuilder display = new(text.Length);
        List<XuiTextColorRun> runs = [];
        XuiColor? activeColor = null;
        int activeRunStart = 0;
        int validCount = 0;
        int malformedCount = 0;
        int index = 0;
        while (index < text.Length)
        {
            int candidate = text.IndexOf('%', index);
            if (candidate < 0)
            {
                display.Append(text, index, text.Length - index);
                break;
            }

            display.Append(text, index, candidate - index);
            if (!StartsWithPrefix(text, candidate, StringComparison.OrdinalIgnoreCase))
            {
                display.Append('%');
                index = candidate + 1;
                continue;
            }

            int payloadStart = candidate + Prefix.Length;
            int close = text.IndexOf(')', payloadStart);
            if (close < 0)
            {
                malformedCount++;
                display.Append(text, candidate, text.Length - candidate);
                index = text.Length;
                break;
            }

            ReadOnlySpan<char> payload =
                text.AsSpan(payloadStart, close - payloadStart);
            bool exactPrefix = StartsWithPrefix(
                text,
                candidate,
                StringComparison.Ordinal);
            bool reset = payload.Equals(
                "reset",
                StringComparison.OrdinalIgnoreCase);
            bool rgb = payload.Length == 6 && IsHex(payload);
            if (!exactPrefix || (!reset && !rgb))
            {
                malformedCount++;
                display.Append(text, candidate, close - candidate + 1);
                index = close + 1;
                continue;
            }

            validCount++;
            if (!enabled)
            {
                display.Append(text, candidate, close - candidate + 1);
                index = close + 1;
                continue;
            }

            FinishRun(runs, activeColor, activeRunStart, display.Length);
            activeColor = reset
                ? null
                : new XuiColor(
                    byte.MaxValue,
                    ParseByte(payload[..2]),
                    ParseByte(payload.Slice(2, 2)),
                    ParseByte(payload.Slice(4, 2)));
            activeRunStart = display.Length;
            index = close + 1;
        }

        FinishRun(runs, activeColor, activeRunStart, display.Length);
        return new XuiColorControlParseResult(
            display.ToString(),
            runs.ToArray(),
            validCount,
            malformedCount);
    }

    private static bool StartsWithPrefix(
        string text,
        int start,
        StringComparison comparison) =>
        start <= text.Length - Prefix.Length &&
        text.AsSpan(start, Prefix.Length).Equals(
            Prefix,
            comparison);

    private static bool IsHex(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (!IsHex(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHex(char value) =>
        value is >= '0' and <= '9' or
            >= 'a' and <= 'f' or
            >= 'A' and <= 'F';

    private static byte ParseByte(ReadOnlySpan<char> value)
    {
        int high = HexValue(value[0]);
        int low = HexValue(value[1]);
        return (byte)((high << 4) | low);
    }

    private static int HexValue(char value) =>
        value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' => value - 'a' + 10,
            _ => value - 'A' + 10,
        };

    private static void FinishRun(
        List<XuiTextColorRun> runs,
        XuiColor? color,
        int start,
        int end)
    {
        if (color is not XuiColor runColor || end <= start)
        {
            return;
        }

        if (runs.Count > 0 &&
            runs[^1] is XuiTextColorRun previous &&
            previous.End == start &&
            previous.Color == runColor)
        {
            runs[^1] = previous with
            {
                Length = end - previous.Start,
            };
            return;
        }

        runs.Add(new XuiTextColorRun(start, end - start, runColor));
    }
}

public static class XuiTextColorRunFormatter
{
    public static XuiTextPresentation Prepare(
        string text,
        IReadOnlyList<XuiTextColorRun> colorRuns,
        bool uppercase,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(colorRuns);
        ArgumentNullException.ThrowIfNull(culture);
        if (!uppercase)
        {
            return new XuiTextPresentation(text, colorRuns);
        }

        if (colorRuns.Count == 0)
        {
            return new XuiTextPresentation(
                text.ToUpper(culture),
                colorRuns);
        }

        StringBuilder transformed = new(text.Length);
        List<XuiTextColorRun> transformedRuns = [];
        int cursor = 0;
        foreach (XuiTextColorRun run in colorRuns)
        {
            int start = Math.Clamp(run.Start, cursor, text.Length);
            int end = Math.Clamp(run.End, start, text.Length);
            AppendUppercase(text, cursor, start, culture, transformed);
            int transformedStart = transformed.Length;
            AppendUppercase(text, start, end, culture, transformed);
            int transformedLength = transformed.Length - transformedStart;
            if (transformedLength > 0)
            {
                transformedRuns.Add(new XuiTextColorRun(
                    transformedStart,
                    transformedLength,
                    run.Color));
            }

            cursor = end;
        }

        AppendUppercase(text, cursor, text.Length, culture, transformed);
        return new XuiTextPresentation(
            transformed.ToString(),
            transformedRuns.ToArray());
    }

    private static void AppendUppercase(
        string text,
        int start,
        int end,
        CultureInfo culture,
        StringBuilder destination)
    {
        if (end <= start)
        {
            return;
        }

        destination.Append(
            text.Substring(start, end - start).ToUpper(culture));
    }
}
