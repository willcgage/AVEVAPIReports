namespace PiReports.Core;

/// <summary>Server-level settings from appsettings.json, section "PiReports".</summary>
public class PiReportsOptions
{
    public const string Section = "PiReports";

    /// <summary>Local folder where every run's output is archived and served from by the portal.</summary>
    public string OutputRoot { get; set; } = Path.Combine(AppContext.BaseDirectory, "output");

    /// <summary>Public base URL of this portal, used for download links in emails.</summary>
    public string? PortalBaseUrl { get; set; }

    /// <summary>
    /// Path to a Chromium-based browser. On a Windows server, the installed Edge works:
    /// C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe
    /// </summary>
    public string? BrowserExecutablePath { get; set; }

    /// <summary>
    /// Hosts the headless browser may send Windows (Kerberos/NTLM) credentials to,
    /// e.g. "pivision.corp.local,*.corp.local". Defaults to the host of each display URL.
    /// </summary>
    public string? IntegratedAuthAllowlist { get; set; }

    /// <summary>Extra command-line switches for the browser, e.g. a proxy setting.</summary>
    public string[] BrowserArgs { get; set; } = [];

    /// <summary>AD group allowed to edit definitions and settings. Blank lets any signed-in user.</summary>
    public string? AdminGroup { get; set; }

    /// <summary>Folder for Data Protection keys (used to encrypt the SMTP password).</summary>
    public string? DataProtectionKeysPath { get; set; }

    /// <summary>
    /// Short name of this installation, e.g. "TEST". When set, the portal shows a banner
    /// and email subjects are prefixed with it. Leave blank on Production.
    /// </summary>
    public string? EnvironmentLabel { get; set; }

    /// <summary>
    /// When set, every email goes to this address instead of the capture's recipients,
    /// with the intended recipients listed in the body. Use on Test so real people get nothing.
    /// </summary>
    public string? EmailRedirectTo { get; set; }

    /// <summary>
    /// When set, share deliveries are written under this folder instead of the capture's
    /// shared folder, in a subfolder named after it. Use on Test so production shares are untouched.
    /// </summary>
    public string? ShareRedirectRoot { get; set; }

    /// <summary>
    /// Create and upgrade the database schema at startup. Turn off when a DBA applies
    /// the scripts in docs/dba and the service account has only read/write rights.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;

    /// <summary>Days to keep archived output. 0 keeps everything.</summary>
    public int RetentionDays { get; set; } = 90;
}
