using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using PiReports.Core.Capture;
using PiReports.Core.Data;
using PiReports.Core.Delivery;
using PiReports.Core.Jobs;
using PiReports.Core.Model;
using PiReports.Web.Infrastructure;

namespace PiReports.Web.Pages.Captures;

public class EditModel(PiReportsDbContext db, CaptureScheduler scheduler) : PageModel
{
    [BindProperty]
    public CaptureDefinition Input { get; set; } = new();

    public bool IsNew => Input.Id == 0;

    public string? PreviewUrl { get; private set; }

    public IEnumerable<SelectListItem> TimeZones => TimeZoneInfo.GetSystemTimeZones()
        .Select(tz => new SelectListItem(tz.DisplayName, tz.Id));

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct)
    {
        if (id is not null)
        {
            var existing = await db.CaptureDefinitions.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            if (existing is null)
            {
                return NotFound();
            }

            Input = existing;
            PreviewUrl = TryBuildUrl(Input);
        }

        return Page();
    }

    public Task<IActionResult> OnPostAsync(int? id, CancellationToken ct) => SaveAsync(id, runAfterSave: false, ct);

    public Task<IActionResult> OnPostSaveAndRunAsync(int? id, CancellationToken ct) => SaveAsync(id, runAfterSave: true, ct);

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        var existing = await db.CaptureDefinitions.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (existing is not null)
        {
            db.CaptureDefinitions.Remove(existing);
            await db.SaveChangesAsync(ct);
            scheduler.Remove(id);
            TempData["Message"] = $"Deleted \"{existing.Name}\".";
        }

        return RedirectToPage("/Index");
    }

    private async Task<IActionResult> SaveAsync(int? id, bool runAfterSave, CancellationToken ct)
    {
        // Navigation and audit fields are not posted by the form.
        ModelState.Remove($"{nameof(Input)}.{nameof(CaptureDefinition.Runs)}");
        Validate(Input);
        if (await db.CaptureDefinitions.AnyAsync(d => d.Name == Input.Name && d.Id != (id ?? 0), ct))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Name)}", "Another capture already has this name.");
        }

        if (!ModelState.IsValid)
        {
            Input.Id = id ?? 0;
            return Page();
        }

        CaptureDefinition target;
        if (id is null)
        {
            target = new CaptureDefinition();
            db.CaptureDefinitions.Add(target);
        }
        else
        {
            target = await db.CaptureDefinitions.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new BadHttpRequestException("Not found", 404);
        }

        target.CopyEditableFieldsFrom(Input);
        target.UpdatedAt = DateTimeOffset.UtcNow;
        target.UpdatedBy = User.DisplayName();
        await db.SaveChangesAsync(ct);

        scheduler.Sync(target);
        if (runAfterSave)
        {
            scheduler.RunNow(target.Id, User.DisplayName());
            TempData["Message"] = $"Saved \"{target.Name}\" and queued a run.";
        }
        else
        {
            TempData["Message"] = $"Saved \"{target.Name}\".";
        }

        return RedirectToPage("/Index");
    }

    private void Validate(CaptureDefinition d)
    {
        if (TryBuildUrl(d) is null)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(d.DisplayUrl)}", "Enter the full http(s) address of the display.");
        }

        if (CaptureScheduler.ValidateCron(d.Cron) is { } cronError)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(d.Cron)}", cronError);
        }

        foreach (var address in EmailSender.ParseRecipients(d.EmailRecipients))
        {
            if (!MailboxAddress.TryParse(address, out _) || !address.Contains('@'))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(d.EmailRecipients)}", $"'{address}' is not an email address.");
            }
        }
    }

    private static string? TryBuildUrl(CaptureDefinition d)
    {
        try
        {
            return string.IsNullOrWhiteSpace(d.DisplayUrl) ? null : PiVisionUrlBuilder.Build(d);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
