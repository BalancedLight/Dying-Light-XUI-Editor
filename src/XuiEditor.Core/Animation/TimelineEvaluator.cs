namespace XuiEditor.Core.Animation;

public sealed class TimelineEvaluator
{
    public const int TicksPerSecond = 60;

    public static XuiAnimatedValue? Sample(XuiTrack track, int tick)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.KeyFrames.Count == 0)
        {
            return null;
        }

        XuiKeyFrame first = track.KeyFrames[0];
        if (tick <= first.Tick)
        {
            return ValueAt(first, track.PropertyIndex);
        }

        XuiKeyFrame last = track.KeyFrames[^1];
        if (tick >= last.Tick)
        {
            return ValueAt(last, track.PropertyIndex);
        }

        for (int index = 0; index < track.KeyFrames.Count - 1; index++)
        {
            XuiKeyFrame left = track.KeyFrames[index];
            XuiKeyFrame right = track.KeyFrames[index + 1];
            if (tick < left.Tick || tick > right.Tick)
            {
                continue;
            }

            XuiAnimatedValue? leftValue = ValueAt(left, track.PropertyIndex);
            XuiAnimatedValue? rightValue = ValueAt(right, track.PropertyIndex);
            if (leftValue is null || rightValue is null)
            {
                return leftValue ?? rightValue;
            }

            if (!CanInterpolate(leftValue, rightValue) ||
                left.Interpolation == XuiInterpolation.Unknown ||
                right.Tick == left.Tick)
            {
                return leftValue;
            }

            double amount = (double)(tick - left.Tick) / (right.Tick - left.Tick);
            if (left.Interpolation == XuiInterpolation.Eased)
            {
                amount = ApplyEase(
                    amount,
                    left.EaseIn,
                    left.EaseOut,
                    left.EaseScale);
            }

            return Interpolate(leftValue, rightValue, amount);
        }

        return last.Values.ElementAtOrDefault(track.PropertyIndex);
    }

    private static XuiAnimatedValue? ValueAt(
        XuiKeyFrame frame,
        int propertyIndex) =>
        frame.Values.ElementAtOrDefault(propertyIndex);

    private static bool CanInterpolate(
        XuiAnimatedValue left,
        XuiAnimatedValue right) =>
        left.Kind == right.Kind &&
        left.Kind is
            XuiTimelineValueKind.Number or
            XuiTimelineValueKind.Vector2 or
            XuiTimelineValueKind.Vector3 or
            XuiTimelineValueKind.Quaternion or
            XuiTimelineValueKind.Color;

    private static XuiAnimatedValue Interpolate(
        XuiAnimatedValue left,
        XuiAnimatedValue right,
        double amount) =>
        left.Kind switch
        {
            XuiTimelineValueKind.Number => left with
            {
                Number = left.Number + ((right.Number - left.Number) * amount),
            },
            XuiTimelineValueKind.Vector2 => left with
            {
                Vector2 = Values.XuiVector2.Lerp(
                    left.Vector2,
                    right.Vector2,
                    amount),
            },
            XuiTimelineValueKind.Vector3 => left with
            {
                Vector3 = Values.XuiVector3.Lerp(
                    left.Vector3,
                    right.Vector3,
                    amount),
            },
            XuiTimelineValueKind.Quaternion => left with
            {
                Quaternion = Values.XuiQuaternion.Slerp(
                    left.Quaternion,
                    right.Quaternion,
                    amount),
            },
            XuiTimelineValueKind.Color => left with
            {
                Color = Values.XuiColor.Lerp(
                    left.Color,
                    right.Color,
                    amount),
            },
            _ => left,
        };

    private static double ApplyEase(
        double amount,
        double easeIn,
        double easeOut,
        double easeScale)
    {
        double clamped = Math.Clamp(amount, 0, 1);
        double smooth = clamped * clamped * (3 - (2 * clamped));
        double influence = Math.Clamp(
            easeScale == 0 ? 1 : Math.Abs(easeScale),
            0,
            1);

        if (easeIn > 0 || easeOut > 0)
        {
            double inPower = 1 + Math.Clamp(easeIn, 0, 8);
            double outPower = 1 + Math.Clamp(easeOut, 0, 8);
            double easedIn = Math.Pow(clamped, inPower);
            double easedOut = 1 - Math.Pow(1 - clamped, outPower);
            double total = Math.Max(easeIn + easeOut, double.Epsilon);
            smooth = ((easedIn * easeIn) + (easedOut * easeOut)) / total;
        }

        return clamped + ((smooth - clamped) * influence);
    }
}
