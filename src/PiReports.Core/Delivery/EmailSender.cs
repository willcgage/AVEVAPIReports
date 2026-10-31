using MailKit.Net.Smtp;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using PiReports.Core.Data;
using PiReports.Core.Model;

namespace PiReports.Core.Delivery;

public record EmailAttachment(string FileName, string ContentType, byte[] Content);

public class EmailSender(PiReportsDbContext db, IDataProtectionProvider protection, IOptions<PiReportsOptions> options)
{
    public const string ProtectorPurpose = "PiReports.SmtpPassword";

    /// <summary>Sends the message and returns who it actually went to, which differs on a redirected (Test) install.</summary>
    public async Task<IReadOnlyList<string>> SendAsync(IReadOnlyCollection<string> to, string subject, string htmlBody, EmailAttachment? attachment, CancellationToken ct)
    {
        var settings = await db.SmtpSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        if (settings is not { IsConfigured: true })
        {
            throw new InvalidOperationException("SMTP is not configured. Fill in Settings > Email in the portal.");
        }

        var o = options.Value;
        if (o.EnvironmentLabel is { Length: > 0 } label)
        {
            subject = $"[{label}] {subject}";
        }

        if (o.EmailRedirectTo is { Length: > 0 } redirect)
        {
            htmlBody = $"""
                <p style="padding:8px;background:#fff4ce;border:1px solid #e0c060">
                {WebUtility.HtmlEncode(o.EnvironmentLabel ?? "Redirected")}: this email was redirected.
                It would have gone to {WebUtility.HtmlEncode(string.Join(", ", to))}.</p>
                """ + htmlBody;
            to = ParseRecipients(redirect);
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        foreach (var address in to)
        {
            message.To.Add(MailboxAddress.Parse(address));
        }

        message.Subject = subject;
        var body = new BodyBuilder { HtmlBody = htmlBody };
        if (attachment is not null)
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, ct);
        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            var password = settings.ProtectedPassword is { Length: > 0 } p
                ? protection.CreateProtector(ProtectorPurpose).Unprotect(p)
                : "";
            await client.AuthenticateAsync(settings.Username, password, ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        return to.ToList();
    }

    public static IReadOnlyList<string> ParseRecipients(string? recipients) =>
        (recipients ?? "")
            .Split([';', ',', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
