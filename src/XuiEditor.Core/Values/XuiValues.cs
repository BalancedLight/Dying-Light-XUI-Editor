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
    public static XuiVector3 Lerp(XuiVector3 left, XuiVector3 right, double amount) =>
        new(
            left.X + ((right.X - left.X) * amount),
            left.Y + ((right.Y - left.Y) * amount),
            left.Z + ((right.Z - left.Z) * amount));
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
