using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using FileViz.Core;
namespace FileViz.Windows;

public sealed class WorkerSession : IAsyncDisposable
{
    private readonly NamedPipeServerStream pipe;
    private readonly Process process;
    private WorkerSession(NamedPipeServerStream pipe, Process process) { this.pipe = pipe; this.process = process; }
    public static async Task<WorkerSession> StartAsync(bool elevate, CancellationToken token = default)
    {
        var name = "FileViz-" + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 65536, 65536);
        Process? process = null;
        try
        {
            var executable = System.IO.Path.Combine(AppContext.BaseDirectory, "worker", "FileViz.Worker.exe");
            if (!File.Exists(executable)) executable = System.IO.Path.Combine(AppContext.BaseDirectory, "FileViz.Worker.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("Worker is missing; use the complete release package.", executable);
            var info = new ProcessStartInfo(executable) { UseShellExecute = elevate && !Native.IsElevated, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            if (info.UseShellExecute) info.Verb = "runas";
            info.ArgumentList.Add("--pipe"); info.ArgumentList.Add(name); info.ArgumentList.Add("--parent"); info.ArgumentList.Add(Environment.ProcessId.ToString());
            process = Process.Start(info) ?? throw new IOException("Could not launch worker.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await pipe.WaitForConnectionAsync(timeout.Token);
            if (!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid != process.Id) throw new UnauthorizedAccessException("Worker process identity mismatch.");
            return new(pipe, process);
        }
        catch { pipe.Dispose(); if (process != null) { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } process.Dispose(); } throw; }
    }
    public async Task ExecuteAsync(WorkerRequest request, Func<WorkerMessage, Task> onMessage, CancellationToken token = default)
    {
        await Wire.WriteAsync(pipe, request, token);
        while (true)
        {
            var message = await Wire.ReadAsync<WorkerMessage>(pipe, token) ?? throw new IOException("Worker exited before completing the request.");
            if (message.Kind == "done") break;
            if (message.Kind == "fatal") throw new IOException(message.Text);
            await onMessage(message);
        }
    }
    public async ValueTask DisposeAsync()
    {
        pipe.Dispose();
        try { if (!process.HasExited) { process.Kill(true); using var timeout = new CancellationTokenSource(5000); await process.WaitForExitAsync(timeout.Token); } }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or OperationCanceledException) { }
        process.Dispose();
    }
}