using System.ComponentModel;
using System.Runtime.InteropServices;
using FileViz.Core;
using Microsoft.Win32.SafeHandles;
namespace FileViz.Windows;

public record CleanupSelection(FileEntry Target,FileEntry? Keeper=null);
public sealed class CleanupService(Action<CleanupRecord> journal)
{
    [StructLayout(LayoutKind.Sequential)] private struct IoStatus { public IntPtr Status; public UIntPtr Information; }
    [DllImport("ntdll.dll")] private static extern int NtSetInformationFile(SafeFileHandle file,out IoStatus status,IntPtr buffer,uint length,int infoClass);
    [DllImport("ntdll.dll")] private static extern uint RtlNtStatusToDosError(int status);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct StreamData
    { public long Size; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=296)] public string Name; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr FindFirstStreamW(string path,int level,out StreamData data,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindNextStreamW(IntPtr handle,out StreamData data);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindClose(IntPtr handle);
    public static bool Protected(string path)
    {
        path=Paths.Normalize(path);
        var roots=new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),AppContext.BaseDirectory };
        return roots.Where(x=>x.Length>0).Any(x=>Paths.Within(path,Paths.Normalize(x))) || System.IO.Path.GetFileName(path).StartsWith('$');
    }
    private static void Validate(FileEntry scanned,FileEntry current)
    {
        if(current.IsDirectory || current.IsReparse || current.IsPlaceholder || Protected(current.Path))throw new IOException("Protected files, directories, reparse points and placeholders cannot be cleaned up.");
        if(scanned.Length!=current.Length || scanned.ModifiedTicks!=current.ModifiedTicks || (scanned.ChangeTicks!=0 && scanned.ChangeTicks!=current.ChangeTicks) || (scanned.Identity!=null && scanned.Identity!=current.Identity))throw new IOException("File identity or metadata changed; rescan first.");
        if(current.Identity==null)throw new IOException("This filesystem does not provide a verifiable file identity.");
    }
    private static void ValidateParents(string path)
    {
        var parent=System.IO.Path.GetDirectoryName(path);var root=Paths.Normalize(Native.VolumeRoot(path));
        while(parent!=null && !Paths.Normalize(parent).Equals(root,StringComparison.OrdinalIgnoreCase))
        {
            if(Native.ReadEntry(parent).IsReparse)throw new IOException("Cleanup through a reparse-point parent is blocked.");parent=System.IO.Path.GetDirectoryName(parent);
        }
    }
    public async Task<CleanupRecord> QuarantineAsync(CleanupSelection selection,CancellationToken token=default)
    {
        var id=Guid.NewGuid().ToString("N");var target=selection.Target;
        var folder=System.IO.Path.Combine(target.Parent,".FileViz-Quarantine");var destination=System.IO.Path.Combine(folder,id+"-"+target.Name);
        var record=new CleanupRecord(id,target.Path,destination,"","Pending",DateTime.UtcNow.ToString("O"));
        SafeFileHandle? keeperHandle=null;List<FileStream>? heldStreams=null;
        try
        {
            token.ThrowIfCancellationRequested();ValidateParents(target.Path);
            using var targetHandle=OpenLocked(target.Path,true);var current=Native.ReadEntry(targetHandle,target.Path);Validate(target,current);
            if(selection.Keeper is { } keeper)
            {
                ValidateParents(keeper.Path);keeperHandle=OpenLocked(keeper.Path,false);var keeperCurrent=Native.ReadEntry(keeperHandle,keeper.Path);Validate(keeper,keeperCurrent);
                if(current.Identity==keeperCurrent.Identity)throw new IOException("These paths are hard links to the same file, not independent copies.");
                if(current.Length!=keeperCurrent.Length)throw new IOException("Keeper content differs.");
                heldStreams=await CompareStreamsAsync(target.Path,keeper.Path,targetHandle,keeperHandle,token);
            }
            Directory.CreateDirectory(folder);var folderEntry=Native.ReadEntry(folder);
            if(folderEntry.IsReparse || !folderEntry.IsDirectory)throw new IOException("Quarantine directory is unsafe.");
            File.SetAttributes(folder,File.GetAttributes(folder)|FileAttributes.Hidden);
            using var folderHandle=Native.CreateFileW(Native.LongPath(folder),2,7,IntPtr.Zero,3,Native.BackupSemantics|Native.OpenReparsePoint,IntPtr.Zero);
            if(folderHandle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
            record=record with { Identity=current.Identity! };journal(record); // Durable intent precedes the handle-based rename.
            token.ThrowIfCancellationRequested();Rename(targetHandle,folderHandle,System.IO.Path.GetFileName(destination));
            record=record with { State="Quarantined" };journal(record);return record;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or Win32Exception or OperationCanceledException)
        { record=record with { State="Failed",Error=e.Message };journal(record);return record; }
        finally { if(heldStreams!=null)foreach(var stream in heldStreams)stream.Dispose();keeperHandle?.Dispose(); }
    }
    public CleanupRecord Restore(CleanupRecord record)
    {
        try
        {
            if(record.State!="Quarantined")throw new IOException("Only quarantined files can be restored.");ValidateParents(record.Destination);ValidateParents(record.Original);
            using var source=OpenLocked(record.Destination,true);var current=Native.ReadEntry(source,record.Destination);
            if(current.Identity!=record.Identity || current.IsReparse)throw new IOException("Quarantined file identity changed.");
            if(File.Exists(record.Original) || Directory.Exists(record.Original))throw new IOException("Restore destination already exists.");
            var parent=System.IO.Path.GetDirectoryName(record.Original)!;
            using var directory=Native.CreateFileW(Native.LongPath(parent),2,7,IntPtr.Zero,3,Native.BackupSemantics|Native.OpenReparsePoint,IntPtr.Zero);
            if(directory.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
            journal(record with { State="Restoring",Error=null });Rename(source,directory,System.IO.Path.GetFileName(record.Original));
            record=record with { State="Restored",Error=null };journal(record);return record;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or Win32Exception) { var failed=record with { Error=e.Message };journal(failed);return failed; }
    }
    public CleanupRecord Recycle(CleanupRecord record)
    {
        try
        {
            if(record.State!="Quarantined" || record.Destination.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Recycle Bin is only offered for quarantined files on local volumes.");
            var current=Native.ReadEntry(record.Destination);if(current.Identity!=record.Identity || current.IsReparse)throw new IOException("Quarantine identity changed.");
            // Windows must display its own dialogs, including any unavailable-recycle warning. No silent permanent-delete fallback.
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(record.Destination,Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            record=record with { State="Sent to Recycle Bin",Error=null };journal(record);return record;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or OperationCanceledException) { record=record with { Error=e.Message };journal(record);return record; }
    }
    public CleanupRecord Recover(CleanupRecord record)
    {
        if(record.State is not ("Pending" or "Restoring"))return record;
        try
        {
            if(File.Exists(record.Destination) && Native.ReadEntry(record.Destination).Identity==record.Identity && !File.Exists(record.Original))record=record with { State="Quarantined",Error=null };
            else if(File.Exists(record.Original) && Native.ReadEntry(record.Original).Identity==record.Identity && !File.Exists(record.Destination))record=record with { State=record.State=="Restoring"?"Restored":"Failed",Error=record.State=="Pending"?"Move did not complete.":null };
            else record=record with { State="Needs review",Error="Interrupted operation could not be reconciled automatically." };
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or Win32Exception) { record=record with { State="Needs review",Error=e.Message }; }
        journal(record);return record;
    }
    private static SafeFileHandle OpenLocked(string path,bool delete)
    {
        var handle=Native.CreateFileW(Native.LongPath(path),0x80000000u|(delete?0x10000u:0),1,IntPtr.Zero,3,Native.BackupSemantics|Native.OpenReparsePoint,IntPtr.Zero);
        if(handle.IsInvalid){handle.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error(),path);}return handle;
    }
    private static void Rename(SafeFileHandle source,SafeFileHandle directory,string name)
    {
        var bytes=System.Text.Encoding.Unicode.GetBytes(name);var size=22+bytes.Length;var buffer=Marshal.AllocHGlobal(size);
        try
        {
            for(var i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
            Marshal.WriteIntPtr(buffer,8,directory.DangerousGetHandle());Marshal.WriteInt32(buffer,16,bytes.Length);Marshal.Copy(bytes,0,buffer+20,bytes.Length);
            var status=NtSetInformationFile(source,out _,buffer,(uint)size,10);if(status<0)throw new Win32Exception((int)RtlNtStatusToDosError(status));
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
    private static List<(string Name,long Size)> Streams(string path)
    {
        var result=new List<(string,long)>();var handle=FindFirstStreamW(Native.LongPath(path),0,out var data,0);
        if(handle==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error(),"Alternate stream enumeration is unavailable; verified duplicate cleanup is blocked.");
        try { do { if(result.Count>=4096)throw new IOException("Too many alternate streams.");result.Add((data.Name,data.Size)); }while(FindNextStreamW(handle,out data));var error=Marshal.GetLastWin32Error();if(error!=38)throw new Win32Exception(error); }
        finally{FindClose(handle);}return result.OrderBy(x=>x.Item1,StringComparer.OrdinalIgnoreCase).ToList();
    }
    private static async Task<List<FileStream>> CompareStreamsAsync(string target,string keeper,SafeFileHandle targetHandle,SafeFileHandle keeperHandle,CancellationToken token)
    {
        var first=Streams(target);var second=Streams(keeper);
        if(!first.SequenceEqual(second))throw new IOException("Main or alternate streams differ.");
        // Duplicate safe-handle wrappers leave ownership with the locked handles above.
        using(var a=new FileStream(new SafeFileHandle(targetHandle.DangerousGetHandle(),false),FileAccess.Read,65536,false))
        using(var b=new FileStream(new SafeFileHandle(keeperHandle.DangerousGetHandle(),false),FileAccess.Read,65536,false))await CompareBytes(a,b,token);
        var held=new List<FileStream>();
        try
        {
            foreach(var stream in first.Where(x=>x.Name!="::$DATA"))
            {
                var a=new FileStream(target+stream.Name,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,65536);held.Add(a);
                var b=new FileStream(keeper+stream.Name,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,65536);held.Add(b);await CompareBytes(a,b,token);
            }
        }
        catch{foreach(var stream in held)stream.Dispose();throw;}
        return held;
    }
    private static async Task CompareBytes(Stream first,Stream second,CancellationToken token)
    {
        var a=new byte[65536];var b=new byte[65536];
        while(true)
        {
            var count=await first.ReadAsync(a,token);var other=0;
            while(other<count){var read=await second.ReadAsync(b.AsMemory(other,count-other),token);if(read==0)throw new IOException("Keeper content differs.");other+=read;}
            if(!a.AsSpan(0,count).SequenceEqual(b.AsSpan(0,count)))throw new IOException("Keeper content differs.");
            if(count==0){if(await second.ReadAsync(b.AsMemory(0,1),token)!=0)throw new IOException("Keeper content differs.");break;}
        }
    }
}