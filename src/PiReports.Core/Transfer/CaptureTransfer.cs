using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PiReports.Core.Capture;
using PiReports.Core.Data;
using PiReports.Core.Jobs;
using PiReports.Core.Model;

namespace PiReports.Core.Transfer;

/// <summary>The file format for moving captures between installations, e.g. Test to Production.</summary>
public class CaptureExportFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string? ExportedFrom { get; set; }

    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<CaptureDefinition> Captures { get; set; } = [];
}

public record ImportResult(int Added, int Updated, IReadOnlyList<string> Errors);

public class CaptureTransfer(PiReportsDbContext db, CaptureScheduler scheduler)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<byte[]> ExportAsync(IReadOnlyCollection<int>? ids, string? exportedFrom, CancellationToken ct)
    {
        var query = db.CaptureDefinitions.AsNoTracking();
        if (ids is { Count: > 0 })
        {
            query = query.Where(d => ids.Contains(d.Id));
        }

        var file = new CaptureExportFile
        {
            ExportedFrom = exportedFrom,
            Captures = (await query.OrderBy(d => d.Name).ToListAsync(ct)).Select(ToPortable).ToList(),
        };
        return JsonSerializer.SerializeToUtf8Bytes(file, Json);
    }

    /// <summary>
    /// Adds captures that don't exist yet and updates those with the same name.
    /// Nothing is saved if any capture in the file is invalid.
    /// </summary>
    /// <param name="hostMap">Optional PI Vision host rename, e.g. ("pivision-test", "pivision01"), applied to display URLs.</param>
    public async Task<ImportResult> ImportAsync(Stream json, bool importDisabled, string? user, CancellationToken ct,
        (string From, string To)? hostMap = null)
    {
        CaptureExportFile? file;
        try
        {
            file = await JsonSerializer.DeserializeAsync<CaptureExportFile>(json, Json, ct);
        }
        catch (JsonException ex)
        {
            return new ImportResult(0, 0, [$"The file is not a PI Reports export: {ex.Message}"]);
        }

        if (file is null || file.Captures.Count == 0)
        {
            return new ImportResult(0, 0, ["The file contains no captures."]);
        }

        if (file.Version > CaptureExportFile.CurrentVersion)
        {
            return new ImportResult(0, 0, [$"The file is version {file.Version}; this installation reads up to {CaptureExportFile.CurrentVersion}. Upgrade it first."]);
        }

        var errors = new List<string>();
        foreach (var c in file.Captures)
        {
            // Hand-edited files may leave out fields the model treats as required.
            c.Name ??= "";
            c.DisplayUrl ??= "";
            c.TimeZoneId ??= TimeZoneInfo.Local.Id;
            c.FileNamePattern ??= "";
            if (hostMap is { } map)
            {
                c.DisplayUrl = ReplaceHost(c.DisplayUrl, map.From, map.To);
            }

            var label = string.IsNullOrWhiteSpace(c.Name) ? "(unnamed)" : c.Name;
            if (string.IsNullOrWhiteSpace(c.Name))
            {
                errors.Add("A capture has no name.");
            }

            try
            {
                PiVisionUrlBuilder.Build(c);
            }
            catch (ArgumentException)
            {
                errors.Add($"{label}: '{c.DisplayUrl}' is not a valid display URL.");
            }

            if (CaptureScheduler.ValidateCron(c.Cron) is { } cronError)
            {
                errors.Add($"{label}: schedule '{c.Cron}' is invalid ({cronError}).");
            }
        }

        var duplicates = file.Captures.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key);
        errors.AddRange(duplicates.Select(name => $"{name}: appears more than once in the file."));
        if (errors.Count > 0)
        {
            return new ImportResult(0, 0, errors);
        }

        var existing = await db.CaptureDefinitions.ToListAsync(ct);
        var byName = existing.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        int added = 0, updated = 0;
        var touched = new List<CaptureDefinition>();
        foreach (var incoming in file.Captures)
        {
            if (!byName.TryGetValue(incoming.Name.Trim(), out var target))
            {
                target = new CaptureDefinition();
                db.CaptureDefinitions.Add(target);
                added++;
            }
            else
            {
                updated++;
            }

            target.CopyEditableFieldsFrom(incoming);
            if (importDisabled)
            {
                target.Enabled = false;
            }

            if (!TimeZoneInfo.TryFindSystemTimeZoneById(target.TimeZoneId, out _))
            {
                target.TimeZoneId = TimeZoneInfo.Local.Id;
            }

            target.UpdatedAt = DateTimeOffset.UtcNow;
            target.UpdatedBy = user;
            touched.Add(target);
        }

        await db.SaveChangesAsync(ct);
        foreach (var d in touched)
        {
            scheduler.Sync(d);
        }

        return new ImportResult(added, updated, []);
    }

    public static string ReplaceHost(string url, string from, string to)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.Equals(uri.Host, from.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        // Rebuild by hand so the fragment and query keep their exact original text.
        var hostStart = url.IndexOf("://", StringComparison.Ordinal) + 3;
        var hostIndex = url.IndexOf(uri.Host, hostStart, StringComparison.OrdinalIgnoreCase);
        return url[..hostIndex] + to.Trim() + url[(hostIndex + uri.Host.Length)..];
    }

    private static CaptureDefinition ToPortable(CaptureDefinition d)
    {
        var copy = new CaptureDefinition();
        copy.CopyEditableFieldsFrom(d);
        return copy;
    }
}
