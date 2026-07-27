using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace XuiEditor.Core.Assets;

public sealed record Rp6ResourceDescriptor(
    int Index,
    string Name,
    short ResourceType,
    int FirstItemIndex,
    int ItemCount)
{
    public byte PayloadType => unchecked((byte)ResourceType);
}

public interface IRp6Reader
{
    string Path { get; }

    IReadOnlyList<Rp6ResourceDescriptor> Resources { get; }

    ValueTask<byte[]> ReadResourceAsync(
        Rp6ResourceDescriptor resource,
        CancellationToken cancellationToken = default);
}

public sealed class Rp6Reader : IRp6Reader
{
    private const int HeaderSize = 36;
    private const int ChunkSize = 20;
    private const int ItemSize = 16;
    private const int ResourceSize = 12;
    private const int MaximumTableCount = 2_000_000;
    private const int MaximumNameBlobSize = 64 * 1024 * 1024;
    private const int MaximumLogicalChunkSize = 1024 * 1024 * 1024;
    private readonly IReadOnlyList<Rp6ChunkDescriptor> _chunks;
    private readonly IReadOnlyList<Rp6ItemDescriptor> _items;

    private Rp6Reader(
        string path,
        IReadOnlyList<Rp6ChunkDescriptor> chunks,
        IReadOnlyList<Rp6ItemDescriptor> items,
        IReadOnlyList<Rp6ResourceDescriptor> resources)
    {
        Path = path;
        _chunks = chunks;
        _items = items;
        Resources = resources;
    }

    public string Path { get; }

    public IReadOnlyList<Rp6ResourceDescriptor> Resources { get; }

