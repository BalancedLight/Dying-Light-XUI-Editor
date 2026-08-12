using System.Globalization;

namespace XuiEditor.Core.Values;

public readonly record struct XuiVector2(double X, double Y)
{
    public static XuiVector2 Lerp(XuiVector2 left, XuiVector2 right, double amount) =>
        new(
            left.X + ((right.X - left.X) * amount),
            left.Y + ((right.Y - left.Y) * amount));
}

public readonly record struct XuiVector3(double X, double Y, double Z)
{
    public static XuiVector3 operator +(
        XuiVector3 left,
        XuiVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static XuiVector3 operator -(
        XuiVector3 left,
        XuiVector3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static XuiVector3 Lerp(XuiVector3 left, XuiVector3 right, double amount) =>
        new(
            left.X + ((right.X - left.X) * amount),
            left.Y + ((right.Y - left.Y) * amount),
            left.Z + ((right.Z - left.Z) * amount));
}

public readonly record struct XuiVector4(double X, double Y, double Z, double W)
{
    public static XuiVector4 Lerp(XuiVector4 left, XuiVector4 right, double amount) =>
        new(
            left.X + ((right.X - left.X) * amount),
            left.Y + ((right.Y - left.Y) * amount),
            left.Z + ((right.Z - left.Z) * amount),
            left.W + ((right.W - left.W) * amount));
}

public readonly record struct XuiQuaternion(double X, double Y, double Z, double W)
{
    public static readonly XuiQuaternion Identity = new(0, 0, 0, 1);

    public double ZRotationDegrees
    {
        get
        {
            double sine = 2 * ((W * Z) + (X * Y));
            double cosine = 1 - (2 * ((Y * Y) + (Z * Z)));
            return Math.Atan2(sine, cosine) * 180 / Math.PI;
        }
    }

    public static XuiQuaternion Slerp(
        XuiQuaternion left,
        XuiQuaternion right,
        double amount)
    {
        System.Numerics.Quaternion result = System.Numerics.Quaternion.Slerp(
            new System.Numerics.Quaternion(
                (float)left.X,
                (float)left.Y,
                (float)left.Z,
                (float)left.W),
            new System.Numerics.Quaternion(
                (float)right.X,
                (float)right.Y,
                (float)right.Z,
                (float)right.W),
            (float)amount);
        return new XuiQuaternion(result.X, result.Y, result.Z, result.W);
    }
}

public enum XuiRotationSourceKind
{
    Quaternion,
    EulerDegrees,
    ScalarDegrees,
}

/// <summary>
/// Converts editor-friendly rotation values to the four-component quaternion
/// representation consumed by Dying Light XUI files.
/// </summary>
public static class XuiRotationCodec
{
    private const double MinimumLengthSquared = 1e-12;
    private const double UnitTolerance = 1e-3;
    private const double ZeroFormattingThreshold = 0.0000005;

    public static bool TryDecode(
        string? text,
        out XuiQuaternion quaternion,
        out XuiRotationSourceKind sourceKind)
    {
        if (XuiValueParser.TryQuaternion(text, out XuiQuaternion authored))
        {
            sourceKind = XuiRotationSourceKind.Quaternion;
            return TryNormalize(authored, out quaternion);
        }

        if (XuiValueParser.TryVector3(text, out XuiVector3 eulerDegrees))
        {
            sourceKind = XuiRotationSourceKind.EulerDegrees;
            quaternion = FromEulerDegrees(eulerDegrees);
            return true;
        }

        if (XuiValueParser.TryNumber(text, out double scalarDegrees))
        {
            sourceKind = XuiRotationSourceKind.ScalarDegrees;
            quaternion = FromZDegrees(scalarDegrees);
            return true;
        }

        quaternion = XuiQuaternion.Identity;
        sourceKind = default;
        return false;
    }

    public static bool TryCanonicalize(
        string? text,
        out string canonical,
        out XuiRotationSourceKind sourceKind)
    {
        if (!TryDecode(text, out XuiQuaternion quaternion, out sourceKind))
        {
            canonical = string.Empty;
            return false;
        }

        canonical = Format(quaternion);
        return true;
    }

    public static bool TryNormalizeForSave(
        string? text,
        out string canonical,
        out XuiRotationSourceKind sourceKind)
    {
        if (XuiValueParser.TryQuaternion(text, out XuiQuaternion authored))
        {
            sourceKind = XuiRotationSourceKind.Quaternion;
            if (!TryNormalize(authored, out XuiQuaternion normalized))
            {
                canonical = string.Empty;
                return false;
            }

            canonical = IsUnitQuaternion(authored)
                ? text!.Trim()
                : Format(normalized);
            return true;
        }

        return TryCanonicalize(text, out canonical, out sourceKind);
    }

    public static XuiQuaternion FromZDegrees(double degrees)
    {
        double radians = degrees * Math.PI / 180;
        double half = radians / 2;
        return CanonicalSign(new XuiQuaternion(
            0,
            0,
            Math.Sin(half),
            Math.Cos(half)));
    }

    /// <summary>
    /// Matches Unity Quaternion.Euler: authored Z, X, then Y rotations.
    /// </summary>
    public static XuiQuaternion FromEulerDegrees(XuiVector3 degrees)
    {
        XuiQuaternion x = AxisAngle(1, 0, 0, degrees.X);
        XuiQuaternion y = AxisAngle(0, 1, 0, degrees.Y);
        XuiQuaternion z = AxisAngle(0, 0, 1, degrees.Z);
        return CanonicalSign(Multiply(y, Multiply(x, z)));
    }

    public static bool TryAddZDegrees(
        string? text,
        double deltaDegrees,
        out string canonical)
    {
        XuiQuaternion current;
        if (string.IsNullOrWhiteSpace(text))
        {
            current = XuiQuaternion.Identity;
        }
        else if (!TryDecode(text, out current, out _))
        {
            canonical = string.Empty;
            return false;
        }

        XuiQuaternion delta = FromZDegrees(deltaDegrees);
        canonical = Format(Multiply(delta, current));
        return true;
    }

    public static string Format(XuiQuaternion quaternion)
    {
        if (!TryNormalize(quaternion, out XuiQuaternion normalized))
        {
            throw new ArgumentException(
                "A Dying Light XUI quaternion must be finite and non-zero.",
                nameof(quaternion));
        }

        normalized = CanonicalSign(normalized);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.000000},{1:0.000000},{2:0.000000},{3:0.000000}",
            Component(normalized.X),
            Component(normalized.Y),
            Component(normalized.Z),
            Component(normalized.W));
    }

    private static XuiQuaternion AxisAngle(
        double x,
        double y,
        double z,
        double degrees)
    {
        double half = degrees * Math.PI / 360;
        double sine = Math.Sin(half);
        return new XuiQuaternion(
            x * sine,
            y * sine,
            z * sine,
            Math.Cos(half));
    }

    private static XuiQuaternion Multiply(
        XuiQuaternion left,
        XuiQuaternion right) =>
        new(
            (left.W * right.X) + (left.X * right.W) +
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.W * right.Y) - (left.X * right.Z) +
            (left.Y * right.W) + (left.Z * right.X),
            (left.W * right.Z) + (left.X * right.Y) -
            (left.Y * right.X) + (left.Z * right.W),
            (left.W * right.W) - (left.X * right.X) -
            (left.Y * right.Y) - (left.Z * right.Z));

    private static bool TryNormalize(
        XuiQuaternion value,
        out XuiQuaternion normalized)
    {
        double lengthSquared =
            (value.X * value.X) +
            (value.Y * value.Y) +
            (value.Z * value.Z) +
            (value.W * value.W);
        if (!double.IsFinite(lengthSquared) ||
            lengthSquared < MinimumLengthSquared)
        {
            normalized = XuiQuaternion.Identity;
            return false;
        }

        double inverseLength = 1 / Math.Sqrt(lengthSquared);
        normalized = new XuiQuaternion(
            value.X * inverseLength,
            value.Y * inverseLength,
            value.Z * inverseLength,
            value.W * inverseLength);
        return true;
    }

    private static bool IsUnitQuaternion(XuiQuaternion value)
    {
        double lengthSquared =
            (value.X * value.X) +
            (value.Y * value.Y) +
            (value.Z * value.Z) +
            (value.W * value.W);
        return double.IsFinite(lengthSquared) &&
               Math.Abs(lengthSquared - 1) <= UnitTolerance;
    }

    private static XuiQuaternion CanonicalSign(XuiQuaternion value)
    {
        if (!TryNormalize(value, out XuiQuaternion normalized))
        {
            return XuiQuaternion.Identity;
        }

        return normalized.W < 0
            ? new XuiQuaternion(
                -normalized.X,
                -normalized.Y,
                -normalized.Z,
                -normalized.W)
            : normalized;
    }

    private static double Component(double value) =>
        Math.Abs(value) < ZeroFormattingThreshold ? 0 : value;
}

