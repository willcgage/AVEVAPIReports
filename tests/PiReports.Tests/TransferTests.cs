using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using PiReports.Core.Data;
using PiReports.Core.Delivery;
using PiReports.Core.Jobs;
using PiReports.Core.Model;
using PiReports.Core.Transfer;

namespace PiReports.Tests;

public class TransferTests
{
    private sealed class FakeRecurring : IRecurringJobManager
    {
        public HashSet<string> Ids { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options) => Ids.Add(recurringJobId);

        public void Trigger(string recurringJobId) { }

        public void RemoveIfExists(string recurringJobId) => Ids.Remove(recurringJobId);
    }

    private sealed class FakeJobs : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => "1";

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private static PiReportsDbContext NewDb() => new(new DbContextOptionsBuilder<PiReportsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CaptureDefinition Capture(string name, string? cron = "0 6 * * *") => new()
    {
        Name = name,
        DisplayUrl = "https://pivision-test/PIVision/#/Displays/12/X",
        Cron = cron,
        TimeZoneId = "UTC",
        EmailRecipients = "ops@corp.local",
    };

    [Fact]
    public async Task Export_then_import_adds_new_and_updates_existing_by_name()
    {
        using var source = NewDb();
        source.CaptureDefinitions.AddRange(Capture("Unit 3"), Capture("Unit 4"));
        await source.SaveChangesAsync();
        var json = await new CaptureTransfer(source, new CaptureScheduler(new FakeRecurring(), new FakeJobs())).ExportAsync(null, "TEST", CancellationToken.None);

        using var target = NewDb();
        var existing = Capture("unit 3", cron: null);
        existing.EmailRecipients = "old@corp.local";
        target.CaptureDefinitions.Add(existing);
        await target.SaveChangesAsync();

        var recurring = new FakeRecurring();
        var result = await new CaptureTransfer(target, new CaptureScheduler(recurring, new FakeJobs()))
            .ImportAsync(new MemoryStream(json), importDisabled: false, "will", CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Updated);
        var all = await target.CaptureDefinitions.OrderBy(d => d.Name).ToListAsync();
        Assert.Equal(["Unit 3", "Unit 4"], all.Select(d => d.Name));
        Assert.Equal("ops@corp.local", all[0].EmailRecipients);
        Assert.Equal(2, recurring.Ids.Count);
    }

    [Fact]
    public async Task Import_as_disabled_schedules_nothing()
    {
        using var source = NewDb();
        source.CaptureDefinitions.Add(Capture("Unit 3"));
        await source.SaveChangesAsync();
        var json = await new CaptureTransfer(source, new CaptureScheduler(new FakeRecurring(), new FakeJobs())).ExportAsync(null, null, CancellationToken.None);

        using var target = NewDb();
        var recurring = new FakeRecurring();
        await new CaptureTransfer(target, new CaptureScheduler(recurring, new FakeJobs()))
            .ImportAsync(new MemoryStream(json), importDisabled: true, null, CancellationToken.None);

        Assert.False((await target.CaptureDefinitions.SingleAsync()).Enabled);
        Assert.Empty(recurring.Ids);
    }

    [Theory]
    [InlineData("""{"captures":[{"name":"A","displayUrl":"not a url"}]}""", "not a valid display URL")]
    [InlineData("""{"captures":[{"name":"A","displayUrl":"https://x/PIVision/","cron":"nope"}]}""", "is invalid")]
    [InlineData("""{"captures":[{"name":"A","displayUrl":"https://x/"},{"name":"a","displayUrl":"https://x/"}]}""", "more than once")]
    [InlineData("""{"captures":[{"displayUrl":"https://x/"}]}""", "no name")]
    [InlineData("""{"captures":[]}""", "no captures")]
    [InlineData("""{"version":99,"captures":[{"name":"A","displayUrl":"https://x/"}]}""", "version 99")]
    [InlineData("hello", "not a PI Reports export")]
    public async Task Invalid_files_import_nothing(string json, string expectedError)
    {
        using var db = NewDb();
        var result = await new CaptureTransfer(db, new CaptureScheduler(new FakeRecurring(), new FakeJobs()))
            .ImportAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)), false, null, CancellationToken.None);

        Assert.Contains(result.Errors, e => e.Contains(expectedError));
        Assert.Empty(db.CaptureDefinitions);
    }

    [Theory]
    [InlineData("https://pivision-test/PIVision/#/Displays/12/X?mode=kiosk", "https://pivision01.corp.local/PIVision/#/Displays/12/X?mode=kiosk")]
    [InlineData("https://PIVISION-TEST:8443/PIVision/#/Displays/12", "https://pivision01.corp.local:8443/PIVision/#/Displays/12")]
    [InlineData("https://other/PIVision/#/Displays/12", "https://other/PIVision/#/Displays/12")]
    public void Host_rename_only_touches_the_matching_host(string url, string expected)
    {
        Assert.Equal(expected, CaptureTransfer.ReplaceHost(url, "pivision-test", "pivision01.corp.local"));
    }

    [Fact]
    public void Share_redirect_flattens_the_unc_path_under_the_root()
    {
        var path = ShareWriter.RedirectPath(Path.Combine("D:", "TestShares"), @"\\files\reports\Unit 3");
        Assert.Equal(Path.Combine("D:", "TestShares", "files_reports_Unit 3"), path);
    }
}
