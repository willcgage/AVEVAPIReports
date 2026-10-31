using Cronos;
using Hangfire;
using PiReports.Core.Model;

namespace PiReports.Core.Jobs;

/// <summary>Keeps Hangfire's recurring jobs in step with the capture definitions.</summary>
public class CaptureScheduler(IRecurringJobManager recurring, IBackgroundJobClient jobs)
{
    public const string Queue = "capture";

    public static string JobId(int definitionId) => $"capture-{definitionId}";

    public void Sync(CaptureDefinition d)
    {
        if (!d.Enabled || string.IsNullOrWhiteSpace(d.Cron))
        {
            recurring.RemoveIfExists(JobId(d.Id));
            return;
        }

        recurring.AddOrUpdate<CaptureJob>(
            JobId(d.Id),
            Queue,
            job => job.RunAsync(d.Id, RunTrigger.Schedule, null, CancellationToken.None),
            d.Cron.Trim(),
            new RecurringJobOptions { TimeZone = ResolveTimeZone(d.TimeZoneId) });
    }

    public void Remove(int definitionId) => recurring.RemoveIfExists(JobId(definitionId));

    public string RunNow(int definitionId, string? requestedBy) =>
        jobs.Enqueue<CaptureJob>(Queue, job => job.RunAsync(definitionId, RunTrigger.Manual, requestedBy, CancellationToken.None));

    /// <summary>Returns an error message, or null when the expression is valid.</summary>
    public static string? ValidateCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            return null;
        }

        try
        {
            CronExpression.Parse(cron.Trim());
            return null;
        }
        catch (CronFormatException ex)
        {
            return ex.Message;
        }
    }

    public static DateTimeOffset? NextOccurrence(CaptureDefinition d, DateTimeOffset from)
    {
        if (!d.Enabled || ValidateCron(d.Cron) is not null || string.IsNullOrWhiteSpace(d.Cron))
        {
            return null;
        }

        return CronExpression.Parse(d.Cron.Trim()).GetNextOccurrence(from, ResolveTimeZone(d.TimeZoneId));
    }

    public static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Local;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) ? tz : TimeZoneInfo.Local;
    }
}
