using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerFlip;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => AppLifecycle.Run("PowerFlip", args, () => ApplicationConfiguration.Initialize(),
        () => new TrayContext("PowerFlip", "PowerFlip", new PowerWindow(), SystemIcons.Information));
}

internal sealed class PowerWindow : ModuleWindow
{
    private const string Preferences = @"Software\TraySuite\PowerFlip";
    private static readonly Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid MaxProcessor = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    private readonly Label active = Label("");
    private readonly PopupToggle quiet = new()
    {
        Text = Texts.Get("Quiet / Cool Mode (99% CPU)", "Tryb Cichy / Chłodny (99% CPU)")
    };
    private readonly (string Label, Guid Scheme)[] plans =
    [
        (Texts.Get("Power saver", "Oszczędny"), new("a1841308-3541-4fab-bc81-f71556f20b4a")),
        (Texts.Get("Balanced", "Zrównoważony"), new("381b4222-f694-41f0-9685-ff5bb260df2e")),
        (Texts.Get("Ultimate performance", "Najwyższa wydajność"), new("e9a42b02-d5df-448d-aa00-03f14749eb61"))
    ];
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 1500 };
    private bool syncing;

    public PowerWindow() : base("PowerFlip", Texts.Get("Power plans and processor limit", "Plany zasilania i limit procesora"), 360)
    {
        Add(active, 28);
        foreach (var plan in plans) Add(Button(plan.Label, () => Run(() => SelectPlan(plan.Scheme))), 43);
        quiet.CheckedChanged += (_, _) =>
        {
            if (!syncing) Run(() => SetQuiet(quiet.Checked));
        };
        Add(quiet, 39);
        VisibleChanged += (_, _) =>
        {
            refresh.Enabled = Visible;
            if (Visible) Run(() => { });
        };
        refresh.Tick += (_, _) => { try { Sync(); } catch (Win32Exception) { } };
    }

    private void Run(Action change)
    {
        SetStatus("");
        try { change(); }
        catch (Exception error) when (error is Win32Exception or UnauthorizedAccessException or IOException or System.Security.SecurityException)
        { SetStatus(error.Message); }
        try { Sync(); }
        catch (Win32Exception error) { quiet.Enabled = false; SetStatus(error.Message); }
    }

    private static Guid Active()
    {
        Check(PowerGetActiveScheme(0, out nint pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    private void Sync()
    {
        Guid scheme = Active();
        active.Text = Texts.Get("Active: ", "Aktywny: ") + SchemeName(scheme);
        var (ac, dc) = ReadLimits(scheme);
        syncing = true;
        try { quiet.Checked = ac == 99 && dc == 99; quiet.Enabled = true; }
        finally { syncing = false; }
    }

    private static string SchemeName(Guid scheme)
    {
        uint size = 0;
        PowerReadFriendlyName(0, in scheme, 0, 0, null, ref size);
        if (size == 0) return scheme.ToString();
        var bytes = new byte[size];
        Check(PowerReadFriendlyName(0, in scheme, 0, 0, bytes, ref size));
        return System.Text.Encoding.Unicode.GetString(bytes, 0, (int)size).TrimEnd('\0');
    }

    private static void SelectPlan(Guid template)
    {
        uint result = PowerSetActiveScheme(0, in template);
        if (result == 0) return;
        if (result is not (2 or 3 or 1168)) Check(result);
        using var key = Registry.CurrentUser.CreateSubKey(Preferences);
        string name = template.ToString();
        if (Guid.TryParse(key.GetValue(name) as string, out var saved) && PowerSetActiveScheme(0, in saved) == 0)
            return;
        Check(PowerDuplicateScheme(0, in template, out nint pointer));
        try
        {
            Guid duplicate = Marshal.PtrToStructure<Guid>(pointer);
            try { key.SetValue(name, duplicate.ToString()); Check(PowerSetActiveScheme(0, in duplicate)); }
            catch { PowerDeleteScheme(0, in duplicate); throw; }
        }
        finally { LocalFree(pointer); }
    }

    private static (uint Ac, uint Dc) ReadLimits(Guid scheme)
    {
        Check(PowerReadACValueIndex(0, in scheme, in Processor, in MaxProcessor, out uint ac));
        Check(PowerReadDCValueIndex(0, in scheme, in Processor, in MaxProcessor, out uint dc));
        return (ac, dc);
    }

    private static void SetQuiet(bool enabled)
    {
        var scheme = Active();
        var old = ReadLimits(scheme);
        uint limit = enabled ? 99u : 100u;
        Check(PowerWriteACValueIndex(0, in scheme, in Processor, in MaxProcessor, limit));
        try
        {
            Check(PowerWriteDCValueIndex(0, in scheme, in Processor, in MaxProcessor, limit));
            if (Active() == scheme) Check(PowerSetActiveScheme(0, in scheme));
        }
        catch
        {
            PowerWriteACValueIndex(0, in scheme, in Processor, in MaxProcessor, old.Ac);
            PowerWriteDCValueIndex(0, in scheme, in Processor, in MaxProcessor, old.Dc);
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) refresh.Dispose();
        base.Dispose(disposing);
    }

    private static void Check(uint code) { if (code != 0) throw new Win32Exception((int)code); }
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(nint root, out nint scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(nint root, in Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerDuplicateScheme(nint root, in Guid source, out nint scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerDeleteScheme(nint root, in Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(nint root, in Guid scheme, nint subgroup, nint setting, byte[]? data, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
