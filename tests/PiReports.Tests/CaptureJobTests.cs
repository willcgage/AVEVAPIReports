using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiReports.Core;
using PiReports.Core.Capture;
using PiReports.Core.Data;
using PiReports.Core.Delivery;
using PiReports.Core.Jobs;
using PiReports.Core.Model;

namespace PiReports.Tests;

public sealed class CaptureJobTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pireports-job-" + Guid.NewGuid().ToString("N"));
    private readonly PiReportsDbContext _db = new(new DbContextOptionsBuilder<PiReportsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeCapturer(Func<CaptureRequest, CaptureResult> capture) : IScreenCapturer
    {
        public CaptureRequest? LastRequest { get; private set; }

        public Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(capture(request));
        }
    }

    private CaptureJob CreateJob(IScreenCapturer capturer) => new(
        _db,
        capturer,
        new EmailSender(_db, new EphemeralDataProtectionProvider(), Options.Create(new PiReportsOptions())),
        Options.Create(new PiReportsOptions { OutputRoot = Path.Combine(_root, "archive") }),
        NullLogger<CaptureJob>.Instance);

    private async Task<CaptureDefinition> AddDefinitionAsync(Action<CaptureDefinition>? configure = null)
    {
        var d = new CaptureDefinition
        {
            Name = "Unit 3 overview",
            DisplayUrl = "https://pivision/PIVision/#/Displays/12/Unit-3",
            StartTime = "*-8h",
            EndTime = "*",
            TimeZoneId = "UTC",
            FileNamePattern = "{name}",
        };
        configure?.Invoke(d);
        _db.CaptureDefinitions.Add(d);
        await _db.SaveChangesAsync();
        return d;
    }

    [Fact]
    public async Task Successful_run_archives_and_copies_to_share()
    {
        var share = Path.Combine(_root, "share");
        var d = await AddDefinitionAsync(x => x.SharePath = share);
        var capturer = new FakeCapturer(_ => new CaptureResult([1, 2, 3], "pdf", "application/pdf", ["minor"]));

        await CreateJob(capturer).RunAsync(d.Id, RunTrigger.Manual, "tester", CancellationToken.None);

        var run = await _db.ReportRuns.SingleAsync();
        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal("Unit 3 overview.pdf", run.FileName);
        Assert.Equal("minor", run.Warnings);
        Assert.True(File.Exists(run.OutputPath));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(share, "Unit 3 overview.pdf")));
        Assert.Contains("mode=kiosk", capturer.LastRequest!.Url);
        Assert.Contains("StartTime=%2A-8h", capturer.LastRequest.Url);
    }

    [Fact]
    public async Task Failed_capture_is_recorded_and_rethrown_for_retry()
    {
        var d = await AddDefinitionAsync();
        var capturer = new FakeCapturer(_ => throw new CaptureException("login page"));

        await Assert.ThrowsAsync<CaptureException>(() => CreateJob(capturer).RunAsync(d.Id, RunTrigger.Schedule, null, CancellationToken.None));

        var run = await _db.ReportRuns.SingleAsync();
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal("login page", run.Error);
        Assert.NotNull(run.FinishedAt);
    }

    [Fact]
    public async Task Email_failure_marks_run_failed_without_throwing()
    {
        // No SMTP settings saved, so sending fails.
        var d = await AddDefinitionAsync(x => x.EmailRecipients = "ops@corp.local");
        var capturer = new FakeCapturer(_ => new CaptureResult([1], "png", "image/png", []));

        await CreateJob(capturer).RunAsync(d.Id, RunTrigger.Schedule, null, CancellationToken.None);

        var run = await _db.ReportRuns.SingleAsync();
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("SMTP is not configured", run.Error);
        Assert.True(File.Exists(run.OutputPath));
    }

    [Fact]
    public async Task Missing_definition_is_skipped()
    {
        await CreateJob(new FakeCapturer(_ => throw new InvalidOperationException())).RunAsync(999, RunTrigger.Schedule, null, CancellationToken.None);
        Assert.Empty(_db.ReportRuns);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