public readonly record struct XuiColor(byte A, byte R, byte G, byte B)
{
    public static readonly XuiColor White = new(255, 255, 255, 255);

    public static readonly XuiColor Transparent = new(0, 0, 0, 0);

    public uint Argb =>
        ((uint)A << 24) |
        ((uint)R << 16) |
        ((uint)G << 8) |
        B;

    public static XuiColor Lerp(XuiColor left, XuiColor right, double amount)
    {
        static byte Interpolate(byte start, byte end, double amount) =>
            (byte)Math.Clamp(
                Math.Round(start + ((end - start) * amount)),
                byte.MinValue,
                byte.MaxValue);

        return new XuiColor(
            Interpolate(left.A, right.A, amount),
            Interpolate(left.R, right.R, amount),
            Interpolate(left.G, right.G, amount),
            Interpolate(left.B, right.B, amount));
    }
}

public readonly record struct XuiRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(XuiVector2 point) =>
        point.X >= X &&
        point.X <= Right &&
        point.Y >= Y &&
        point.Y <= Bottom;

    public static XuiRect FromPoints(ReadOnlySpan<XuiVector2> points)
    {
        if (points.IsEmpty)
        {
            return default;
        }

        double minimumX = points[0].X;
        double maximumX = points[0].X;
        double minimumY = points[0].Y;
        double maximumY = points[0].Y;
        for (int index = 1; index < points.Length; index++)
        {
            XuiVector2 point = points[index];
            minimumX = Math.Min(minimumX, point.X);
            maximumX = Math.Max(maximumX, point.X);
            minimumY = Math.Min(minimumY, point.Y);
            maximumY = Math.Max(maximumY, point.Y);
        }

        return new XuiRect(
            minimumX,
            minimumY,
            maximumX - minimumX,
            maximumY - minimumY);
    }
}

