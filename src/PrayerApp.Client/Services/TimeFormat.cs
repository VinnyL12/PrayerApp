namespace PrayerApp.Client.Services;

public static class TimeFormat
{
    /// <summary>"just now", "5m ago", "3h ago", "2d ago", then a date.</summary>
    public static string Ago(DateTimeOffset time, DateTimeOffset now)
    {
        var elapsed = now - time;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes}m ago",
            { TotalDays: < 1 } => $"{(int)elapsed.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)elapsed.TotalDays}d ago",
            _ => time.ToLocalTime().ToString("MMM d, yyyy"),
        };
    }
}
