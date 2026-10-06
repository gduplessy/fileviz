using System.Buffers;
using System.ComponentModel;
using System.Security.Cryptography;
using FileViz.Core;
namespace FileViz.Windows;

public static class Hashing
{
    public static HashAlgorithmName Algorithm(string name) => name.Replace("-", "").ToUpperInvariant() switch
    {
        "SHA256" => HashAlgorithmName.SHA256,
        "SHA1" => HashAlgorithmName.SHA1,
        "MD5" => HashAlgorithmName.MD5,
        _ => throw new ArgumentException("Supported algorithms: SHA-256, SHA-1, MD5.", nameof(name))
    };
    public static async Task<HashResult> HashAsync(HashRequest request, CancellationToken token = default, Func<HashProgress, Task>? progress = null)
    {
        try
        {
            var entry = Native.ReadEntry(request.Entry.Path);
            if (entry.IsDirectory || entry.IsPlaceholder || entry.IsReparse)
                throw new IOException("Directories, reparse points and offline placeholders are not hashed.");
            if ((request.Entry.Identity != null && entry.Identity != request.Entry.Identity) || entry.Length != request.Entry.Length || entry.ModifiedTicks != request.Entry.ModifiedTicks || (request.Entry.ChangeTicks != 0 && entry.ChangeTicks != request.Entry.ChangeTicks))
                throw new IOException("File changed since the scan; rescan first.");
            using var handle = Native.CreateFileW(Native.LongPath(entry.Path), 0x80000000, 1, IntPtr.Zero, 3, Native.BackupSemantics | Native.OpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            var opened = Native.ReadEntry(handle, entry.Path);
            if (opened.Identity != entry.Identity || opened.Length != entry.Length || opened.ChangeTicks != entry.ChangeTicks)
                throw new IOException("File changed while opening.");
            using var stream = new FileStream(handle, FileAccess.Read, 1024 * 1024, false);
            using var hash = IncrementalHash.CreateHash(Algorithm(request.Algorithm));
            var total = request.Sample && opened.Length > 3 * 65536 ? 3 * 65536 : opened.Length;
            long read = 0;
            var updates = System.Diagnostics.Stopwatch.StartNew();
            async Task Report(bool force = false)
            {
                if (progress != null && (force || updates.ElapsedMilliseconds >= 250))
                {
                    await progress(new(entry.Path, read, total));
                    updates.Restart();
                }
            }
            await Report(true);
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
            try
            {
                if (request.Sample && opened.Length > 3 * 65536)
                {
                    hash.AppendData(BitConverter.GetBytes(opened.Length));
                    foreach (var offset in new[] { 0L, opened.Length / 2 - 32768, opened.Length - 65536 })
                    {
                        token.ThrowIfCancellationRequested();
                        stream.Position = offset;
                        await stream.ReadExactlyAsync(buffer.AsMemory(0, 65536), token);
                        hash.AppendData(buffer, 0, 65536);
                        read += 65536;
                        await Report();
                    }
                }
                else
                {
                    int count;
                    while ((count = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
                    {
                        hash.AppendData(buffer, 0, count);
                        read += count;
                        await Report();
                    }
                }
                await Report(true);
                var after = Native.ReadEntry(handle, entry.Path);
                if (after.Identity != opened.Identity || after.Length != opened.Length || after.ModifiedTicks != opened.ModifiedTicks || after.ChangeTicks != opened.ChangeTicks)
                    throw new IOException("File changed while hashing.");
                return new(entry.Path, Convert.ToHexString(hash.GetHashAndReset()), opened.Identity, opened.Length, opened.ModifiedTicks, opened.ChangeTicks, null);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or CryptographicException)
        {
            return new(request.Entry.Path, null, null, 0, 0, 0, e.Message);
        }
    }
}
