using System.Buffers.Binary;
using FileViz.Windows.Ntfs;
using Xunit;
namespace FileViz.Tests;

public class NtfsParserTests
{
    private static byte[] Record()
    {
        var bytes = new byte[1024];
        "FILE"u8.CopyTo(bytes);
        Put16(bytes, 4, 48);
        Put16(bytes, 6, 3);
        Put16(bytes, 16, 7);
        Put16(bytes, 20, 56);
        Put16(bytes, 22, 1);
        Put32(bytes, 24, 64);
        Put32(bytes, 28, 1024);
        Put16(bytes, 48, 0x1234);
        Put16(bytes, 50, 0);
        Put16(bytes, 52, 0);
        Put16(bytes, 510, 0x1234);
        Put16(bytes, 1022, 0x1234);
        Put32(bytes, 56, uint.MaxValue);
        return bytes;
    }
    [Fact]
    public void FixupsAndSequenceNumbersAreApplied()
    {
        var record = NtfsParser.Parse(Record(), 512, 42);
        Assert.True(record.InUse);
        Assert.Equal(((ulong)7 << 48) | 42, record.Reference);
    }
    [Fact]
    public void TornSectorFailsClosed()
    {
        var bytes = Record();
        bytes[510] = 0;
        Assert.Throws<InvalidDataException>(() => NtfsParser.Parse(bytes, 512, 42));
    }
    [Fact]
    public void NegativeAndUnterminatedRunsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => NtfsParser.ParseRuns([0x11, 8, 128, 0]));
        Assert.Throws<InvalidDataException>(() => NtfsParser.ParseRuns([0x11, 8, 10]));
        Assert.Throws<InvalidDataException>(() => NtfsParser.ParseRuns([0x11, 0, 10, 0]));
    }
    [Fact]
    public void FragmentedRunsDecodeCorrectly()
    {
        var runs = NtfsParser.ParseRuns([0x21, 8, 128, 0, 0x11, 2, 0xFC, 0x01, 3, 0]);
        Assert.Equal(3, runs.Length);
        Assert.Equal(128, runs[0].Lcn);
        Assert.Equal(124, runs[1].Lcn);
        Assert.Null(runs[2].Lcn);
        Assert.Equal(10, runs[2].Vcn);
    }
    [Fact]
    public void AttributeListReferencesAreBoundsChecked()
    {
        var bytes = new byte[32];
        Put32(bytes, 0, 0x80);
        Put16(bytes, 4, 32);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), ((ulong)3 << 48) | 99);
        Assert.Equal(((ulong)3 << 48) | 99, Assert.Single(NtfsParser.ParseAttributeList(bytes)).Reference);
        Put16(bytes, 4, 100);
        Assert.Throws<InvalidDataException>(() => NtfsParser.ParseAttributeList(bytes));
    }
    [Fact]
    public void MutatedRecordsNeverEscapeAsUncheckedReads()
    {
        var random = new Random(815);
        for (var i = 0; i < 5000; i++)
        {
            var bytes = Record();
            var changes = random.Next(1, 8);
            for (var j = 0; j < changes; j++)
                bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
            try
            {
                NtfsParser.Parse(bytes, 512, 42);
            }
            catch (InvalidDataException) { }
            catch (OverflowException) { }
        }
    }
    [Fact]
    public void RootDotNameIsValidOnlyForTheRootRecord()
    {
        var bytes = Record();
        Put32(bytes, 24, 160);
        Put32(bytes, 56, 0x30);
        Put32(bytes, 60, 96);
        Put32(bytes, 72, 68);
        Put16(bytes, 76, 24);
        bytes[56 + 24 + 64] = 1;
        bytes[56 + 24 + 65] = 3;
        Put16(bytes, 56 + 24 + 66, '.');
        Put32(bytes, 152, uint.MaxValue);
        Assert.Equal(".", Assert.Single(NtfsParser.Parse(bytes, 512, 5).Names).Name);
        Assert.Throws<InvalidDataException>(() => NtfsParser.Parse(bytes, 512, 42));
    }
    [Fact]
    public void ResidentDataAllocationMatchesWindowsEightByteRounding()
    {
        var bytes = Record();
        Put32(bytes, 24, 104);
        Put32(bytes, 56, 0x80);
        Put32(bytes, 60, 40);
        Put32(bytes, 72, 9);
        Put16(bytes, 76, 24);
        Put32(bytes, 96, uint.MaxValue);
        var data = Assert.Single(NtfsParser.Parse(bytes, 512, 42).Data);
        Assert.Equal(9, data.Length);
        Assert.Equal(16, data.Allocated);
    }
    private static void Put16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
