namespace PrayerApp.Shared.Enums;

/// <summary>How an admin closed a report. Null on the report while it's still open.</summary>
public enum ReportResolution
{
    /// <summary>The post was kept (or restored).</summary>
    Dismissed = 1,

    PostHidden = 2,
}
