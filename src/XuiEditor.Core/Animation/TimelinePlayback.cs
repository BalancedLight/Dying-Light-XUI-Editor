using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Animation;

public sealed record TimelinePlaybackState(
    int Tick,
    bool IsPlaying,
    IReadOnlyList<XuiDiagnostic> Diagnostics);

public sealed class TimelinePlayback
{
    private const int MaximumCommandHops = 128;

    public static TimelinePlaybackState Advance(
        XuiTimelineSet timelineSet,
        string scopeKey,
        int currentTick,
        bool playing,
        bool loop)
    {
        ArgumentNullException.ThrowIfNull(timelineSet);
        if (!playing)
        {
            return new TimelinePlaybackState(currentTick, false, []);
        }

        List<XuiDiagnostic> diagnostics = [];
        int tick = checked(currentTick + 1);
        bool continuePlaying = true;
        HashSet<(string Scope, string Name)> visited = [];

        for (int hops = 0; hops < MaximumCommandHops; hops++)
        {
            XuiNamedFrame? frame = timelineSet.NamedFrames
                .Where(frame =>
                    frame.ScopeKey == scopeKey &&
                    frame.Tick == tick &&
                    frame.Command.Length > 0)
                .LastOrDefault();
            if (frame is null)
            {
                break;
            }

            switch (frame.Command)
            {
                case "stop":
                    continuePlaying = false;
                    return new TimelinePlaybackState(
                        tick,
                        continuePlaying,
                        diagnostics);

                case "goto":
                case "gotoandplay":
                case "gotoandstop":
                    if (!visited.Add((scopeKey, frame.Name)))
                    {
                        diagnostics.Add(new XuiDiagnostic(
                            "XUI-TL010",
                            XuiDiagnosticSeverity.Error,
                            $"Named-frame command cycle detected at '{frame.Name}'.",
                            frame.Syntax.Span,
                            frame.Syntax.Key));
                        return new TimelinePlaybackState(
                            tick,
                            false,
                            diagnostics);
                    }

                    XuiNamedFrame? target = timelineSet.NamedFrames.LastOrDefault(
                        candidate =>
                            candidate.ScopeKey == scopeKey &&
                            string.Equals(
                                candidate.Name,
                                frame.CommandParameter,
                                StringComparison.OrdinalIgnoreCase));
                    if (target is null)
                    {
                        diagnostics.Add(new XuiDiagnostic(
                            "XUI-TL011",
                            XuiDiagnosticSeverity.Error,
                            $"Named-frame target '{frame.CommandParameter}' was not found.",
                            frame.Syntax.Span,
                            frame.Syntax.Key));
                        return new TimelinePlaybackState(
                            tick,
                            false,
                            diagnostics);
                    }

                    tick = target.Tick;
                    continuePlaying = frame.Command != "gotoandstop";
                    if (!continuePlaying)
                    {
                        return new TimelinePlaybackState(
                            tick,
                            false,
                            diagnostics);
                    }

                    continue;
            }

            break;
        }

        if (tick > timelineSet.MaximumTick)
        {
            tick = loop ? 0 : timelineSet.MaximumTick;
            continuePlaying = loop;
        }

        return new TimelinePlaybackState(tick, continuePlaying, diagnostics);
    }
}
