using System.Runtime.InteropServices;

namespace Dimmli;

internal enum ScheduleMode { Off, Fixed, Solar }

internal sealed record AutomationSettings(
    ScheduleMode Schedule = ScheduleMode.Off,
    int DayMinutes = 7 * 60,
    int EveningMinutes = 19 * 60,
    double Latitude = 52.23,
    double Longitude = 21.01,
    bool Fullscreen = false,
    int FullscreenLevel = 100)
{
    public bool IsValid() => Enum.IsDefined(Schedule) && DayMinutes is >= 0 and < 1440
        && EveningMinutes is >= 0 and < 1440 && DayMinutes != EveningMinutes
        && Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180
        && FullscreenLevel is >= 0 and <= 100;
}

internal sealed class ProfileSchedule(Action<Profile> activate) : IDisposable
{
    private readonly System.Windows.Forms.Timer clock = new();
    private AutomationSettings settings = new();
    private DateTimeOffset? lastTransition;

    public void Configure(AutomationSettings value)
    {
        bool timingChanged = settings.Schedule != value.Schedule || settings.DayMinutes != value.DayMinutes
            || settings.EveningMinutes != value.EveningMinutes || settings.Latitude != value.Latitude
            || settings.Longitude != value.Longitude;
        settings = value;
        if (!timingChanged)
            return;
        lastTransition = null;
        clock.Tick -= Tick;
        if (settings.Schedule != ScheduleMode.Off)
            clock.Tick += Tick;
        Reevaluate();
    }

    private void Tick(object? sender, EventArgs args) => Reevaluate();

    public void Reevaluate()
    {
        clock.Stop();
        if (settings.Schedule == ScheduleMode.Off)
            return;

        var now = DateTimeOffset.UtcNow;
        var (profile, since, next) = Calculate(settings, now, TimeZoneInfo.Local);
        if (since != lastTransition)
        {
            lastTransition = since;
            activate(profile);
        }

        clock.Interval = (int)Math.Clamp((next - now).TotalMilliseconds + 100, 250, 86_400_000);
        clock.Start();
    }

    public static (Profile Profile, DateTimeOffset Since, DateTimeOffset Next) Calculate(
        AutomationSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime localDate = TimeZoneInfo.ConvertTime(now, zone).Date;
        var events = new List<(DateTimeOffset At, Profile Profile)>();
        // A solar UTC result can land on the previous or next calendar day near the date line.
        for (int offset = -2; offset <= 2; offset++)
        {
            var date = localDate.AddDays(offset);
            var sun = settings.Schedule == ScheduleMode.Solar ? SolarTimes(date, settings.Latitude, settings.Longitude) : null;
            if (sun is { } times)
            {
                events.Add((MoveToDate(times.Rise, date, zone), Profile.Day));
                events.Add((MoveToDate(times.Set, date, zone), Profile.Evening));
            }
            else
            {
                events.Add((WallTime(date.AddMinutes(settings.DayMinutes), zone), Profile.Day));
                events.Add((WallTime(date.AddMinutes(settings.EveningMinutes), zone), Profile.Evening));
            }
        }

        var ordered = events.OrderBy(item => item.At).ToArray();
        var current = ordered.Last(item => item.At <= now);
        return (current.Profile, current.At, ordered.First(item => item.At > now).At);
    }

    private static DateTimeOffset MoveToDate(DateTimeOffset utc, DateTime date, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        return local.Date == date ? utc : WallTime(date.Add(local.TimeOfDay), zone);
    }

    private static DateTimeOffset WallTime(DateTime local, TimeZoneInfo zone)
    {
        // A spring-forward time in the missing hour fires when the clock next reaches a valid time.
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static (DateTimeOffset Rise, DateTimeOffset Set)? SolarTimes(DateTime date, double latitude, double longitude)
    {
        // NOAA solar position approximation, with the apparent horizon at 90.833 degrees.
        double yearAngle = 2 * Math.PI * (date.DayOfYear - 1) / (DateTime.IsLeapYear(date.Year) ? 366 : 365);
        double equation = 229.18 * (0.000075 + 0.001868 * Math.Cos(yearAngle) - 0.032077 * Math.Sin(yearAngle)
            - 0.014615 * Math.Cos(2 * yearAngle) - 0.040849 * Math.Sin(2 * yearAngle));
        double declination = 0.006918 - 0.399912 * Math.Cos(yearAngle) + 0.070257 * Math.Sin(yearAngle)
            - 0.006758 * Math.Cos(2 * yearAngle) + 0.000907 * Math.Sin(2 * yearAngle)
            - 0.002697 * Math.Cos(3 * yearAngle) + 0.00148 * Math.Sin(3 * yearAngle);
        double lat = latitude * Math.PI / 180;
        double angle = (Math.Cos(90.833 * Math.PI / 180) - Math.Sin(lat) * Math.Sin(declination))
            / (Math.Cos(lat) * Math.Cos(declination));
        if (!double.IsFinite(angle) || angle is < -1 or > 1)
            return null;

        double midday = 720 - 4 * longitude - equation;
        double minutes = 4 * Math.Acos(angle) * 180 / Math.PI;
        var midnight = new DateTimeOffset(date, TimeSpan.Zero);
        return (midnight.AddMinutes(midday - minutes), midnight.AddMinutes(midday + minutes));
    }

    public void Dispose() => clock.Dispose();
}

internal sealed class FullscreenWatcher : IDisposable
{
    private const uint Foreground = 3, Location = 0x800b, State = 0x800a;
    private readonly Action<string?> changed;
    private readonly WinEvent callback;
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 240 };
    private nint foregroundHook, resizeHook, active;
    private bool watching;
    private string? currentDevice;

