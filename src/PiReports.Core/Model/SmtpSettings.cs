using MailKit.Security;

namespace PiReports.Core.Model;

/// <summary>Single-row table edited from the portal's settings page.</summary>
public class SmtpSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string Host { get; set; } = "";

    public int Port { get; set; } = 25;

    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.Auto;

    /// <summary>Blank for anonymous relay.</summary>
    public string? Username { get; set; }

    /// <summary>Encrypted with ASP.NET Core Data Protection; never stored in clear text.</summary>
    public string? ProtectedPassword { get; set; }

    public string FromAddress { get; set; } = "";

    public string FromName { get; set; } = "PI Reports";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? UpdatedBy { get; set; }
}
