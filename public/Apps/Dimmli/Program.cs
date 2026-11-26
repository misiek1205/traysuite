using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Dimmli;

internal static class Program
{
    // Share the mutex with the previous portable release.
    private const string InstanceName = @"Local\Brightness.Tray";

    [STAThread]
    private static int Main(string[] args)
    {
        string mode = args.FirstOrDefault() ?? "--start";
        if (args.Length > 1 || mode is not ("--start" or "--open" or "--exit"))
            return RunCommand(args);

        using var instance = new Mutex(true, InstanceName, out bool firstInstance);
        if (!firstInstance)
            return mode == "--start" ? 0 : AppLifecycle.Forward("Dimmli", mode);
        if (mode == "--exit")
            return 0;

        ApplicationConfiguration.Initialize();
        using var app = new TrayApplication();
        if (mode == "--open")
            app.OpenFromLauncher();
        Application.Run(app);
        return 0;
    }

    private static int RunCommand(string[] args)
    {
        AttachConsole(uint.MaxValue);
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(BrightnessCommand.Usage);
            return 0;
        }
        if (BrightnessCommand.Parse(args) is not { } command)
        {
            Console.Error.WriteLine(BrightnessCommand.Usage);
            return 2;
        }

        try
        {
            // CLI callers start the tray if needed, then use the same pipe as other callers.
            bool start;
            using (var probe = new Mutex(false, InstanceName, out start)) { }
            if (start)
            {
                if (command.ReleaseGame)
                    return 0;
                if (Environment.ProcessPath is null)
                    return 1;
                using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false });
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(22));
            var reply = CommandPipe<BrightnessCommand>.Send("Dimmli", command, deadline.Token).GetAwaiter().GetResult();
            (reply.Success ? Console.Out : Console.Error).WriteLine(reply.Message);
            return reply.Success ? 0 : 1;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException
            or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not complete the Dimmli command. {error.Message}");
            return 1;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint process);
}
