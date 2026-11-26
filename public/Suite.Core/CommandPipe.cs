using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Suite.Core;

public sealed record CommandReply(bool Success, string Message);

public sealed class CommandPipe<T> : IDisposable where T : class
{
    private const int MaxFrame = 64 * 1024;
    private const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task listener;

    public CommandPipe(string application, Func<T, CancellationToken, Task<CommandReply>> dispatch, Action<T>? afterReply = null)
    {
        listener = Task.Run(() => Listen(PipeName(application), dispatch, afterReply, shutdown.Token));
    }

    private static string PipeName(string application)
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        return $"{application}.{identity.User?.Value}.{process.SessionId}";
    }

    private static async Task Listen(string name, Func<T, CancellationToken, Task<CommandReply>> dispatch,
        Action<T>? afterReply, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, Options);
                await server.WaitForConnectionAsync(stop);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                var command = await Read<T>(server, deadline.Token);
                var reply = await dispatch(command, deadline.Token);
                await Write(server, reply, deadline.Token);
                // Keep the server instance alive until the other process reads the reply.
                await server.ReadExactlyAsync(new byte[1], deadline.Token);
                afterReply?.Invoke(command);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
            {
                if (!stop.IsCancellationRequested)
                    await Task.Delay(250, stop);
            }
        }
    }

    public static async Task<CommandReply> Send(string application, T command, CancellationToken token,
        bool allowForeground = false)
    {
        using var client = new NamedPipeClientStream(".", PipeName(application), PipeDirection.InOut, Options);
        await client.ConnectAsync(token);
        if (allowForeground)
            AppLifecycle.AllowForeground(client.SafePipeHandle);
        await Write(client, command, token);
        var reply = await Read<CommandReply>(client, token);
        await client.WriteAsync(new byte[] { 1 }, token);
        return reply;
    }

    private static async Task<TValue> Read<TValue>(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrame)
            throw new InvalidDataException("Invalid command size.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, token);
        return JsonSerializer.Deserialize<TValue>(payload) ?? throw new JsonException("Empty command.");
    }

    private static async Task Write<TValue>(Stream stream, TValue value, CancellationToken token)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (payload.Length > MaxFrame)
            throw new InvalidDataException("message is too large.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(payload, token);
        await stream.FlushAsync(token);
    }

    public void Dispose()
    {
        shutdown.Cancel();
        // A UI dispatch may still be unwinding, so the UI thread must not wait on it.
        _ = listener.ContinueWith(_ => shutdown.Dispose(), TaskScheduler.Default);
    }
}
