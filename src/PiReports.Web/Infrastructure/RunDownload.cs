using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PiReports.Core;
using PiReports.Core.Data;

namespace PiReports.Web.Infrastructure;

public static class RunDownload
{
    public static async Task<IResult> HandleAsync(long id, PiReportsDbContext db, IOptions<PiReportsOptions> options, CancellationToken ct)
    {
        var run = await db.ReportRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (run?.OutputPath is not { Length: > 0 } path || !File.Exists(path))
        {
            return Results.NotFound();
        }

        // Only serve files from the archive, whatever the database says.
        var root = Path.GetFullPath(options.Value.OutputRoot);
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        var contentType = Path.GetExtension(full).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "application/pdf";
        return Results.File(full, contentType, run.FileName ?? Path.GetFileName(full));
    }
}
