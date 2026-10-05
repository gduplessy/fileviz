using System.Buffers.Binary;
using System.Text;
namespace FileViz.Windows.Ntfs;

public record DataRun(long Vcn, long Clusters, long? Lcn);
public record NtfsName(ulong Parent, string Name, byte Namespace);
public record NtfsAttribute(uint Type, string Name, ushort Instance, long LowestVcn, long Length, long Allocated, byte[]? Value, DataRun[] Runs);
public record AttributeReference(uint Type, ushort Instance, ulong Reference, long LowestVcn, string Name);
public record NtfsRecord(ulong Reference, ulong BaseReference, bool InUse, bool IsDirectory, uint Attributes,
    long ModifiedTicks, long ChangeTicks, NtfsName[] Names, NtfsAttribute[] Data, AttributeReference[] References);

/// <summary>Strict NTFS 3.0/3.1 metadata decoder. No filesystem writes or content decompression.</summary>
public static class NtfsParser
{
    public const ulong ReferenceMask = 0x0000FFFFFFFFFFFF;
    public static NtfsRecord Parse(ReadOnlySpan<byte> source, int sectorSize, ulong index, bool kernelFixed = false)
    {
        if (source.Length < 64 || source.Length > 65536 || sectorSize < 256 || sectorSize > 4096 || source.Length % sectorSize != 0)
            throw Bad("Record geometry");
        if (!source[..4].SequenceEqual("FILE"u8))
            throw Bad("Record signature");
        var bytes = source.ToArray();
        var fixOffset = U16(bytes, 4);
        var fixCount = U16(bytes, 6);
        if (fixCount != source.Length / sectorSize + 1 || fixOffset < 8 || fixOffset + fixCount * 2 > bytes.Length)
            throw Bad("Fixup array");
        var sequence = U16(bytes, fixOffset);
        for (var sector = 1; sector < fixCount; sector++)
        {
            var end = sector * sectorSize - 2;
            var replacement = U16(bytes, fixOffset + sector * 2);
            var actual = U16(bytes, end);
            if (actual != sequence && !(kernelFixed && actual == replacement))
                throw Bad("Sector fixup mismatch");
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(end, 2), replacement);
        }
        var used = U32(bytes, 24);
        var attributeOffset = U16(bytes, 20);
        var flags = U16(bytes, 22);
        if (used < 64 || used > bytes.Length || attributeOffset < 48 || attributeOffset % 8 != 0 || attributeOffset >= used)
            throw Bad("Record bounds");
        var reference = (index & ReferenceMask) | ((ulong)U16(bytes, 16) << 48);
        var baseRef = U64(bytes, 32);
        var names = new List<NtfsName>();
        var data = new List<NtfsAttribute>();
        var refs = new List<AttributeReference>();
        uint attributes = 0;
        long modified = 0, changed = 0;
        var count = 0;
        var offset = (int)attributeOffset;
        while (offset + 4 <= used)
        {
            var type = U32(bytes, offset);
            if (type == uint.MaxValue)
                break;
            if (++count > 4096 || offset + 24 > used)
                throw Bad("Attribute count/header");
            var length = U32(bytes, offset + 4);
            if (length < 24 || length % 8 != 0 || offset + (long)length > used)
                throw Bad("Attribute length");
            var attr = bytes.AsSpan(offset, (int)length);
            var form = attr[8];
            var nameLength = attr[9];
            var nameOffset = U16(attr, 10);
            var name = nameLength == 0 ? "" : ReadName(attr, nameOffset, nameLength);
            var instance = U16(attr, 14);
            byte[]? value = null;
            DataRun[] runs = [];
            long lowest = 0, size = 0, allocated = 0;
            if (form == 0)
            {
                var valueLength = U32(attr, 16);
                var valueOffset = U16(attr, 20);
                if (valueOffset < 24 || valueOffset + (long)valueLength > attr.Length)
                    throw Bad("Resident bounds");
                value = attr.Slice(valueOffset, (int)valueLength).ToArray();
                size = valueLength;
                allocated = ((long)valueLength + 7) & ~7L;
            }
            else if (form == 1)
            {
                if (length < 64)
                    throw Bad("Nonresident header");
                lowest = I64(attr, 16);
                var highest = I64(attr, 24);
                var runOffset = U16(attr, 32);
                if (lowest < 0 || highest < lowest - 1 || runOffset < 64 || runOffset >= attr.Length)
                    throw Bad("Nonresident bounds");
                runs = ParseRuns(attr[runOffset..], lowest);
                if (runs.Length > 0 && checked(runs[^1].Vcn + runs[^1].Clusters - 1) != highest)
                    throw Bad("Run coverage");
                if (lowest == 0)
                {
                    size = I64(attr, 48);
                    allocated = I64(attr, 40);
                    if ((U16(attr, 12) & 0x80ff) != 0)
                    {
                        if (length < 72 || runOffset < 72)
                            throw Bad("Sparse/compressed header");
                        allocated = I64(attr, 64);
                    }
                    if (size < 0 || allocated < 0 || I64(attr, 56) < 0 || I64(attr, 56) > size)
                        throw Bad("Data length");
                }
            }
            else
                throw Bad("Attribute form");
            if (type == 0x10 && value != null)
            {
                if (value.Length < 36)
                    throw Bad("Standard information");
                modified = Native.FileTime(I64(value, 8));
                changed = Native.FileTime(I64(value, 16));
                attributes = U32(value, 32);
            }
            else if (type == 0x30 && value != null)
            {
                if (value.Length < 66)
                    throw Bad("File name header");
                var space = value[65];
                var filename = ReadName(value, 66, value[64]);
                if (filename.Length == 0 || (filename == ".." || (filename == "." && index != 5)) || filename.IndexOfAny(['\\', '/', '\0']) >= 0 || space > 3)
                    throw Bad("File name");
                if (space != 2)
                    names.Add(new(U64(value, 0), filename, space));
            }
            else if (type == 0x20 && value != null)
                refs.AddRange(ParseAttributeList(value));
            if (type is 0x80 or 0x20)
                data.Add(new(type, name, instance, lowest, size, allocated, value, runs));
            offset = checked(offset + (int)length);
        }
        if (offset + 4 > used || U32(bytes, offset) != uint.MaxValue)
            throw Bad("Missing attribute terminator");
        return new(reference, baseRef, (flags & 1) != 0, (flags & 2) != 0, attributes, modified, changed, names.Distinct().ToArray(), data.ToArray(), refs.ToArray());
    }
    public static DataRun[] ParseRuns(ReadOnlySpan<byte> bytes, long initialVcn = 0)
    {
        var result = new List<DataRun>();
        var offset = 0;
        var vcn = initialVcn;
        long lcn = 0;
        while (offset < bytes.Length)
        {
            var header = bytes[offset++];
            if (header == 0)
                return result.ToArray();
            var countBytes = header & 15;
            var offsetBytes = header >> 4;
            if (countBytes is < 1 or > 8 || offsetBytes > 8 || offset + countBytes + offsetBytes > bytes.Length || result.Count >= 100000)
                throw Bad("Run encoding");
            ulong count = 0;
            for (var i = 0; i < countBytes; i++)
                count |= (ulong)bytes[offset + i] << (8 * i);
            offset += countBytes;
            if (count == 0 || count > long.MaxValue)
                throw Bad("Run length");
            long? physical = null;
            if (offsetBytes > 0)
            {
                long delta = 0;
                for (var i = 0; i < offsetBytes; i++)
                    delta |= (long)bytes[offset + i] << (8 * i);
                if (offsetBytes < 8 && (bytes[offset + offsetBytes - 1] & 0x80) != 0)
                    delta |= (-1L) << (8 * offsetBytes);
                offset += offsetBytes;
                lcn = checked(lcn + delta);
                if (lcn < 0)
                    throw Bad("Negative LCN");
                physical = lcn;
            }
            result.Add(new(vcn, (long)count, physical));
            vcn = checked(vcn + (long)count);
        }
        throw Bad("Unterminated runlist");
    }
    public static AttributeReference[] ParseAttributeList(ReadOnlySpan<byte> bytes)
    {
        var result = new List<AttributeReference>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 26)
                throw Bad("Attribute list header");
            var entry = bytes[offset..];
            var length = U16(entry, 4);
            if (length < 26 || length % 8 != 0 || offset + length > bytes.Length || result.Count >= 100000)
                throw Bad("Attribute list bounds");
            var name = entry[6] == 0 ? "" : ReadName(entry[..length], entry[7], entry[6]);
            var vcn = I64(entry, 8);
            if (vcn < 0)
                throw Bad("Attribute VCN");
            result.Add(new(U32(entry, 0), U16(entry, 24), U64(entry, 16), vcn, name));
            offset += length;
        }
        return result.ToArray();
    }
    private static string ReadName(ReadOnlySpan<byte> value, int offset, int count)
    {
        if (offset < 0 || offset + (long)count * 2 > value.Length)
            throw Bad("Name bounds");
        return Encoding.Unicode.GetString(value.Slice(offset, count * 2));
    }
    private static InvalidDataException Bad(string reason) => new("Unsupported or inconsistent NTFS metadata: " + reason);
    private static ushort U16(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(o, 2));
    private static uint U32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(o, 4));
    private static ulong U64(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(o, 8));
    private static long I64(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadInt64LittleEndian(b.Slice(o, 8));
}
