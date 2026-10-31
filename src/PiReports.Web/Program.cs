using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PiReports.Core;
using PiReports.Core.Data;
using PiReports.Core.Jobs;
using PiReports.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "PiReports");

var connectionString = builder.Configuration.GetConnectionString("PiReports")
    ?? throw new InvalidOperationException("ConnectionStrings:PiReports is not set.");
var reportOptions = builder.Configuration.GetSection(PiReportsOptions.Section).Get<PiReportsOptions>() ?? new();

builder.Services.Configure<PiReportsOptions>(builder.Configuration.GetSection(PiReportsOptions.Section));
builder.Services.AddDbContext<PiReportsDbContext>(o => o.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PiReportsDbContext.Schema)));
builder.Services.AddPiReportsCore();

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("PiReports");
if (reportOptions.DataProtectionKeysPath is { Length: > 0 } keysPath)
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    if (OperatingSystem.IsWindows())
    {
        dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
    }
}

// Windows sign-in. Under IIS this defers to IIS's own Windows Authentication.
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = o.DefaultPolicy;
    o.AddPolicy(Policies.Admin, p =>
    {
        p.RequireAuthenticatedUser();
        if (reportOptions.AdminGroup is { Length: > 0 } group)
        {
            p.RequireRole(group);
        }
    });
});

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Settings", Policies.Admin);
    o.Conventions.AuthorizePage("/Captures/Edit", Policies.Admin);
    o.Conventions.AuthorizePage("/Captures/Transfer", Policies.Admin);
});

builder.Services.AddHangfire(c => c
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        SchemaName = "HangFire",
        PrepareSchemaIfNecessary = reportOptions.ApplyMigrationsOnStartup,
        // Captures don't need sub-second pickup; polling gently keeps load on the AF SQL instance low.
        QueuePollInterval = TimeSpan.FromSeconds(15),
    }));

// One worker: captures run one at a time so they don't compete with people using PI Vision.
builder.Services.AddHangfireServer(o =>
{
    o.Queues = [CaptureScheduler.Queue, "default"];
    o.WorkerCount = 1;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PiReportsDbContext>();
    if (reportOptions.ApplyMigrationsOnStartup)
    {
        db.Database.Migrate();
    }

    var scheduler = scope.ServiceProvider.GetRequiredService<CaptureScheduler>();
    foreach (var definition in db.CaptureDefinitions.AsNoTracking().ToList())
    {
        scheduler.Sync(definition);
    }

    scope.ServiceProvider.GetRequiredService<IRecurringJobManager>()
        .AddOrUpdate<RetentionJob>(RetentionJob.JobId, j => j.RunAsync(CancellationToken.None), "30 2 * * *");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapHangfireDashboard("/jobs", new DashboardOptions
{
    // Access is enforced by the endpoint policy below instead of Hangfire's local-only default.
    Authorization = [],
    DashboardTitle = "PI Reports jobs",
}).RequireAuthorization(Policies.Admin);

app.MapGet("/runs/{id:long}/download", RunDownload.HandleAsync);

app.Run();
