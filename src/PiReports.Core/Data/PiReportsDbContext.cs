using Microsoft.EntityFrameworkCore;
using PiReports.Core.Model;

namespace PiReports.Core.Data;

public class PiReportsDbContext(DbContextOptions<PiReportsDbContext> options) : DbContext(options)
{
    public const string Schema = "reports";

    public DbSet<CaptureDefinition> CaptureDefinitions => Set<CaptureDefinition>();

    public DbSet<ReportRun> ReportRuns => Set<ReportRun>();

    public DbSet<SmtpSettings> SmtpSettings => Set<SmtpSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<CaptureDefinition>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.DisplayUrl).HasMaxLength(2000).IsRequired();
            e.Property(x => x.StartTime).HasMaxLength(100);
            e.Property(x => x.EndTime).HasMaxLength(100);
            e.Property(x => x.Asset).HasMaxLength(1000);
            e.Property(x => x.Format).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Cron).HasMaxLength(100);
            e.Property(x => x.TimeZoneId).HasMaxLength(100);
            e.Property(x => x.EmailRecipients).HasMaxLength(4000);
            e.Property(x => x.EmailSubject).HasMaxLength(500);
            e.Property(x => x.SharePath).HasMaxLength(1000);
            e.Property(x => x.FileNamePattern).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
        });

        b.Entity<ReportRun>(e =>
        {
            e.HasOne(x => x.Definition).WithMany(d => d.Runs).HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DefinitionId, x.StartedAt });
            e.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RequestedBy).HasMaxLength(200);
            e.Property(x => x.OutputPath).HasMaxLength(1000);
            e.Property(x => x.FileName).HasMaxLength(300);
        });

        b.Entity<SmtpSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Host).HasMaxLength(300);
            e.Property(x => x.Security).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Username).HasMaxLength(300);
            e.Property(x => x.FromAddress).HasMaxLength(300);
            e.Property(x => x.FromName).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
            e.Ignore(x => x.IsConfigured);
        });
    }
}
