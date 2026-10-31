namespace PiReports.Core.Model;

public enum CaptureFormat
{
    Pdf,
    Png,
}

/// <summary>
/// A configured capture of one PI Vision display: what to open, how to render it,
/// when to run it, and where to deliver the result.
/// </summary>
public class CaptureDefinition
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    // --- Source -------------------------------------------------------------

    /// <summary>The display URL as copied from the browser address bar.</summary>
    public string DisplayUrl { get; set; } = "";

    /// <summary>PI time expression, e.g. "*-24h" or "t". Blank keeps the display's own time range.</summary>
    public string? StartTime { get; set; }

    /// <summary>PI time expression, e.g. "*" or "t+6h".</summary>
    public string? EndTime { get; set; }

    /// <summary>Optional AF element path for asset-relative displays, e.g. \\AF\Plant\Unit 3.</summary>
    public string? Asset { get; set; }

    public bool HideToolbar { get; set; } = true;

    public bool HideTimebar { get; set; }

    // --- Render -------------------------------------------------------------

    public CaptureFormat Format { get; set; } = CaptureFormat.Pdf;

    public int ViewportWidth { get; set; } = 1920;

    public int ViewportHeight { get; set; } = 1080;

    public bool Landscape { get; set; } = true;

    /// <summary>Extra time to let the display settle after the page reports it has loaded.</summary>
    public int SettleSeconds { get; set; } = 5;

    /// <summary>Upper bound on the whole capture, including the stability wait.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    // --- Schedule -----------------------------------------------------------

    /// <summary>Five-field cron expression. Blank means on demand only.</summary>
    public string? Cron { get; set; }

    /// <summary>Windows or IANA time zone id the cron expression is evaluated in.</summary>
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    // --- Delivery -----------------------------------------------------------

    /// <summary>Semicolon- or comma-separated addresses. Blank disables email.</summary>
    public string? EmailRecipients { get; set; }

    public string? EmailSubject { get; set; }

    /// <summary>Files above this size are linked from the portal instead of attached.</summary>
    public int AttachMaxMb { get; set; } = 5;

    /// <summary>UNC folder to drop the file into. Blank disables share delivery.</summary>
    public string? SharePath { get; set; }

    /// <summary>File name without extension. Tokens: {name}, {date:format}.</summary>
    public string FileNamePattern { get; set; } = "{name}_{date:yyyyMMdd_HHmm}";

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [System.Text.Json.Serialization.JsonIgnore]
    public string? UpdatedBy { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public List<ReportRun> Runs { get; set; } = [];

    /// <summary>Copies everything a user can edit, leaving identity, audit fields and run history alone.</summary>
    public void CopyEditableFieldsFrom(CaptureDefinition from)
    {
        Name = from.Name.Trim();
        Description = from.Description;
        Enabled = from.Enabled;
        DisplayUrl = from.DisplayUrl.Trim();
        StartTime = from.StartTime;
        EndTime = from.EndTime;
        Asset = from.Asset;
        HideToolbar = from.HideToolbar;
        HideTimebar = from.HideTimebar;
        Format = from.Format;
        ViewportWidth = from.ViewportWidth;
        ViewportHeight = from.ViewportHeight;
        Landscape = from.Landscape;
        SettleSeconds = from.SettleSeconds;
        TimeoutSeconds = from.TimeoutSeconds;
        Cron = string.IsNullOrWhiteSpace(from.Cron) ? null : from.Cron.Trim();
        TimeZoneId = from.TimeZoneId;
        EmailRecipients = from.EmailRecipients;
        EmailSubject = from.EmailSubject;
        AttachMaxMb = from.AttachMaxMb;
        SharePath = from.SharePath;
        FileNamePattern = string.IsNullOrWhiteSpace(from.FileNamePattern) ? "{name}_{date:yyyyMMdd_HHmm}" : from.FileNamePattern.Trim();
    }
}
