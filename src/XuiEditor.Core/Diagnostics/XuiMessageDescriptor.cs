using System.Globalization;

namespace XuiEditor.Core.Diagnostics;

/// <summary>
/// Describes a user-facing message without tying Core to a UI language.
/// </summary>
public sealed record XuiMessageDescriptor
{
    public XuiMessageDescriptor(
        string key,
        string englishFallback,
        params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(englishFallback);
        Key = key;
        EnglishFallback = englishFallback;
        Arguments = arguments?.ToArray() ?? [];
    }

    public string Key { get; }

    public string EnglishFallback { get; }

    public IReadOnlyList<object?> Arguments { get; }

    public string Format(
        IFormatProvider? provider = null,
        string? template = null) =>
        string.Format(
            provider ?? CultureInfo.InvariantCulture,
            template ?? EnglishFallback,
            Arguments.ToArray());
}
