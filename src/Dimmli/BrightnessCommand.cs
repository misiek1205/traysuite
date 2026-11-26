using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Dimmli;

internal sealed record BrightnessCommand(Profile? Profile = null, int? Level = null, int? Monitor = null, bool List = false)
{
    public const string Usage = """
        Dimmli.exe --list
        Dimmli.exe --profile <Day|Evening|Dzień|Wieczór>
        Dimmli.exe [--monitor <number>] --brightness <0-100>
        """;

    public bool IsValid() => List ? Profile is null && Level is null && Monitor is null
        : Profile is { } profile ? Enum.IsDefined(profile) && Level is null && Monitor is null
        : Level is >= 0 and <= 100 && (Monitor is null or > 0);

    public static BrightnessCommand? Parse(string[] args)
    {
        if (args is ["--list"])
            return new(List: true);
        if (args.Length == 0 || args.Length % 2 != 0)
            return null;

        Profile? profile = null;
        int? level = null, monitor = null;
        for (int index = 0; index < args.Length; index += 2)
        {
            string value = args[index + 1];
            switch (args[index])
            {
                case "--profile" when profile is null:
                    profile = value.ToUpperInvariant() switch
                    {
                        "DAY" or "DZIEŃ" => Dimmli.Profile.Day,
                        "EVENING" or "WIECZÓR" => Dimmli.Profile.Evening,
                        _ => null
                    };
                    if (profile is null)
                        return null;
                    break;
                case "--brightness" when level is null:
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int brightness))
                        return null;
                    level = brightness;
                    break;
                case "--monitor" when monitor is null:
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                        return null;
                    monitor = number;
                    break;
                default:
                    return null;
            }
        }

        var command = new BrightnessCommand(profile, level, monitor);
        return command.IsValid() ? command : null;
    }
}

internal sealed record CommandReply(bool Success, string Message);

internal sealed class CommandPipe : IDisposable
{
    private const int MaxFrame = 64 * 1024;
    private const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
    private static readonly string Name = PipeName();
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task listener;

    public CommandPipe(Func<BrightnessCommand, CancellationToken, Task<CommandReply>> dispatch)
    {
        listener = Task.Run(() => Listen(dispatch, shutdown.Token));
    }

    private static string PipeName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        return $"Dimmli.{identity.User?.Value}.{process.SessionId}";
    }

    private static async Task Listen(Func<BrightnessCommand, CancellationToken, Task<CommandReply>> dispatch,
        CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, Options);
                await server.WaitForConnectionAsync(stop);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                var command = await Read<BrightnessCommand>(server, deadline.Token);
                var reply = command.IsValid() ? await dispatch(command, deadline.Token)
                    : new CommandReply(false, "Invalid Dimmli command.");
                await Write(server, reply, deadline.Token);
                // Keep the server instance alive until the other process reads the reply.
                await server.ReadExactlyAsync(new byte[1], deadline.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                if (!stop.IsCancellationRequested)
                    await Task.Delay(TimeSpan.FromSeconds(2), stop).ConfigureAwait(false);
            }
        }
    }

    public static async Task<CommandReply> Send(BrightnessCommand command, CancellationToken token)
    {
        using var client = new NamedPipeClientStream(".", Name, PipeDirection.InOut, Options);
        await client.ConnectAsync(token);
        await Write(client, command, token);
        var reply = await Read<CommandReply>(client, token);
        await client.WriteAsync(new byte[] { 1 }, token);
        return reply;
    }

    private static async Task<T> Read<T>(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrame)
            throw new InvalidDataException("Invalid Dimmli command size.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, token);
        return JsonSerializer.Deserialize<T>(payload) ?? throw new JsonException("Empty Dimmli command.");
    }

    private static async Task Write<T>(Stream stream, T value, CancellationToken token)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (payload.Length > MaxFrame)
            throw new InvalidDataException("Dimmli message is too large.");
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
