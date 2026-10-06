using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using FileViz.Core;
using FileViz.Windows;

namespace FileViz.Worker;

/// <summary>WIC codecs run exclusively in the disposable read-only worker, never in the UI.</summary>
internal static class PhotoReader
{
    public static PhotoInfo Read(PhotoRequest request, bool preview)
    {
        var scanned = request.Entry;
        try
        {
            PhotoMemoryBudget.Ensure();
            if (!Path.IsPathFullyQualified(scanned.Path) || scanned.Path.StartsWith(@"\\.\", StringComparison.Ordinal) || !PhotoFormats.Extensions.Contains(scanned.Extension))
                throw new InvalidDataException("Unsupported image path or extension.");
            if (scanned.IsDirectory || scanned.IsPlaceholder || scanned.IsReparse)
                throw new IOException("Directories, reparse points and cloud placeholders are skipped.");
            // Do not follow junctions into a different scope or hydrate a placeholder parent.
            for (var parent = scanned.Parent; !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            {
                var entry = Native.ReadEntry(parent);
                if (entry.IsReparse || entry.IsPlaceholder) throw new IOException("Image has a reparse-point or placeholder parent.");
            }
            // FILE_FLAG_OPEN_NO_RECALL prevents offline/provider data from being recalled by this read.
            using var handle = Native.CreateFileW(Native.LongPath(scanned.Path), 0x80000000, 1, IntPtr.Zero, 3, Native.OpenReparsePoint | Native.BackupSemantics | 0x00100000, IntPtr.Zero);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var current = Native.ReadEntry(handle, scanned.Path);
            if (current.IsDirectory || current.IsReparse || current.IsPlaceholder) throw new IOException("Image is a directory, reparse point or cloud placeholder.");
            if ((scanned.Identity != null && scanned.Identity != current.Identity) || scanned.Length != current.Length ||
                scanned.ModifiedTicks != current.ModifiedTicks || (scanned.ChangeTicks != 0 && scanned.ChangeTicks != current.ChangeTicks))
                throw new IOException("Image changed since the inventory scan; rescan first.");
            if (!preview && request.Cached is { } cached && cached.CacheMatches(current)) return cached with { Entry = current, Preview = null };
            using var stream = new FileStream(handle, FileAccess.Read, 65536, false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            if (decoder.Frames.Count is < 1 or > 256) throw new IOException("Image has too many frames for bounded analysis.");
            // The largest frame avoids classifying a multipage image by its thumbnail-sized first page.
            var width = 0; var height = 0;
            foreach (var frame in decoder.Frames)
            {
                if (frame.PixelWidth is < 1 or > 100_000 || frame.PixelHeight is < 1 or > 100_000) throw new IOException("Invalid or excessive image dimensions.");
                width = Math.Max(width, frame.PixelWidth); height = Math.Max(height, frame.PixelHeight);
            }
            var result = new PhotoInfo(current, width, height, decoder.Frames.Count);
            if (!preview) return result;
            if (result.Pixels > 25_000_000 || current.Length > 64L * 1024 * 1024) throw new IOException("Preview exceeds the safety budget. Open the original in Explorer.");
            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.StreamSource = stream; bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (width >= height) bitmap.DecodePixelWidth = Math.Min(512, width); else bitmap.DecodePixelHeight = Math.Min(512, height);
            bitmap.EndInit(); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream(); encoder.Save(output);
            if (output.Length > 1024 * 1024) throw new IOException("Preview exceeds the IPC budget.");
            return result with { Preview = output.ToArray() };
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new(scanned, 0, 0, Error: e.Message);
        }
    }
}
