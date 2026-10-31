using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PiReports.Core;
using PiReports.Core.Data;
using PiReports.Core.Model;
using PiReports.Core.Transfer;
using PiReports.Web.Infrastructure;

namespace PiReports.Web.Pages.Captures;

public class TransferModel(PiReportsDbContext db, CaptureTransfer transfer, IOptions<PiReportsOptions> options) : PageModel
{
    public List<CaptureDefinition> Captures { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Captures = await db.CaptureDefinitions.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostExportAsync(int[] ids, CancellationToken ct)
    {
        var from = options.Value.EnvironmentLabel is { Length: > 0 } label ? $"{label} ({Environment.MachineName})" : Environment.MachineName;
        var json = await transfer.ExportAsync(ids, from, ct);
        var prefix = options.Value.EnvironmentLabel is { Length: > 0 } l ? $"{l.ToLowerInvariant()}-" : "";
        return File(json, "application/json", $"pi-reports-captures-{prefix}{DateTime.Now:yyyyMMdd-HHmm}.json");
    }

    public async Task<IActionResult> OnPostImportAsync(IFormFile? file, bool importDisabled, string? hostFrom, string? hostTo, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose an export file to import.";
            return RedirectToPage();
        }

        await using var stream = file.OpenReadStream();
        (string, string)? hostMap = !string.IsNullOrWhiteSpace(hostFrom) && !string.IsNullOrWhiteSpace(hostTo) ? (hostFrom, hostTo) : null;
        var result = await transfer.ImportAsync(stream, importDisabled, User.DisplayName(), ct, hostMap);
        if (result.Errors.Count > 0)
        {
            TempData["Error"] = "Nothing was imported. " + string.Join(" ", result.Errors);
            return RedirectToPage();
        }

        TempData["Message"] = $"Imported {result.Added} new and updated {result.Updated} existing captures" +
            (importDisabled ? ". They are disabled until you enable them." : ".");
        return RedirectToPage("/Index");
    }
}
