using System.IO.Pipes;
using System.Security.Principal;
using FileViz.Core;
using FileViz.Windows;

if (args.Length != 4 || args[0] != "--pipe" || args[2] != "--parent" || !int.TryParse(args[3], out var parent) || !args[1].StartsWith("FileViz-", StringComparison.Ordinal) || args[1].Length != 40) return 2;
try
{
    Native.EnableBackupPrivilege();
    using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
    using var timeout = new CancellationTokenSource(60000); await pipe.ConnectAsync(timeout.Token);
    if (!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var server) || server != parent) return 3;
    while (await Wire.ReadAsync<WorkerRequest>(pipe) is { } request)
    {
        try
        {
            if (request.Operation == "scan" && request.Scopes is { Length: > 0 and <= 128 } scopes)
            {
                foreach (var scope in scopes)
                {
                    if (!Path.IsPathFullyQualified(scope.Root) || scope.Root.StartsWith(@"\\.\", StringComparison.Ordinal) || scope.Exclusions.Length > 256) throw new InvalidDataException("Invalid scan scope.");
                    IScanEngine engine = new FileViz.Windows.Ntfs.AutoScanEngine();
                    await foreach (var batch in engine.ScanAsync(scope)) await Wire.WriteAsync(pipe, new WorkerMessage("batch", Batch: batch));
                }
            }
            else if (request.Operation == "hash" && request.Hashes is { Length: > 0 and <= 128 } hashes)
            {
                foreach (var hash in hashes) { Hashing.Algorithm(hash.Algorithm); await Wire.WriteAsync(pipe, new WorkerMessage("hash", Hash: await Hashing.HashAsync(hash))); }
            }
            else throw new InvalidDataException("Unknown worker operation or invalid batch.");
            await Wire.WriteAsync(pipe, new WorkerMessage("done"));
        }
        catch (Exception e) when (e is not OutOfMemoryException) { await Wire.WriteAsync(pipe, new WorkerMessage("fatal", Text: e.Message)); }
    }
    return 0;
}
catch (Exception e) when (e is not OutOfMemoryException) { Console.Error.WriteLine(e.Message); return 1; }