using System.Buffers.Binary;
using System.Text.Json;
namespace FileViz.Core;

public static class Wire
{
    public const int MaximumFrame = 2 * 1024 * 1024;
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumFrame)
            throw new InvalidDataException("IPC frame exceeds limit.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken token = default)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), token);
        if (first == 0)
            return default;
        await stream.ReadExactlyAsync(header.AsMemory(1), token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumFrame)
            throw new InvalidDataException("Invalid IPC frame length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty IPC message.");
    }
}
