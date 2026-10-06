namespace FileViz.Core;

/// <summary>Header metadata only. Resolution is a review criterion, not a measure of visual quality.</summary>
public record PhotoInfo(FileEntry Entry, int Width, int Height, int Frames = 1, string? Error = null, byte[]? Preview = null)
{
    public long Pixels => (long)Width * Height;
    public int ShortEdge => Math.Min(Width, Height);
    public string Resolution => Error == null ? $"{Width:N0} × {Height:N0} · {Pixels / 1_000_000d:0.##} MP" : "Unreadable";
    public bool CacheMatches(FileEntry current) => Error == null && Entry.Identity != null && Entry.ChangeTicks > 0 &&
        Entry.Identity == current.Identity && Entry.Length == current.Length && Entry.ModifiedTicks == current.ModifiedTicks && Entry.ChangeTicks == current.ChangeTicks;
}
public record PhotoRequest(FileEntry Entry, PhotoInfo? Cached = null);
public record PhotoFilter(int MinimumShortEdge = 720, double MinimumMegapixels = 0, string Search = "", bool ErrorsOnly = false)
{
    public void Validate()
    {
        if (MinimumShortEdge is < 0 or > 100_000 || !double.IsFinite(MinimumMegapixels) || MinimumMegapixels is < 0 or > 10_000)
            throw new ArgumentException("Use a short edge from 0 to 100,000 pixels and megapixels from 0 to 10,000. Zero disables a threshold.");
    }
    public bool Matches(PhotoInfo photo) => ErrorsOnly ? photo.Error != null : photo.Error == null &&
        ((MinimumShortEdge > 0 && photo.ShortEdge < MinimumShortEdge) || (MinimumMegapixels > 0 && photo.Pixels < MinimumMegapixels * 1_000_000));
}
public static class PhotoFormats
{
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif", ".wdp", ".jxr"];
}
