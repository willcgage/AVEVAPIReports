using System.Net;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiReports.Core.Capture;
using PiReports.Core.Data;
using PiReports.Core.Delivery;
using PiReports.Core.Model;

namespace PiReports.Core.Jobs;

/// <summary>One run of one capture definition: capture, archive, deliver, record.</summary>
public class CaptureJob(
    PiReportsDbContext db,
    IScreenCapturer capturer,
    EmailSender email,
    IOptions<PiReportsOptions> options,
    ILogger<CaptureJob> logger)
{
    // A failed capture is retried. A failed delivery is not, because a retry would
    // re-send to everyone who already got the email; it is recorded on the run instead.
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [60, 300])]
    [JobDisplayName("Capture {0} ({1})")]
    public async Task RunAsync(int definitionId, RunTrigger trigger, string? requestedBy, CancellationToken ct)
    {
        var definition = await db.CaptureDefinitions.SingleOrDefaultAsync(d => d.Id == definitionId, ct);
        if (definition is null)
        {
            logger.LogWarning("Capture definition {Id} no longer exists; skipping", definitionId);
            return;
        }

        var run = new ReportRun { DefinitionId = definitionId, Trigger = trigger, RequestedBy = requestedBy };
        db.ReportRuns.Add(run);
        await db.SaveChangesAsync(ct);

        CaptureResult result;
        try
        {
            var url = PiVisionUrlBuilder.Build(definition);
            logger.LogInformation("Capturing {Name} from {Url}", definition.Name, url);
            result = await capturer.CaptureAsync(
                new CaptureRequest(url, definition.ViewportWidth, definition.ViewportHeight, definition.Format,
                    definition.Landscape, definition.SettleSeconds, definition.TimeoutSeconds),
                ct);
        }
        catch (Exception ex)
        {
            await FinishAsync(run, RunStatus.Failed, ex.Message, null, ct);
            throw;
        }

        var warnings = result.Warnings.ToList();
        var errors = new List<string>();

        var localNow = TimeZoneInfo.ConvertTime(run.StartedAt, CaptureScheduler.ResolveTimeZone(definition.TimeZoneId));
        var fileName = FileNameBuilder.Build(definition.FileNamePattern, definition.Name, localNow, result.Extension);

        var archiveFolder = Path.Combine(options.Value.OutputRoot, definition.Id.ToString(), localNow.ToString("yyyy-MM"));
        run.OutputPath = await ShareWriter.WriteAtomicAsync(archiveFolder, $"{run.Id}_{fileName}", result.Content, ct);
        run.FileName = fileName;
        run.OutputBytes = result.Content.LongLength;

        if (!string.IsNullOrWhiteSpace(definition.SharePath))
        {
            var folder = options.Value.ShareRedirectRoot is { Length: > 0 } redirectRoot
                ? ShareWriter.RedirectPath(redirectRoot, definition.SharePath)
                : definition.SharePath.Trim();
            try
            {
                await ShareWriter.WriteAtomicAsync(folder, fileName, result.Content, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Share delivery failed for {Name}", definition.Name);
                errors.Add($"Share delivery to {folder} failed: {ex.Message}");
            }
        }

        var recipients = EmailSender.ParseRecipients(definition.EmailRecipients);
        if (recipients.Count > 0)
        {
            try
            {
                await SendEmailAsync(definition, run, result, fileName, localNow, recipients, warnings, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Email delivery failed for {Name}", definition.Name);
                errors.Add($"Email delivery failed: {ex.Message}");
            }
        }

        run.Warnings = warnings.Count > 0 ? string.Join(Environment.NewLine, warnings) : null;
        await FinishAsync(run, errors.Count > 0 ? RunStatus.Failed : RunStatus.Succeeded,
            errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null, run.Warnings, ct);
    }

    private async Task SendEmailAsync(CaptureDefinition d, ReportRun run, CaptureResult result, string fileName,
        DateTimeOffset localNow, IReadOnlyList<string> recipients, List<string> warnings, CancellationToken ct)
    {
        var subject = string.IsNullOrWhiteSpace(d.EmailSubject)
            ? $"{d.Name} — {localNow:yyyy-MM-dd HH:mm}"
            : d.EmailSubject.Replace("{date}", localNow.ToString("yyyy-MM-dd HH:mm"));

        var attach = result.Content.LongLength <= d.AttachMaxMb * 1024L * 1024L;
        var link = options.Value.PortalBaseUrl is { Length: > 0 } baseUrl
            ? $"{baseUrl.TrimEnd('/')}/runs/{run.Id}/download"
            : null;

        if (!attach && link is null)
        {
            warnings.Add("The file was over the attachment limit and PortalBaseUrl is not set, so it was attached anyway.");
            attach = true;
        }

        var range = string.IsNullOrWhiteSpace(d.StartTime) && string.IsNullOrWhiteSpace(d.EndTime)
            ? "the display's default time range"
            : $"{WebUtility.HtmlEncode(d.StartTime ?? "")} to {WebUtility.HtmlEncode(d.EndTime ?? "")}";

        var body = $"""
            <p><strong>{WebUtility.HtmlEncode(d.Name)}</strong>, captured {localNow:yyyy-MM-dd HH:mm} for {range}.</p>
            {(attach ? "<p>The report is attached.</p>" : $"<p>The report is too large to attach. <a href=\"{link}\">Download it from the report portal</a>.</p>")}
            {(d.Description is { Length: > 0 } desc ? $"<p>{WebUtility.HtmlEncode(desc)}</p>" : "")}
            """;

        await email.SendAsync(recipients, subject, body,
            attach ? new EmailAttachment(fileName, result.ContentType, result.Content) : null, ct);
    }

    private async Task FinishAsync(ReportRun run, RunStatus status, string? error, string? warnings, CancellationToken ct)
    {
        run.Status = status;
        run.Error = error;
        run.Warnings = warnings ?? run.Warnings;
        run.FinishedAt = DateTimeOffset.UtcNow;
        // The capture may have been cancelled; still record how the run ended.
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
