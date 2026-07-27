using System.Buffers.Binary;
using System.Text;

namespace XuiEditor.Core.Assets;

public static class DyingLightDdsBuilder
{
    private const uint DdsMagic = 0x20534444;
    private const uint DdsHeaderSize = 124;
    private const uint DdsPixelFormatSize = 32;
    private const uint DdsdCaps = 0x1;
    private const uint DdsdHeight = 0x2;
    private const uint DdsdWidth = 0x4;
    private const uint DdsdPitch = 0x8;
    private const uint DdsdPixelFormat = 0x1000;
    private const uint DdsdMipmapCount = 0x20000;
    private const uint DdsdLinearSize = 0x80000;
    private const uint DdpfFourCc = 0x4;
    private const uint DdsCapsComplex = 0x8;
    private const uint DdsCapsTexture = 0x1000;
    private const uint DdsCapsMipmap = 0x400000;

    public static byte[] Build(ReadOnlySpan<byte> resource)
    {
        if (resource.Length < 151)
        {
            throw new InvalidDataException(
                "Dying Light texture resource metadata is truncated.");
        }

        int width = BinaryPrimitives.ReadUInt16LittleEndian(resource);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(resource[2..]);
        int mipCount = Math.Max(
            1,
            (int)BinaryPrimitives.ReadUInt16LittleEndian(resource[8..]));
        int format = BinaryPrimitives.ReadInt32LittleEndian(resource[12..]);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException(
                "Dying Light texture resource has invalid dimensions.");
        }

        ReadOnlySpan<byte> pixels = resource[151..];
        FormatInfo info = Format(format, width, height);
        if (pixels.Length < info.TopLevelSize)
        {
            throw new InvalidDataException(
                $"Dying Light texture payload is too small for {width}×{height} format {format}.");
        }

        int headerSize = info.DxgiFormat is null ? 128 : 148;
        byte[] result = GC.AllocateUninitializedArray<byte>(
            checked(headerSize + pixels.Length));
        Span<byte> header = result.AsSpan(0, headerSize);
        header.Clear();
        Write(header, 0, DdsMagic);
        Write(header, 4, DdsHeaderSize);
        uint flags =
            DdsdCaps |
            DdsdHeight |
            DdsdWidth |
            DdsdPixelFormat |
            (info.IsBlockCompressed ? DdsdLinearSize : DdsdPitch);
        if (mipCount > 1)
        {
            flags |= DdsdMipmapCount;
        }

        Write(header, 8, flags);
        Write(header, 12, (uint)height);
        Write(header, 16, (uint)width);
        Write(
            header,
            20,
            (uint)(info.IsBlockCompressed
                ? info.TopLevelSize
                : info.RowPitch));
        Write(header, 28, (uint)mipCount);
        Write(header, 76, DdsPixelFormatSize);
        Write(header, 80, DdpfFourCc);
        Write(
            header,
            84,
            info.DxgiFormat is null
                ? FourCc(info.FourCc!)
                : FourCc("DX10"));
        uint caps = DdsCapsTexture;
        if (mipCount > 1)
        {
            caps |= DdsCapsComplex | DdsCapsMipmap;
        }

        Write(header, 108, caps);
        if (info.DxgiFormat is int dxgi)
        {
            Write(header, 128, (uint)dxgi);
            Write(header, 132, 3);
            Write(header, 140, 1);
        }

        pixels.CopyTo(result.AsSpan(headerSize));
        return result;
    }

    private static FormatInfo Format(int format, int width, int height) =>
        format switch
        {
            2 or 3 => new FormatInfo(
                DxgiFormat: 28,
                FourCc: null,
                IsBlockCompressed: false,
                TopLevelSize: checked(width * height * 4),
                RowPitch: checked(width * 4)),
            14 => new FormatInfo(
                DxgiFormat: 61,
                FourCc: null,
                IsBlockCompressed: false,
                TopLevelSize: checked(width * height),
                RowPitch: width),
            17 => Block("DXT1", width, height, 8),
            18 => Block("DXT3", width, height, 16),
            19 or 33 => Block("DXT5", width, height, 16),
            _ => throw new NotSupportedException(
                $"Dying Light texture format {format} is not supported."),
        };

    private static FormatInfo Block(
        string fourCc,
        int width,
        int height,
        int blockBytes)
    {
        int blocksWide = Math.Max(1, (width + 3) / 4);
        int blocksHigh = Math.Max(1, (height + 3) / 4);
        return new FormatInfo(
            DxgiFormat: null,
            fourCc,
            IsBlockCompressed: true,
            TopLevelSize: checked(blocksWide * blocksHigh * blockBytes),
            RowPitch: checked(blocksWide * blockBytes));
    }

    private static uint FourCc(string text)
    {
        if (Encoding.ASCII.GetByteCount(text) != 4)
        {
            throw new ArgumentException(
                "A DDS FourCC must contain exactly four ASCII bytes.",
                nameof(text));
        }

        Span<byte> bytes = stackalloc byte[4];
        _ = Encoding.ASCII.GetBytes(text, bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static void Write(Span<byte> destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], value);

    private sealed record FormatInfo(
        int? DxgiFormat,
        string? FourCc,
        bool IsBlockCompressed,
        int TopLevelSize,
        int RowPitch);
}
