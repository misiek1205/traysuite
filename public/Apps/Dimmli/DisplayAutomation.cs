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
