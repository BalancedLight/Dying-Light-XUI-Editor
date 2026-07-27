namespace XuiEditor.Core.Diagnostics;

public enum XuiDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public readonly record struct SourceSpan(int Start, int Length)
{
    public int End => checked(Start + Length);

    public bool Contains(int offset) => offset >= Start && offset < End;

    public override string ToString() => $"{Start}..{End}";
}

public sealed record XuiDiagnostic(
    string Code,
    XuiDiagnosticSeverity Severity,
    string Message,
    SourceSpan? Span = null,
    string? NodeKey = null);
