using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PiReports.Core.Data;
using PiReports.Core.Delivery;
using PiReports.Core.Model;
using PiReports.Web.Infrastructure;

namespace PiReports.Web.Pages.Settings;

public class EmailModel(PiReportsDbContext db, EmailSender sender, IDataProtectionProvider protection, ILogger<EmailModel> logger) : PageModel
{
    [BindProperty]
    public SmtpSettings Input { get; set; } = new();

    [BindProperty]
    public string? NewPassword { get; set; }

    [BindProperty]
    public bool ClearPassword { get; set; }

    [BindProperty]
    public string? TestTo { get; set; }

    public bool HasPassword { get; private set; }

    public IEnumerable<SelectListItem> SecurityOptions =>
    [
        new("Auto (STARTTLS when offered)", nameof(SecureSocketOptions.Auto)),
        new("None (plain relay on port 25)", nameof(SecureSocketOptions.None)),
        new("STARTTLS required (usually 587)", nameof(SecureSocketOptions.StartTls)),
        new("TLS on connect (usually 465)", nameof(SecureSocketOptions.SslOnConnect)),
    ];

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        TestTo ??= Input.FromAddress;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        ModelState.Remove(nameof(TestTo));
        if (!ModelState.IsValid)
        {
            HasPassword = await db.SmtpSettings.AnyAsync(s => s.ProtectedPassword != null, ct);
            return Page();
        }

        var settings = await db.SmtpSettings.SingleOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new SmtpSettings();
            db.SmtpSettings.Add(settings);
        }

        settings.Host = Input.Host.Trim();
        settings.Port = Input.Port;
        settings.Security = Input.Security;
        settings.Username = string.IsNullOrWhiteSpace(Input.Username) ? null : Input.Username.Trim();
        settings.FromAddress = Input.FromAddress.Trim();
        settings.FromName = string.IsNullOrWhiteSpace(Input.FromName) ? "PI Reports" : Input.FromName.Trim();
        if (ClearPassword)
        {
            settings.ProtectedPassword = null;
        }
        else if (!string.IsNullOrEmpty(NewPassword))
        {
            settings.ProtectedPassword = protection.CreateProtector(EmailSender.ProtectorPurpose).Protect(NewPassword);
        }

        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = User.DisplayName();
        await db.SaveChangesAsync(ct);

        TempData["Message"] = "Email settings saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(TestTo))
        {
            TempData["Error"] = "Enter an address to send the test to.";
            return RedirectToPage();
        }

        try
        {
            var sentTo = await sender.SendAsync([TestTo.Trim()], "PI Reports test email",
                $"<p>This is a test from PI Reports, sent by {System.Net.WebUtility.HtmlEncode(User.DisplayName())}. Email delivery is working.</p>",
                null, ct);
            TempData["Message"] = $"Test email sent to {string.Join(", ", sentTo)}.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Test email failed");
            TempData["Error"] = $"Test email failed: {ex.Message}";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var settings = await db.SmtpSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        if (settings is not null)
        {
            Input = settings;
            HasPassword = settings.ProtectedPassword is not null;
            Input.ProtectedPassword = null;
        }
    }
}