public static class XuiValueParser
{
    public static bool TryNumber(string? text, out double value) =>
        double.TryParse(
            text?.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) &&
        double.IsFinite(value);

    public static bool TryInteger(string? text, out int value) =>
        int.TryParse(
            text?.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);

    public static bool TryBoolean(string? text, out bool value)
    {
        string normalized = text?.Trim() ?? string.Empty;
        if (bool.TryParse(normalized, out value))
        {
            return true;
        }

        if (normalized == "1")
        {
            value = true;
            return true;
        }

        if (normalized == "0")
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    public static bool TryVector2(string? text, out XuiVector2 value)
    {
        if (TryComponents(text, 2, out double[] components))
        {
            value = new XuiVector2(components[0], components[1]);
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryVector3(string? text, out XuiVector3 value)
    {
        if (TryComponents(text, 3, out double[] components))
        {
            value = new XuiVector3(components[0], components[1], components[2]);
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryVector4(string? text, out XuiVector4 value)
    {
        if (TryComponents(text, 4, out double[] components))
        {
            value = new XuiVector4(
                components[0],
                components[1],
                components[2],
                components[3]);
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryQuaternion(string? text, out XuiQuaternion value)
    {
        if (TryComponents(text, 4, out double[] components))
        {
            value = new XuiQuaternion(
                components[0],
                components[1],
                components[2],
                components[3]);
            return true;
        }

        value = XuiQuaternion.Identity;
        return false;
    }

    public static bool TryColor(string? text, out XuiColor value)
    {
        string normalized = text?.Trim() ?? string.Empty;
        bool hexadecimalPrefix = normalized.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase);
        if (hexadecimalPrefix)
        {
            normalized = normalized[2..];
        }
        else if (normalized.StartsWith('#'))
        {
            normalized = normalized[1..];
        }

        if (normalized.Length == 6 &&
            uint.TryParse(
                normalized,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out uint rgb))
        {
            value = new XuiColor(
                255,
                (byte)(rgb >> 16),
                (byte)(rgb >> 8),
                (byte)rgb);
            return true;
        }

        if (hexadecimalPrefix && normalized.Length is > 0 and < 8)
        {
            normalized = normalized.PadLeft(8, '0');
        }

        if (normalized.Length == 8 &&
            uint.TryParse(
                normalized,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out uint argb))
        {
            value = new XuiColor(
                (byte)(argb >> 24),
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb);
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryComponents(
        string? text,
        int requiredCount,
        out double[] components)
    {
        string[] parts = (text ?? string.Empty).Split(
            ',',
            StringSplitOptions.TrimEntries);
        if (parts.Length != requiredCount)
        {
            components = [];
            return false;
        }

        components = new double[requiredCount];
        for (int index = 0; index < parts.Length; index++)
        {
            if (!TryNumber(parts[index], out components[index]))
            {
                components = [];
                return false;
            }
        }

        return true;
    }
}
