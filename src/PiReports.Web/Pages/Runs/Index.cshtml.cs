using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PiReports.Core.Data;
using PiReports.Core.Model;

namespace PiReports.Web.Pages.Runs;

public class IndexModel(PiReportsDbContext db) : PageModel
{
    public List<ReportRun> Runs { get; private set; } = [];

    public string? DefinitionName { get; private set; }

    public async Task OnGetAsync(int? definitionId, CancellationToken ct)
    {
        var query = db.ReportRuns.AsNoTracking().Include(r => r.Definition).AsQueryable();
        if (definitionId is not null)
        {
            query = query.Where(r => r.DefinitionId == definitionId);
            DefinitionName = await db.CaptureDefinitions.Where(d => d.Id == definitionId).Select(d => d.Name).SingleOrDefaultAsync(ct);
        }

        Runs = await query.OrderByDescending(r => r.StartedAt).Take(200).ToListAsync(ct);
    }
}
