using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Mutagen.Bethesda.Plugins;

namespace MasterStripper;

internal static class RawPluginEditor
{
    private const int RecordHeaderSize = 24;
    private const uint SmallMasterFlag = 0x0000_0200;
    private const uint CompressedRecordFlag = 0x0004_0000;

    public static int RemoveRecordsOwnedByMaster(string pluginPath, ModKey target, string dataFolder) =>
        Rewrite(pluginPath, target, dataFolder, removeReferences: false);

    public static int RemoveRecordOrContainers(string pluginPath, FormKey missing, string dataFolder) =>
        Rewrite(pluginPath, missing.ModKey, dataFolder, removeReferences: true, exactObjectId: missing.ID);

    private static int Rewrite(
        string pluginPath,
        ModKey target,
        string dataFolder,
        bool removeReferences,
        uint? exactObjectId = null)
    {
        var bytes = File.ReadAllBytes(pluginPath);
        if (bytes.Length < RecordHeaderSize || Sig(bytes, 0) != "TES4")
            throw new InvalidDataException("Not a valid Skyrim plugin.");

        var tes4Length = checked(RecordHeaderSize + ReadLength(bytes, 4));
        if (tes4Length > bytes.Length) throw new InvalidDataException("TES4 header extends past the end of the file.");

        var masters = ReadMasters(bytes.AsSpan(RecordHeaderSize, tes4Length - RecordHeaderSize));
        var mapping = BuildMapping(masters, dataFolder);
        var targetRawPrefix = mapping.PrefixFor(target);
        if (targetRawPrefix is null)
            throw new InvalidOperationException($"{target.FileName} is not declared as a master.");

        var exactRaws = exactObjectId is null
            ? []
            : mapping.RawCandidatesFor(target, exactObjectId.Value);

        var removed = 0;
        using var output = new MemoryStream(bytes.Length);
        output.Write(bytes, 0, tes4Length);
        var position = tes4Length;
        while (position < bytes.Length)
        {
            var block = ProcessBlock(bytes, position, bytes.Length, targetRawPrefix.Value, exactRaws, removeReferences, ref removed);
            if (block.Data.Length > 0) output.Write(block.Data);
            position += block.OriginalLength;
        }

        if (removed == 0) return 0;
        var temp = pluginPath + "." + Guid.NewGuid().ToString("N") + ".rawtmp";
        File.WriteAllBytes(temp, output.ToArray());
        try { File.Move(temp, pluginPath, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return removed;
    }

    private static BlockResult ProcessBlock(
        byte[] source,
        int offset,
        int limit,
        MasterPrefix target,
        IReadOnlyList<uint> exactRaws,
        bool removeReferences,
        ref int removed)
    {
        if (offset + RecordHeaderSize > limit) throw new InvalidDataException($"Truncated block at 0x{offset:X}.");
        var signature = Sig(source, offset);
        if (signature == "GRUP")
        {
            var originalLength = ReadLength(source, offset + 4);
            if (originalLength < RecordHeaderSize || offset + originalLength > limit)
                throw new InvalidDataException($"Invalid GRUP size at 0x{offset:X}.");

            using var group = new MemoryStream(originalLength);
            group.Write(source, offset, RecordHeaderSize);
            var child = offset + RecordHeaderSize;
            var groupEnd = offset + originalLength;
            while (child < groupEnd)
            {
                var result = ProcessBlock(source, child, groupEnd, target, exactRaws, removeReferences, ref removed);
                if (result.Data.Length > 0) group.Write(result.Data);
                child += result.OriginalLength;
            }

            var data = group.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4, 4), data.Length);
            return new BlockResult(data, originalLength);
        }

        var payloadLength = ReadLength(source, offset + 4);
        var recordLength = checked(RecordHeaderSize + payloadLength);
        if (recordLength < RecordHeaderSize || offset + recordLength > limit)
            throw new InvalidDataException($"Invalid {signature} size at 0x{offset:X}.");

        var rawFormId = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + 12, 4));
        var ownedByTarget = target.Matches(rawFormId);
        var exactRecord = exactRaws.Contains(rawFormId);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + 8, 4));
        var containsExactReference = removeReferences &&
                                     exactRaws.Any(raw => ContainsReference(
                                         source.AsSpan(offset + RecordHeaderSize, payloadLength),
                                         flags,
                                         raw));
        if (ownedByTarget || exactRecord || containsExactReference)
        {
            removed++;
            return new BlockResult([], recordLength);
        }

        return new BlockResult(source.AsSpan(offset, recordLength).ToArray(), recordLength);
    }

    private static bool Contains(ReadOnlySpan<byte> data, uint value)
    {
        Span<byte> needle = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(needle, value);
        return data.IndexOf(needle) >= 0;
    }

    private static bool ContainsReference(ReadOnlySpan<byte> payload, uint flags, uint value)
    {
        if ((flags & CompressedRecordFlag) == 0) return Contains(payload, value);
        if (payload.Length < 6) return false;

        try
        {
            var expectedLength = BinaryPrimitives.ReadInt32LittleEndian(payload[..4]);
            using var compressed = new MemoryStream(payload[4..].ToArray(), writable: false);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var expanded = expectedLength > 0 ? new MemoryStream(expectedLength) : new MemoryStream();
            zlib.CopyTo(expanded);
            return Contains(expanded.ToArray(), value);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static List<string> ReadMasters(ReadOnlySpan<byte> tes4Data)
    {
        var masters = new List<string>();
        var position = 0;
        while (position + 6 <= tes4Data.Length)
        {
            var signature = Encoding.ASCII.GetString(tes4Data.Slice(position, 4));
            var length = BinaryPrimitives.ReadUInt16LittleEndian(tes4Data.Slice(position + 4, 2));
            position += 6;
            if (position + length > tes4Data.Length) break;
            if (signature == "MAST")
            {
                var raw = tes4Data.Slice(position, length);
                var nullAt = raw.IndexOf((byte)0);
                if (nullAt >= 0) raw = raw[..nullAt];
                masters.Add(Encoding.UTF8.GetString(raw));
            }
            position += length;
        }
        return masters;
    }

    private static MasterMap BuildMapping(IEnumerable<string> masters, string dataFolder)
    {
        var declared = new List<ModKey>();
        var small = new List<ModKey>();
        foreach (var name in masters)
        {
            var key = ModKey.FromFileName(name);
            declared.Add(key);
            if (IsSmallMaster(Path.Combine(dataFolder, name), name)) small.Add(key);
        }
        return new MasterMap(declared, small);
    }

    private static bool IsSmallMaster(string path, string name)
    {
        if (Path.GetExtension(name).Equals(".esl", StringComparison.OrdinalIgnoreCase)) return true;
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[RecordHeaderSize];
        if (stream.Read(header) != RecordHeaderSize || Encoding.ASCII.GetString(header[..4]) != "TES4") return false;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));
        return (flags & SmallMasterFlag) != 0;
    }

    private static int ReadLength(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));

    private static string Sig(byte[] data, int offset) =>
        Encoding.ASCII.GetString(data, offset, 4);

    private readonly record struct BlockResult(byte[] Data, int OriginalLength);

    private readonly record struct MasterPrefix(bool IsSmall, int Index)
    {
        public bool Matches(uint rawFormId) =>
            IsSmall
                ? (rawFormId & 0xFF00_0000u) == 0xFE00_0000u && ((rawFormId >> 12) & 0xFFFu) == (uint)Index
                : (rawFormId >> 24) == (uint)Index;
    }

    private sealed record MasterMap(IReadOnlyList<ModKey> Declared, IReadOnlyList<ModKey> Small)
    {
        public MasterPrefix? PrefixFor(ModKey key)
        {
            for (var i = 0; i < Small.Count; i++) if (Small[i] == key) return new MasterPrefix(true, i);
            // A normal FormID's high byte is the master's position in the complete
            // TES4 MAST list, including any light masters that precede it.
            for (var i = 0; i < Declared.Count; i++) if (Declared[i] == key) return new MasterPrefix(false, i);
            return null;
        }

        public uint[] RawCandidatesFor(ModKey key, uint objectId)
        {
            var candidates = new List<uint>(2);
            for (var i = 0; i < Declared.Count; i++)
            {
                if (Declared[i] != key) continue;
                candidates.Add(((uint)i << 24) | (objectId & 0x00FF_FFFFu));
                break;
            }
            for (var i = 0; i < Small.Count; i++)
            {
                if (Small[i] != key) continue;
                candidates.Add(0xFE00_0000u | ((uint)i << 12) | (objectId & 0xFFFu));
                break;
            }
            return candidates.Distinct().ToArray();
        }
    }
}
