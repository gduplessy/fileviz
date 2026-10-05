namespace FileViz.Core;

/// <summary>File-type categories used for space-map coloring. Order is the storage order of folder category totals.</summary>
public enum FileCategory
{
    DiskImage, Video, Code, Image, Audio, Document, Archive, Cache, Other
}

/// <summary>Extension-only classification. Defined once here and reused by SQL aggregation.</summary>
public static class FileCategories
{
    public const int Count = 9;
    private static readonly Dictionary<string, FileCategory> map = Build();
    private static Dictionary<string, FileCategory> Build()
    {
        var result = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory category, string extensions)
        {
            foreach (var extension in extensions.Split(' '))
                result["." + extension] = category;
        }
        Add(FileCategory.DiskImage, "vhd vhdx vmdk iso img wim esd qcow2");
        Add(FileCategory.Video, "mp4 mkv mov avi wmv webm m4v ts");
        Add(FileCategory.Code, "cs js ts py java go rs c cpp h json xml jar class pdb obj o lib nupkg whl");
        Add(FileCategory.Image, "jpg jpeg png gif heic webp tif tiff bmp dng cr2 cr3 nef arw psd");
        Add(FileCategory.Audio, "mp3 flac wav aac m4a ogg opus wma");
        Add(FileCategory.Document, "pdf doc docx xls xlsx ppt pptx odt rtf txt md epub");
        Add(FileCategory.Archive, "zip 7z rar tar gz bz2 xz zst cab msi msix appx");
        Add(FileCategory.Cache, "tmp log etl dmp cache");
        return result;
    }
    /// <summary>Lowercase extensions with the leading dot, mapped to their category. Everything else is Other.</summary>
    public static IReadOnlyDictionary<string, FileCategory> Extensions => map;
    public static FileCategory Of(string extension) => map.TryGetValue(extension, out var category) ? category : FileCategory.Other;
    public static string Name(FileCategory category) => category switch
    {
        FileCategory.DiskImage => "Disk images",
        FileCategory.Video => "Video",
        FileCategory.Code => "Code and SDKs",
        FileCategory.Image => "Images",
        FileCategory.Audio => "Audio",
        FileCategory.Document => "Documents",
        FileCategory.Archive => "Archives and installers",
        FileCategory.Cache => "Caches and temp",
        _ => "System and other"
    };
}

/// <summary>Last-modified age buckets relative to a snapshot's start time, so a snapshot's colors do not drift.</summary>
public static class AgeBuckets
{
    public const int Count = 5;
    /// <summary>Upper bounds of buckets 0 to 3; bucket 4 is everything older, including unknown modification times.</summary>
    public static readonly TimeSpan[] Limits = [TimeSpan.FromDays(30), TimeSpan.FromDays(182), TimeSpan.FromDays(365), TimeSpan.FromDays(1095)];
    public static int Of(long modifiedTicks, long referenceTicks)
    {
        if (modifiedTicks <= 0)
            return Count - 1;
        var age = referenceTicks - modifiedTicks;
        for (var bucket = 0; bucket < Limits.Length; bucket++)
            if (age < Limits[bucket].Ticks)
                return bucket;
        return Count - 1;
    }
    public static string Name(int bucket) => bucket switch
    {
        0 => "Under 30 days",
        1 => "1 to 6 months",
        2 => "6 to 12 months",
        3 => "1 to 3 years",
        _ => "Over 3 years"
    };
    /// <summary>Byte-weighted mean bucket, rounded. Returns -1 when there are no bytes.</summary>
    public static int Weighted(IReadOnlyList<long> bytes)
    {
        double total = 0, weighted = 0;
        for (var bucket = 0; bucket < bytes.Count; bucket++)
        {
            total += bytes[bucket];
            weighted += (double)bytes[bucket] * bucket;
        }
        return total <= 0 ? -1 : (int)Math.Round(weighted / total, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Logical bytes by category and by age bucket.</summary>
public sealed record Composition(long[] Categories, long[] Ages)
{
    public static Composition Empty => new(new long[FileCategories.Count], new long[AgeBuckets.Count]);
    public static Composition ForFile(FileEntry entry, long referenceTicks)
    {
        var result = Empty;
        result.Categories[(int)FileCategories.Of(entry.Extension)] = entry.Length;
        result.Ages[AgeBuckets.Of(entry.ModifiedTicks, referenceTicks)] = entry.Length;
        return result;
    }
    /// <summary>Category holding the most bytes, or Other when empty.</summary>
    public FileCategory Dominant
    {
        get
        {
            var best = (int)FileCategory.Other;
            for (var index = 0; index < Categories.Length; index++)
                if (Categories[index] > Categories[best])
                    best = index;
            return (FileCategory)best;
        }
    }
    public int AgeBucket => Math.Max(0, AgeBuckets.Weighted(Ages));
}

/// <summary>One tile of the space map. Folders may carry a second level of children.</summary>
public sealed record MapNode(string Path, string Name, bool IsDirectory, long Bytes, long Files, Composition Composition, IReadOnlyList<MapNode> Children);

public readonly record struct TreemapRect(double X, double Y, double Width, double Height);

/// <summary>Squarified treemap layout (Bruls, Huizing, van Wijk). Pure and UI-independent.</summary>
public static class TreemapLayout
{
    /// <summary>Returns one rectangle per value, in input order. Zero and negative values get empty rectangles.</summary>
    public static TreemapRect[] Squarify(IReadOnlyList<double> values, TreemapRect bounds)
    {
        var result = new TreemapRect[values.Count];
        var order = Enumerable.Range(0, values.Count).Where(i => values[i] > 0).OrderByDescending(i => values[i]).ToList();
        var total = order.Sum(i => values[i]);
        if (order.Count == 0 || total <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return result;
        var scale = bounds.Width * bounds.Height / total;
        double x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height;
        var start = 0;
        while (start < order.Count)
        {
            var side = Math.Min(width, height);
            var end = start + 1;
            var rowArea = values[order[start]] * scale;
            var worst = Worst(start, end, rowArea);
            while (end < order.Count)
            {
                var nextArea = rowArea + values[order[end]] * scale;
                var next = Worst(start, end + 1, nextArea);
                if (next > worst)
                    break;
                worst = next;
                rowArea = nextArea;
                end++;
            }
            if (width >= height)
            {
                var rowWidth = rowArea / height;
                var cursor = y;
                for (var i = start; i < end; i++)
                {
                    var itemHeight = values[order[i]] * scale / rowWidth;
                    result[order[i]] = new(x, cursor, rowWidth, itemHeight);
                    cursor += itemHeight;
                }
                x += rowWidth;
                width -= rowWidth;
            }
            else
            {
                var rowHeight = rowArea / width;
                var cursor = x;
                for (var i = start; i < end; i++)
                {
                    var itemWidth = values[order[i]] * scale / rowHeight;
                    result[order[i]] = new(cursor, y, itemWidth, rowHeight);
                    cursor += itemWidth;
                }
                y += rowHeight;
                height -= rowHeight;
            }
            start = end;

            double Worst(int from, int to, double area)
            {
                double max = 0, min = double.MaxValue;
                for (var i = from; i < to; i++)
                {
                    var item = values[order[i]] * scale;
                    max = Math.Max(max, item);
                    min = Math.Min(min, item);
                }
                return Math.Max(side * side * max / (area * area), area * area / (side * side * min));
            }
        }
        return result;
    }
}
