using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiReports.Core.Data;

namespace PiReports.Core.Jobs;

/// <summary>Deletes archived output and run history older than RetentionDays.</summary>
public class RetentionJob(PiReportsDbContext db, IOptions<PiReportsOptions> options, ILogger<RetentionJob> logger)
{
    public const string JobId = "retention";

    public async Task RunAsync(CancellationToken ct)
    {
        var days = options.Value.RetentionDays;
        if (days <= 0)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var old = await db.ReportRuns.Where(r => r.StartedAt < cutoff).ToListAsync(ct);
        foreach (var run in old)
        {
            if (run.OutputPath is { Length: > 0 } path && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        db.ReportRuns.RemoveRange(old);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Retention removed {Count} runs older than {Days} days", old.Count, days);
    }
}
