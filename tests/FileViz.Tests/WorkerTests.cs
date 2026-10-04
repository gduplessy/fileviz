using System.Diagnostics;
using FileViz.Core;
using FileViz.Windows;
using Xunit;
namespace FileViz.Tests;
public class WorkerTests
{
    [Fact] public async Task AuthenticatedWorkerScansAndRejectsUnknownOperations()
    {
        using var fixture=new Fixture();fixture.Write("a.txt","abc");var entries=new List<FileEntry>();await using var worker=await WorkerSession.StartAsync(false);
        await worker.ExecuteAsync(new("scan",Scopes:[new(fixture.Root,[],false)]),message=>{if(message.Batch!=null)entries.AddRange(message.Batch.Entries);return Task.CompletedTask;});Assert.Single(entries);
        await Assert.ThrowsAsync<IOException>(()=>worker.ExecuteAsync(new("mutate"),_=>Task.CompletedTask));
    }
    [Fact] public async Task CancellationTerminatesHashWorkerWithinFiveSeconds()
    {
        using var fixture=new Fixture();var path=System.IO.Path.Combine(fixture.Root,"large.bin");using(var stream=File.Create(path))stream.SetLength(256L*1024*1024);
        var worker=await WorkerSession.StartAsync(false);using var cts=new CancellationTokenSource(20);var timer=Stopwatch.StartNew();
        try{await worker.ExecuteAsync(new("hash",Hashes:[new(Native.ReadEntry(path),"SHA-256")]),_=>Task.CompletedTask,cts.Token);}catch(OperationCanceledException){}
        finally{await worker.DisposeAsync();}Assert.True(timer.Elapsed<TimeSpan.FromSeconds(5),$"Cancellation took {timer.Elapsed}.");
    }
}