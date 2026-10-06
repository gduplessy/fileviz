using FileViz.Core;
using FileViz.Data;
using FileViz.Windows;

namespace FileViz.App.Services;

public record PhotoProgress(string Phase, long Done, long Total, long Errors, string Path = "");

/// <summary>One codec request at a time, with a deadline; failed workers are discarded.</summary>
public sealed class PhotoCoordinator : IAsyncDisposable
{
    private WorkerSession? worker;
    private readonly bool elevate;
    public PhotoCoordinator(bool elevate = false) => this.elevate = elevate;
    public async Task<PhotoInfo> ReadAsync(PhotoRequest request, bool preview, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            worker ??= await WorkerSession.StartAsync(elevate, token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            PhotoInfo? result = null;
            await worker.ExecuteAsync(new(preview ? "photo-preview" : "photo", Photos: [request]), message =>
            {
                result = message.Photo;
                return Task.CompletedTask;
            }, deadline.Token);
            return result ?? throw new IOException("Image worker returned no result.");
        }
        catch (Exception e) when (e is IOException or OperationCanceledException)
        {
            await DisposeAsync();
            token.ThrowIfCancellationRequested();
            return new(request.Entry, 0, 0, Error: e is OperationCanceledException ? "Image read timed out; no removal was selected." : e.Message);
        }
    }
    public async Task AnalyzeAsync(string database, long snapshot, string root, IProgress<PhotoProgress> progress, CancellationToken token)
    {
        using var store = new IndexStore(database);
        progress.Report(new("Preparing image queries", 0, 0, 0));
        store.PreparePhotos(snapshot, token);
        var total = store.PhotoCandidateCount(snapshot, root);
        long done = 0, errors = 0;
        var after = "";
        var updates = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var batch = store.PhotoCandidates(snapshot, root, after);
            if (batch.Count == 0) break;
            var results = new List<PhotoInfo>();
            try
            {
                foreach (var request in batch)
                {
                    token.ThrowIfCancellationRequested();
                    // At most five normal updates per second; a slow read still identifies the current file.
                    if (updates.ElapsedMilliseconds >= 200 || done == 0)
                    {
                        progress.Report(new("Reading image dimensions", done, total, errors, request.Entry.Path));
                        updates.Restart();
                    }
                    var photo = await ReadAsync(request, false, token).ConfigureAwait(false);
                    results.Add(photo);
                    done++; if (photo.Error != null) errors++;
                    after = request.Entry.Path;
                }
            }
            // Commit bounded batches even on cancellation; only complete results are persisted.
            finally { store.SavePhotos(snapshot, results); }
        }
        progress.Report(new("Photo analysis complete", done, total, errors));
    }
    public async ValueTask DisposeAsync()
    {
        if (worker is { } active) { worker = null; await active.DisposeAsync(); }
    }
}