    public FullscreenWatcher(Action<string?> changed)
    {
        this.changed = changed;
        callback = OnWinEvent;
        debounce.Tick += (_, _) => { debounce.Stop(); Evaluate(); };
    }

    public bool Enabled => foregroundHook != 0;

    public bool Configure(bool enabled)
    {
        if (enabled == Enabled)
            return true;
        if (!enabled)
        {
            Stop(notify: true);
            return true;
        }

        foregroundHook = SetWinEventHook(Foreground, Foreground, 0, callback, 0, 0, 0);
        if (!Enabled)
            return false;
        Reevaluate();
        return true;
    }

    public void Reevaluate()
    {
        if (!Enabled)
            return;

        nint window = GetForegroundWindow();
        if (window != active || !watching)
        {
            if (resizeHook != 0)
                UnhookWinEvent(resizeHook);
            active = window;
            uint thread = GetWindowThreadProcessId(window, out uint process);
            resizeHook = thread == 0 ? 0 : SetWinEventHook(State, Location, 0, callback, process, thread, 0);
            watching = true;
        }
        debounce.Stop();
        debounce.Start();
    }

    private void OnWinEvent(nint hook, uint type, nint window, int objectId, int child, uint thread, uint time)
    {
        if (!Enabled)
            return;
        if (type == Foreground)
            Reevaluate();
        else if (window == active && objectId == 0 && child == 0)
        {
            debounce.Stop();
            debounce.Start();
        }
    }

    private void Evaluate()
    {
        string? device = FindFullscreenScreen(active);
        if (!string.Equals(device, currentDevice, StringComparison.OrdinalIgnoreCase))
        {
            currentDevice = device;
            changed(device);
        }
    }

    private static string? FindFullscreenScreen(nint window)
    {
        if (window == 0 || !IsWindowVisible(window) || IsIconic(window) || window == GetDesktopWindow()
            || window == GetShellWindow() || window == GetAncestor(GetShellWindow(), 2))
            return null;
        GetWindowThreadProcessId(window, out uint process);
        if (process == (uint)Environment.ProcessId)
            return null;

        long style = GetWindowLongPtr(window, -16).ToInt64();
        if (IsZoomed(window) && (style & 0x00c00000) == 0x00c00000)
            return null; // A maximized window with a title bar is not fullscreen.
        if (DwmGetWindowAttribute(window, 9, out var rect, 16) != 0 && !GetWindowRect(window, out rect))
            return null;

        var screen = Screen.FromHandle(window);
        return CoversDisplay(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom), screen.Bounds)
            ? screen.DeviceName : null;
    }

    public static bool CoversDisplay(Rectangle window, Rectangle screen) =>
        screen.Width > 0 && screen.Height > 0 && Math.Abs(window.Left - screen.Left) <= 2
        && Math.Abs(window.Top - screen.Top) <= 2 && Math.Abs(window.Right - screen.Right) <= 2
        && Math.Abs(window.Bottom - screen.Bottom) <= 2;

    private void Stop(bool notify)
    {
        debounce.Stop();
        if (foregroundHook != 0)
            UnhookWinEvent(foregroundHook);
        if (resizeHook != 0)
            UnhookWinEvent(resizeHook);
        foregroundHook = resizeHook = active = 0;
        watching = false;
        if (currentDevice is not null)
        {
            currentDevice = null;
            if (notify)
                changed(null);
        }
    }

    public void Dispose()
    {
        Stop(notify: false);
        debounce.Dispose();
    }

    private delegate void WinEvent(nint hook, uint type, nint window, int objectId, int child, uint thread, uint time);

    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module,
        WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out MonitorNative.Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute,
        out MonitorNative.Rect rect, uint size);
}
