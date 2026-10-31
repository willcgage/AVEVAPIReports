using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PiReports.Core.Data;
using PiReports.Core.Jobs;
using PiReports.Core.Model;
using PiReports.Web.Infrastructure;

namespace PiReports.Web.Pages;

public class IndexModel(PiReportsDbContext db, CaptureScheduler scheduler, IAuthorizationService authorization) : PageModel
{
    public record Row(CaptureDefinition Definition, ReportRun? LastRun, DateTimeOffset? NextRun);

    public List<Row> Rows { get; private set; } = [];

    public bool IsAdmin { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        IsAdmin = (await authorization.AuthorizeAsync(User, Policies.Admin)).Succeeded;

        var definitions = await db.CaptureDefinitions.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
        var lastRuns = await db.ReportRuns.AsNoTracking()
            .GroupBy(r => r.DefinitionId)
            .Select(g => g.OrderByDescending(r => r.StartedAt).First())
            .ToListAsync(ct);
        var byDefinition = lastRuns.ToDictionary(r => r.DefinitionId);

        var now = DateTimeOffset.UtcNow;
        Rows = definitions
            .Select(d => new Row(d, byDefinition.GetValueOrDefault(d.Id), CaptureScheduler.NextOccurrence(d, now)?.ToLocalTime()))
            .ToList();
    }

    public async Task<IActionResult> OnPostRunNowAsync(int id, CancellationToken ct)
    {
        if (!(await authorization.AuthorizeAsync(User, Policies.Admin)).Succeeded)
        {
            return Forbid();
        }

        var definition = await db.CaptureDefinitions.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
        {
            return NotFound();
        }

        scheduler.RunNow(id, User.DisplayName());
        TempData["Message"] = $"\"{definition.Name}\" is queued. It will appear in the run history when it finishes.";
        return RedirectToPage();
    }
}
