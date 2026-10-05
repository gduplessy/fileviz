using System.ComponentModel;
using FileViz.Core;
using Microsoft.Win32.SafeHandles;
namespace FileViz.Windows;

public enum CheckState
{
    NotChecked, Passed, Failed
}

/// <summary>Result of a read-only cleanup precheck. <see cref="Error"/> is set when any check failed.</summary>
public sealed record CleanupCheck(CleanupSelection Selection, CheckState Identity, CheckState Metadata, CheckState Bytes, CheckState Streams, string? Error)
{
    public bool Ready => Error == null;
}

public sealed partial class CleanupService
{
    /// <summary>
    /// Runs the same validation as <see cref="QuarantineAsync"/> without moving anything, reporting each check
    /// separately for review. The move still revalidates everything under its own locks.
    /// </summary>
    public static async Task<CleanupCheck> PrecheckAsync(CleanupSelection selection, CancellationToken token = default)
    {
        CheckState identity = CheckState.NotChecked, metadata = CheckState.NotChecked, bytes = CheckState.NotChecked, streams = CheckState.NotChecked;
        var stage = 0;
        SafeFileHandle? targetHandle = null, keeperHandle = null;
        try
        {
            var target = selection.Target;
            ValidateParents(target.Path);
            targetHandle = OpenLocked(target.Path, false);
            var current = Native.ReadEntry(targetHandle, target.Path);
            CheckIdentity(target, current, "");
            identity = CheckState.Passed;
            stage = 1;
            CheckMetadata(target, current, "");
            metadata = CheckState.Passed;
            if (selection.Keeper is not { } keeper)
                return new(selection, identity, metadata, bytes, streams, null);
            stage = 0;
            ValidateParents(keeper.Path);
            keeperHandle = OpenLocked(keeper.Path, false);
            var keeperCurrent = Native.ReadEntry(keeperHandle, keeper.Path);
            identity = CheckState.NotChecked;
            CheckIdentity(keeper, keeperCurrent, "Keeper: ");
            if (current.Identity == keeperCurrent.Identity)
                throw new IOException("These paths are hard links to the same file, not independent copies.");
            identity = CheckState.Passed;
            stage = 1;
            metadata = CheckState.NotChecked;
            CheckMetadata(keeper, keeperCurrent, "Keeper: ");
            metadata = CheckState.Passed;
            stage = 3;
            var first = Streams(target.Path);
            var second = Streams(keeper.Path);
            if (!first.SequenceEqual(second))
                throw new IOException("Named streams differ from the keeper.");
            stage = 2;
            if (current.Length != keeperCurrent.Length)
                throw new IOException("Content differs from the keeper.");
            using (var a = new FileStream(new SafeFileHandle(targetHandle.DangerousGetHandle(), false), FileAccess.Read, 65536, false))
            using (var b = new FileStream(new SafeFileHandle(keeperHandle.DangerousGetHandle(), false), FileAccess.Read, 65536, false))
                await CompareBytes(a, b, token).ConfigureAwait(false);
            bytes = CheckState.Passed;
            stage = 3;
            foreach (var stream in first.Where(x => x.Name != "::$DATA"))
            {
                using var a = new FileStream(target.Path + stream.Name, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536);
                using var b = new FileStream(keeper.Path + stream.Name, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536);
                await CompareBytes(a, b, token).ConfigureAwait(false);
            }
            streams = CheckState.Passed;
            return new(selection, identity, metadata, bytes, streams, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
        {
            var message = e.Message == "Keeper content differs." ? stage == 3 ? "A named stream differs from the keeper." : "Content differs from the keeper." : e.Message;
            switch (stage)
            {
                case 0: identity = CheckState.Failed; break;
                case 1: metadata = CheckState.Failed; break;
                case 2: bytes = CheckState.Failed; break;
                default: streams = CheckState.Failed; break;
            }
            return new(selection, identity, metadata, bytes, streams, message);
        }
        finally
        {
            targetHandle?.Dispose();
            keeperHandle?.Dispose();
        }
    }
    private static void CheckIdentity(FileEntry scanned, FileEntry current, string prefix)
    {
        if (current.IsDirectory || current.IsReparse || current.IsPlaceholder || Protected(current.Path))
            throw new IOException(prefix + "Protected files, directories, reparse points and placeholders cannot be cleaned up.");
        if (current.Identity == null)
            throw new IOException(prefix + "This filesystem does not provide a verifiable file identity.");
        if (scanned.Identity != null && scanned.Identity != current.Identity)
            throw new IOException(prefix + "File identity changed; rescan first.");
    }
    private static void CheckMetadata(FileEntry scanned, FileEntry current, string prefix)
    {
        if (scanned.Length != current.Length || scanned.ModifiedTicks != current.ModifiedTicks || (scanned.ChangeTicks != 0 && scanned.ChangeTicks != current.ChangeTicks))
            throw new IOException(prefix + "Size or timestamps changed since the scan; rescan first.");
    }
}
