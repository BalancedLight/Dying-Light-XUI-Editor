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
        IReadOnlyList<XuiNamedFrame> namedFrames = timelineSet.NamedFrames
            .Where(frame => frame.ScopeKey == scopeKey)
            .ToArray();
        return AdvanceCore(
            namedFrames,
            scopeKey,
            ScopeMaximumTick(timelineSet, scopeKey),
            currentTick,
            playing,
            loop);
    }

    public static TimelinePlaybackState Advance(
        XuiTimelineScope scope,
        int currentTick,
        bool playing,
        bool loop)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return AdvanceCore(
            scope.NamedFrames,
            scope.ScopeKey,
            scope.MaximumTick,
            currentTick,
            playing,
            loop);
    }

    private static TimelinePlaybackState AdvanceCore(
        IReadOnlyList<XuiNamedFrame> namedFrames,
        string scopeKey,
        int maximumTick,
        int currentTick,
        bool playing,
        bool loop)
    {
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
            XuiNamedFrame? frame = namedFrames
                .Where(frame =>
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

                    XuiNamedFrame? target = namedFrames.LastOrDefault(
                        candidate =>
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

        if (tick > maximumTick)
        {
            tick = loop ? 0 : maximumTick;
            continuePlaying = loop;
        }

        return new TimelinePlaybackState(tick, continuePlaying, diagnostics);
    }

    private static int ScopeMaximumTick(
        XuiTimelineSet timelineSet,
        string scopeKey)
    {
        int keyMaximum = timelineSet.Timelines
            .Where(timeline => timeline.ScopeKey == scopeKey)
            .SelectMany(static timeline => timeline.Tracks)
            .SelectMany(static track => track.KeyFrames)
            .Select(static frame => frame.Tick)
            .DefaultIfEmpty()
            .Max();
        int frameMaximum = timelineSet.NamedFrames
            .Where(frame => frame.ScopeKey == scopeKey)
            .Select(static frame => frame.Tick)
            .DefaultIfEmpty()
            .Max();
        return Math.Max(keyMaximum, frameMaximum);
    }
}
