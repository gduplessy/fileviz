using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FileViz.Core;
using Microsoft.Win32.SafeHandles;
namespace FileViz.Windows.Ntfs;

public sealed class MftScanEngine : IScanEngine
{
    public async IAsyncEnumerable<ScanBatch> ScanAsync(ScanScope scope, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var root = Native.VolumeRoot(scope.Root);
        if (!Native.IsElevated || !Paths.Normalize(root).Equals(Paths.Normalize(scope.Root), StringComparison.OrdinalIgnoreCase) || root.StartsWith(@"\\", StringComparison.Ordinal))
            throw new NotSupportedException("Raw MFT scanning requires an elevated, complete local NTFS volume.");
        using var reader = new MftReader(root);
        // Only directory ancestry stays in memory. A cap forces streaming directory enumeration for unusually directory-heavy volumes.
        var directories = new Dictionary<ulong, NtfsName>();
        var counter = 0L;
        var total = 2 * reader.RecordCount;
        long ancestryBytes = 0;
        ulong rootReference = 0;
        foreach (var record in reader.Records(cancellationToken, true))
        {
            if (record.InUse && record.IsDirectory && record.BaseReference == 0 && (record.Reference & NtfsParser.ReferenceMask) == 5)
                rootReference = record.Reference;
            if (record.InUse && record.IsDirectory && record.BaseReference == 0 && (record.Reference & NtfsParser.ReferenceMask) != 5)
            {
                var full = reader.Complete(record);
                var name = full.Names.FirstOrDefault();
                if (name != null)
                {
                    directories[record.Reference] = name;
                    ancestryBytes += 160L + 2L * name.Name.Length;
                }
                if (directories.Count > 250000 || ancestryBytes > 64L * 1024 * 1024)
                    throw new NotSupportedException("Directory ancestry exceeds the raw scanner memory budget; using directory enumeration.");
            }
            if (++counter % 16384 == 0)
                yield return new(scope.Root, $"MFT: indexing ancestry ({counter:N0} records)", [], [], Progress: new(counter, total));
        }
        if (rootReference == 0)
            throw new InvalidDataException("Missing NTFS root record.");
        long pathCacheBytes = 0;
        var budget = 0;
        var excludedParents = new Dictionary<ulong, bool>();
        var volumeName = Native.VolumeName(root);
        var pathCache = new Dictionary<ulong, string>();
        var entries = new List<FileEntry>(256);
        var errors = new List<ScanError>();
        // Two passes over the MFT: ancestry, then entries. Progress counts both.
        var processed = counter;
        foreach (var record in reader.Records(cancellationToken))
        {
            processed++;
            if (!record.InUse || record.BaseReference != 0 || (record.Reference & NtfsParser.ReferenceMask) == 5)
                continue;
            var full = reader.Complete(record);
            foreach (var name in full.Names)
            {
                var parent = Resolve(name.Parent, root, directories, pathCache, rootReference, ref pathCacheBytes);
                var path = System.IO.Path.Combine(parent, name.Name);
                if (!excludedParents.TryGetValue(name.Parent, out var excluded))
                {
                    var ancestor = parent;
                    excluded = false;
                    while (Paths.Within(ancestor, scope.Root))
                    {
                        if (Paths.Excluded(ancestor, scope.Exclusions))
                        {
                            excluded = true;
                            break;
                        }
                        var nextParent = System.IO.Path.GetDirectoryName(ancestor);
                        if (nextParent == null || nextParent == ancestor)
                            break;
                        ancestor = nextParent;
                    }
                    if (excludedParents.Count >= 10000)
                        excludedParents.Clear();
                    excludedParents[name.Parent] = excluded;
                }
                if (excluded || Paths.Excluded(path, scope.Exclusions))
                    continue;
                if (path.Length > 32760)
                    throw new NotSupportedException("NTFS path exceeds the supported Win32 path length.");
                var data = full.Data.FirstOrDefault(x => x.Type == 0x80 && x.Name.Length == 0 && x.LowestVcn == 0);
                var attributes = full.Attributes | (full.IsDirectory ? 16u : 0u);
                var idBytes = new byte[16];
                BinaryPrimitives.WriteUInt64LittleEndian(idBytes, full.Reference);
                entries.Add(new(path, parent, name.Name, volumeName + ":" + Convert.ToHexString(idBytes), full.IsDirectory,
                    full.IsDirectory ? 0 : data?.Length ?? 0, full.IsDirectory ? 0 : data?.Allocated ?? 0, full.ModifiedTicks, full.ChangeTicks, attributes));
                budget += 6 * (path.Length + parent.Length + name.Name.Length) + 1024;
                if (entries.Count == 256 || budget >= 1024 * 1024)
                {
                    yield return new(scope.Root, "Raw MFT", entries.ToArray(), errors.ToArray(), Progress: new(Math.Min(processed, total), total));
                    entries.Clear();
                    budget = 0;
                    errors.Clear();
                }
            }
        }
        reader.ValidateStableMft();
        if (entries.Count > 0 || errors.Count > 0)
            yield return new(scope.Root, "Raw MFT", entries.ToArray(), errors.ToArray(), Progress: new(total, total));
    }
    private static string Resolve(ulong reference, string root, Dictionary<ulong, NtfsName> directories, Dictionary<ulong, string> cache, ulong rootReference, ref long cacheBytes)
    {
        if ((reference & NtfsParser.ReferenceMask) == 5)
        {
            if (reference != rootReference)
                throw new InvalidDataException("Recycled NTFS root reference.");
            return Paths.Normalize(root);
        }
        if (cache.TryGetValue(reference, out var found))
            return found;
        var chain = new List<(ulong Reference, string Name)>();
        var seen = new HashSet<ulong>();
        var current = reference;
        while ((current & NtfsParser.ReferenceMask) != 5)
        {
            if (cache.TryGetValue(current, out found))
                break;
            if (!seen.Add(current) || chain.Count >= 1024 || !directories.TryGetValue(current, out var name))
                throw new InvalidDataException("Unresolved or recycled NTFS directory reference; rescan through directory enumeration.");
            chain.Add((current, name.Name));
            current = name.Parent;
        }
        if ((current & NtfsParser.ReferenceMask) == 5 && current != rootReference)
            throw new InvalidDataException("Recycled NTFS root reference.");
        var path = found ?? Paths.Normalize(root);
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (path.Length + chain[i].Name.Length + 1 > 32760)
                throw new NotSupportedException("NTFS path exceeds the supported Win32 path length.");
            path = System.IO.Path.Combine(path, chain[i].Name);
            var bytes = 96L + 2L * path.Length;
            if (cache.Count >= 10000 || cacheBytes + bytes > 32L * 1024 * 1024)
            {
                cache.Clear();
                cacheBytes = 0;
            }
            cache[chain[i].Reference] = path;
            cacheBytes += bytes;
        }
        return path;
    }
}
internal sealed class MftReader : IDisposable
{
    private readonly SafeFileHandle handle; private readonly FileStream stream;
    private readonly int sectorSize, clusterSize, recordSize; private readonly long volumeBytes;
    private readonly DataRun[] runs; private readonly long length; private readonly ulong mftReference;
    /// <summary>Number of file records in the MFT, used as the scan progress denominator.</summary>
    public long RecordCount => length / recordSize;
    public MftReader(string root)
    {
        var volume = Native.VolumeName(root).TrimEnd('\\');
        handle = Native.CreateFileW(volume, 0x80000000, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open NTFS volume read-only.");
        try
        {
            var geometry = new byte[128];
            Control(0x90064, null, geometry, out var returned);
            if (returned < 104 || BitConverter.ToUInt16(geometry, 100) != 3 || BitConverter.ToUInt16(geometry, 102) > 1)
                throw new NotSupportedException("Raw scanner supports NTFS 3.0/3.1 only.");
            sectorSize = BitConverter.ToInt32(geometry, 40);
            clusterSize = BitConverter.ToInt32(geometry, 44);
            recordSize = BitConverter.ToInt32(geometry, 48);
            if (sectorSize is < 256 or > 4096 || clusterSize < sectorSize || clusterSize > 2 * 1024 * 1024 || clusterSize % sectorSize != 0 || recordSize is < 512 or > 65536 || recordSize % sectorSize != 0)
                throw new InvalidDataException("Invalid volume geometry.");
            volumeBytes = checked(BitConverter.ToInt64(geometry, 8) * sectorSize);
            stream = new FileStream(handle, FileAccess.Read, 1024 * 1024, false);
            var mft = Complete(GetRecord(0));
            mftReference = mft.Reference;
            var data = mft.Data.Where(x => x.Type == 0x80 && x.Name.Length == 0).OrderBy(x => x.LowestVcn).ToArray();
            length = data.FirstOrDefault(x => x.LowestVcn == 0)?.Length ?? throw new InvalidDataException("Missing MFT data.");
            runs = data.SelectMany(x => x.Runs).OrderBy(x => x.Vcn).ToArray();
            if (length <= 0 || length % recordSize != 0 || length > volumeBytes)
                throw new InvalidDataException("Invalid MFT length.");
            long next = 0;
            foreach (var run in runs)
            {
                if (run.Vcn != next || run.Lcn == null || run.Lcn < 0 || checked((run.Lcn.Value + run.Clusters) * clusterSize) > volumeBytes)
                    throw new InvalidDataException("Invalid MFT run coverage.");
                next = checked(run.Vcn + run.Clusters);
            }
            if (checked(next * clusterSize) < length)
                throw new InvalidDataException("Incomplete MFT extents.");
        }
        catch { handle.Dispose(); throw; }
    }
    private void Control(uint code, byte[]? input, byte[] output, out int returned)
    {
        if (!Native.DeviceIoControl(handle, code, input, input?.Length ?? 0, output, output.Length, out returned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public NtfsRecord GetRecord(ulong reference)
    {
        var output = new byte[recordSize + 16];
        Control(0x90068, BitConverter.GetBytes(reference), output, out var returned);
        var actual = BitConverter.ToUInt64(output, 0);
        var size = BitConverter.ToInt32(output, 8);
        if (returned < 12 || (actual & NtfsParser.ReferenceMask) != (reference & NtfsParser.ReferenceMask) || size != recordSize || 12L + size > returned)
            throw new InvalidDataException("NTFS returned a different file record.");
        var record = NtfsParser.Parse(output.AsSpan(12, size), sectorSize, actual, true);
        if ((reference >> 48) != 0 && reference != record.Reference)
            throw new InvalidDataException("Recycled NTFS record reference.");
        return record;
    }
    public NtfsRecord Complete(NtfsRecord original)
    {
        var references = original.References.ToList();
        foreach (var attribute in original.Data.Where(x => x.Type == 0x20 && x.Value == null && x.LowestVcn == 0))
            references.AddRange(NtfsParser.ParseAttributeList(ReadAttribute(attribute)));
        if (references.Count == 0)
            return original;
        var names = original.Names.ToList();
        var data = original.Data.ToList();
        var fetched = new HashSet<ulong>();
        static long MetadataBytes(NtfsRecord record) => record.Names.Sum(x => 160L + 2L * x.Name.Length) + record.Data.Sum(x => 160L + 2L * x.Name.Length + (x.Value?.LongLength ?? 0) + 48L * x.Runs.Length);
        var metadataBytes = MetadataBytes(original);
        foreach (var reference in references)
        {
            if (reference.Reference == original.Reference || !fetched.Add(reference.Reference))
                continue;
            if (fetched.Count > 4096)
                throw new InvalidDataException("Too many extension records.");
            var extension = GetRecord(reference.Reference);
            if (!extension.InUse || extension.BaseReference != original.Reference)
                throw new InvalidDataException("Extension base reference mismatch.");
            metadataBytes += MetadataBytes(extension);
            if (metadataBytes > 32L * 1024 * 1024)
                throw new NotSupportedException("Extension metadata exceeds the raw scanner memory budget.");
            names.AddRange(extension.Names);
            data.AddRange(extension.Data);
        }
        return original with
        {
            Names = names.Distinct().ToArray(),
            Data = data.DistinctBy(x => (x.Type, x.Name, x.LowestVcn, x.Instance)).ToArray()
        };
    }
    private byte[] ReadAttribute(NtfsAttribute attribute)
    {
        if (attribute.Length < 0 || attribute.Length > 16 * 1024 * 1024)
            throw new NotSupportedException("Attribute list exceeds parser memory budget.");
        var savedPosition = stream.Position;
        try
        {
            var value = new byte[(int)attribute.Length];
            var written = 0;
            foreach (var run in attribute.Runs)
            {
                if (run.Lcn == null || checked((run.Lcn.Value + run.Clusters) * clusterSize) > volumeBytes)
                    throw new InvalidDataException("Invalid attribute extent.");
                var count = (int)Math.Min(value.Length - written, checked(run.Clusters * clusterSize));
                if (count <= 0)
                    break;
                var aligned = checked((count + sectorSize - 1) / sectorSize * sectorSize);
                var buffer = new byte[aligned];
                stream.Position = checked(run.Lcn.Value * clusterSize);
                stream.ReadExactly(buffer);
                buffer.AsSpan(0, count).CopyTo(value.AsSpan(written));
                written += count;
            }
            if (written != value.Length)
                throw new InvalidDataException("Incomplete attribute list.");
            return value;
        }
        finally { stream.Position = savedPosition; }
    }
    public IEnumerable<NtfsRecord> Records(CancellationToken token, bool directoriesOnly = false)
    {
        var buffer = new byte[1024 * 1024];
        long index = 0, remaining = length;
        foreach (var run in runs)
        {
            stream.Position = checked(run.Lcn!.Value * clusterSize);
            var bytes = Math.Min(checked(run.Clusters * clusterSize), remaining);
            while (bytes > 0)
            {
                token.ThrowIfCancellationRequested();
                var count = (int)Math.Min(buffer.Length, bytes);
                if (count % recordSize != 0)
                    throw new InvalidDataException("MFT record spans unsupported run boundary.");
                stream.ReadExactly(buffer.AsSpan(0, count));
                for (var offset = 0; offset < count; offset += recordSize, index++)
                {
                    if (!buffer.AsSpan(offset, 4).SequenceEqual("FILE"u8))
                    {
                        if (buffer.AsSpan(offset, recordSize).IndexOfAnyExcept((byte)0) >= 0)
                            throw new InvalidDataException("Invalid MFT record signature.");
                        continue;
                    }
                    var flags = BitConverter.ToUInt16(buffer, offset + 22);
                    if ((flags & 1) == 0 || (directoriesOnly && (flags & 2) == 0))
                        continue;
                    yield return NtfsParser.Parse(buffer.AsSpan(offset, recordSize), sectorSize, (ulong)index);
                }
                bytes -= count;
                remaining -= count;
            }
            if (remaining == 0)
                break;
        }
        if (remaining != 0)
            throw new InvalidDataException("Truncated MFT read.");
    }
    public void ValidateStableMft()
    {
        var record = Complete(GetRecord(0));
        var current = record.Data.Where(x => x.Type == 0x80 && x.Name.Length == 0).OrderBy(x => x.LowestVcn).ToArray();
        if (record.Reference != mftReference || current.FirstOrDefault(x => x.LowestVcn == 0)?.Length != length || !current.SelectMany(x => x.Runs).SequenceEqual(runs))
            throw new InvalidDataException("MFT changed during scanning; retrying directory enumeration.");
    }
    public void Dispose()
    {
        stream.Dispose();
        handle.Dispose();
    }
}

public sealed class AutoScanEngine : IScanEngine
{
    public async IAsyncEnumerable<ScanBatch> ScanAsync(ScanScope scope, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? failure = null;
        if (scope.PreferMft && Native.IsElevated && !scope.Root.StartsWith(@"\\", StringComparison.Ordinal) && Paths.Normalize(scope.Root).Equals(Paths.Normalize(Native.VolumeRoot(scope.Root)), StringComparison.OrdinalIgnoreCase))
        {
            await using var iterator = new MftScanEngine().ScanAsync(scope, cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                ScanBatch? batch = null;
                try
                {
                    if (!await iterator.MoveNextAsync())
                        yield break;
                    batch = iterator.Current;
                }
                catch (Exception e) when (e is IOException or NotSupportedException or Win32Exception or UnauthorizedAccessException or OverflowException) { failure = e.Message; break; }
                yield return batch;
            }
        }
        if (failure != null)
            yield return new(scope.Root, "Directory fallback", [], [new(scope.Root, failure, DiagnosticKinds.EngineFallback)], true);
        await foreach (var batch in new DirectoryScanEngine().ScanAsync(scope, cancellationToken))
            yield return batch;
    }
}
