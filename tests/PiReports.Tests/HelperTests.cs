using PiReports.Core.Capture;
using PiReports.Core.Delivery;
using PiReports.Core.Jobs;

namespace PiReports.Tests;

public class HelperTests
{
    private static readonly DateTimeOffset When = new(2026, 10, 27, 6, 5, 0, TimeSpan.Zero);

    [Fact]
    public void File_name_expands_tokens()
    {
        Assert.Equal("Unit 3_20261027_0605.pdf", FileNameBuilder.Build("{name}_{date:yyyyMMdd_HHmm}", "Unit 3", When, "pdf"));
        Assert.Equal("daily-2026-10-27.png", FileNameBuilder.Build("daily-{date:yyyy-MM-dd}", "x", When, ".png"));
    }

    [Fact]
    public void File_name_replaces_characters_windows_rejects()
    {
        Assert.Equal("A_B_ C_.pdf", FileNameBuilder.Build("{name}", "A/B: C?", When, "pdf"));
        Assert.Equal("report.pdf", FileNameBuilder.Build("{name}", "...", When, "pdf"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("0 6 * * *", true)]
    [InlineData("0 6 * * 1-5", true)]
    [InlineData("*/15 * * * *", true)]
    [InlineData("0 25 * * *", false)]
    [InlineData("every day", false)]
    public void Cron_validation(string? cron, bool valid)
    {
        Assert.Equal(valid, CaptureScheduler.ValidateCron(cron) is null);
    }

    [Fact]
    public void Recipients_split_on_common_separators_and_dedupe()
    {
        var list = EmailSender.ParseRecipients("a@x.com; b@x.com,\nA@x.com  c@x.com");
        Assert.Equal(["a@x.com", "b@x.com", "c@x.com"], list);
    }

    [Fact]
    public async Task Share_writer_replaces_existing_file_and_leaves_no_temp_files()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pireports-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await ShareWriter.WriteAtomicAsync(folder, "r.pdf", [1, 2, 3], CancellationToken.None);
            var path = await ShareWriter.WriteAtomicAsync(folder, "r.pdf", [4, 5], CancellationToken.None);

            Assert.Equal([4, 5], await File.ReadAllBytesAsync(path));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