    public static Rp6Reader Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = System.IO.Path.GetFullPath(path);
        using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.RandomAccess);
        Span<byte> header = stackalloc byte[HeaderSize];
        ReadExactly(stream, header);
        if (!header[..4].SequenceEqual("RP6L"u8))
        {
            throw new InvalidDataException(
                $"'{fullPath}' is not an RP6L resource pack.");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (version != 1)
        {
            throw new InvalidDataException(
                $"RP6L version {version} is not supported.");
        }

        int itemCount = ReadBoundedCount(header[12..], "item");
        int chunkCount = ReadBoundedCount(header[16..], "chunk");
        int resourceCount = ReadBoundedCount(header[20..], "resource");
        int nameBlobSize = BinaryPrimitives.ReadInt32LittleEndian(header[24..]);
        int nameCount = ReadBoundedCount(header[28..], "name");
        if (nameBlobSize < 0 || nameBlobSize > MaximumNameBlobSize)
        {
            throw new InvalidDataException(
                $"RP6L name blob size {nameBlobSize} is unsafe.");
        }

        long tableSize = checked(
            (long)chunkCount * ChunkSize +
            (long)itemCount * ItemSize +
            (long)resourceCount * ResourceSize +
            (long)nameCount * sizeof(int) +
            nameBlobSize);
        if (HeaderSize + tableSize > stream.Length)
        {
            throw new InvalidDataException(
                "RP6L tables extend beyond the end of the file.");
        }

        byte[] table = GC.AllocateUninitializedArray<byte>((int)tableSize);
        ReadExactly(stream, table);
        ReadOnlySpan<byte> data = table;
        int cursor = 0;
        List<Rp6ChunkDescriptor> chunks = new(chunkCount);
        for (int index = 0; index < chunkCount; index++)
        {
            ReadOnlySpan<byte> row = data.Slice(cursor, ChunkSize);
            cursor += ChunkSize;
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(row[4..]);
            uint logicalSize = BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);
            int packedSize = BinaryPrimitives.ReadInt32LittleEndian(row[12..]);
            if (logicalSize > MaximumLogicalChunkSize ||
                packedSize < 0 ||
                packedSize > MaximumLogicalChunkSize)
            {
                throw new InvalidDataException(
                    $"RP6L chunk {index} has an unsafe size.");
            }

            long storedSize = packedSize > 0 ? packedSize : logicalSize;
            if (offset > stream.Length ||
                checked((long)offset + storedSize) > stream.Length)
            {
                throw new InvalidDataException(
                    $"RP6L chunk {index} extends beyond the end of the file.");
            }

            chunks.Add(new Rp6ChunkDescriptor(
                offset,
                (int)logicalSize,
                packedSize));
        }

        List<Rp6ItemDescriptor> items = new(itemCount);
        for (int index = 0; index < itemCount; index++)
        {
            ReadOnlySpan<byte> row = data.Slice(cursor, ItemSize);
            cursor += ItemSize;
            int chunkIndex = row[0];
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(row[4..]);
            int size = BinaryPrimitives.ReadInt32LittleEndian(row[8..]);
            if (chunkIndex >= chunks.Count || size < 0)
            {
                throw new InvalidDataException(
                    $"RP6L item {index} has an invalid chunk or size.");
            }

            Rp6ChunkDescriptor chunk = chunks[chunkIndex];
            if (offset > chunk.LogicalSize ||
                checked((long)offset + size) > chunk.LogicalSize)
            {
                throw new InvalidDataException(
                    $"RP6L item {index} extends beyond logical chunk {chunkIndex}.");
            }

            items.Add(new Rp6ItemDescriptor(chunkIndex, (int)offset, size));
        }

        List<Rp6RawResource> rawResources = new(resourceCount);
        for (int index = 0; index < resourceCount; index++)
        {
            ReadOnlySpan<byte> row = data.Slice(cursor, ResourceSize);
            cursor += ResourceSize;
            short itemAmount = BinaryPrimitives.ReadInt16LittleEndian(row);
            short resourceType = BinaryPrimitives.ReadInt16LittleEndian(row[2..]);
            int nameIndex = BinaryPrimitives.ReadInt32LittleEndian(row[4..]);
            int firstItemIndex = BinaryPrimitives.ReadInt32LittleEndian(row[8..]);
            if (itemAmount < 0 ||
                nameIndex < 0 ||
                nameIndex >= nameCount ||
                firstItemIndex < 0 ||
                checked(firstItemIndex + itemAmount) > itemCount)
            {
                throw new InvalidDataException(
                    $"RP6L resource {index} has invalid table indexes.");
            }

            rawResources.Add(new Rp6RawResource(
                itemAmount,
                resourceType,
                nameIndex,
                firstItemIndex));
        }

        int[] nameOffsets = new int[nameCount];
        for (int index = 0; index < nameCount; index++)
        {
            nameOffsets[index] =
                BinaryPrimitives.ReadInt32LittleEndian(data[cursor..]);
            cursor += sizeof(int);
        }

        ReadOnlySpan<byte> nameBlob = data.Slice(cursor, nameBlobSize);
        string[] names = new string[nameCount];
        for (int index = 0; index < nameCount; index++)
        {
            int offset = nameOffsets[index];
            if (offset < 0 || offset >= nameBlob.Length)
            {
                throw new InvalidDataException(
                    $"RP6L name {index} has an invalid offset.");
            }

            int terminator = nameBlob[offset..].IndexOf((byte)0);
            if (terminator < 0)
            {
                throw new InvalidDataException(
                    $"RP6L name {index} is not NUL terminated.");
            }

            names[index] = Encoding.UTF8.GetString(
                nameBlob.Slice(offset, terminator));
        }

        List<Rp6ResourceDescriptor> resources = new(resourceCount);
        for (int index = 0; index < rawResources.Count; index++)
        {
            Rp6RawResource resource = rawResources[index];
            resources.Add(new Rp6ResourceDescriptor(
                index,
                names[resource.NameIndex],
                resource.ResourceType,
                resource.FirstItemIndex,
                resource.ItemCount));
        }

        return new Rp6Reader(fullPath, chunks, items, resources);
    }

    public async ValueTask<byte[]> ReadResourceAsync(
        Rp6ResourceDescriptor resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.Index < 0 ||
            resource.Index >= Resources.Count ||
            Resources[resource.Index] != resource)
        {
            throw new ArgumentException(
                "The resource does not belong to this RP6 reader.",
                nameof(resource));
        }

        int totalSize = 0;
        for (int index = 0; index < resource.ItemCount; index++)
        {
            totalSize = checked(
                totalSize +
                _items[resource.FirstItemIndex + index].Size);
        }

        byte[] result = GC.AllocateUninitializedArray<byte>(totalSize);
        int destination = 0;
        Dictionary<int, byte[]> chunks = [];
        for (int index = 0; index < resource.ItemCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Rp6ItemDescriptor item =
                _items[resource.FirstItemIndex + index];
            if (!chunks.TryGetValue(item.ChunkIndex, out byte[]? chunk))
            {
                chunk = await ReadChunkAsync(
                    item.ChunkIndex,
                    cancellationToken).ConfigureAwait(false);
                chunks.Add(item.ChunkIndex, chunk);
            }

            chunk.AsSpan(item.Offset, item.Size).CopyTo(
                result.AsSpan(destination, item.Size));
            destination += item.Size;
        }

        return result;
    }

    private async Task<byte[]> ReadChunkAsync(
        int index,
        CancellationToken cancellationToken)
    {
        Rp6ChunkDescriptor chunk = _chunks[index];
        int storedSize = chunk.PackedSize > 0
            ? chunk.PackedSize
            : chunk.LogicalSize;
        byte[] stored = GC.AllocateUninitializedArray<byte>(storedSize);
        await using (FileStream stream = new(
                         Path,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read | FileShare.Delete,
                         bufferSize: 128 * 1024,
                         FileOptions.Asynchronous | FileOptions.RandomAccess))
        {
            stream.Position = chunk.Offset;
            await stream.ReadExactlyAsync(
                stored,
                cancellationToken).ConfigureAwait(false);
        }

        if (chunk.PackedSize == 0)
        {
            return stored;
        }

        byte[] logical = GC.AllocateUninitializedArray<byte>(chunk.LogicalSize);
        using MemoryStream source = new(stored, writable: false);
        using ZLibStream zlib = new(source, CompressionMode.Decompress);
        int total = 0;
        while (total < logical.Length)
        {
            int read = await zlib.ReadAsync(
                logical.AsMemory(total),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total != logical.Length || zlib.ReadByte() != -1)
        {
            throw new InvalidDataException(
                $"RP6L chunk {index} did not decompress to its declared logical size.");
        }

        return logical;
    }

    private static int ReadBoundedCount(ReadOnlySpan<byte> source, string label)
    {
        int value = BinaryPrimitives.ReadInt32LittleEndian(source);
        if (value < 0 || value > MaximumTableCount)
        {
            throw new InvalidDataException(
                $"RP6L {label} count {value} is unsafe.");
        }

        return value;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            total += read;
        }
    }

    private sealed record Rp6ChunkDescriptor(
        uint Offset,
        int LogicalSize,
        int PackedSize);

    private sealed record Rp6ItemDescriptor(
        int ChunkIndex,
        int Offset,
        int Size);

    private sealed record Rp6RawResource(
        short ItemCount,
        short ResourceType,
        int NameIndex,
        int FirstItemIndex);
}
