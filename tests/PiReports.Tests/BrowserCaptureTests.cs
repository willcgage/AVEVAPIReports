using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiReports.Core;
using PiReports.Core.Capture;
using PiReports.Core.Model;

namespace PiReports.Tests;

/// <summary>Runs only when PIREPORTS_TEST_BROWSER points at a Chromium or Edge executable.</summary>
public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PIREPORTS_TEST_BROWSER")))
        {
            Skip = "Set PIREPORTS_TEST_BROWSER to a browser executable to run browser tests.";
        }
    }
}

public sealed class BrowserCaptureTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pireports-browser-" + Guid.NewGuid().ToString("N"));

    public BrowserCaptureTests() => Directory.CreateDirectory(_folder);

    private static PuppeteerScreenCapturer CreateCapturer() => new(
        Options.Create(new PiReportsOptions
        {
            BrowserExecutablePath = Environment.GetEnvironmentVariable("PIREPORTS_TEST_BROWSER"),
            IntegratedAuthAllowlist = "localhost",
            // Needed when the tests run as root in a Linux container.
            BrowserArgs = OperatingSystem.IsLinux() ? ["--no-sandbox"] : [],
        }),
        NullLogger<PuppeteerScreenCapturer>.Instance);

    private string Page(string name, string html)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, html);
        return new Uri(path).AbsoluteUri;
    }

    private static CaptureRequest Request(string url, CaptureFormat format) => new(url, 800, 600, format, true, 0, 30);

    [BrowserFact]
    public async Task Waits_for_the_display_to_finish_drawing()
    {
        var url = Page("display.html", """
            <html><body style="margin:0;background:#fff">
            <div id="v" style="font:40px sans-serif">0</div>
            <script>
              let n = 0;
              const t = setInterval(() => { document.getElementById('v').textContent = ++n; if (n === 6) clearInterval(t); }, 500);
            </script></body></html>
            """);

        var result = await CreateCapturer().CaptureAsync(Request(url, CaptureFormat.Png), CancellationToken.None);

        Assert.Equal("png", result.Extension);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], result.Content[..4]);
        Assert.Empty(result.Warnings);
    }

    [BrowserFact]
    public async Task Produces_a_pdf()
    {
        var url = Page("pdf.html", "<html><body><h1>Unit 3</h1></body></html>");

        var result = await CreateCapturer().CaptureAsync(Request(url, CaptureFormat.Pdf), CancellationToken.None);

        Assert.Equal("pdf", result.Extension);
        Assert.Equal("%PDF"u8.ToArray(), result.Content[..4]);
    }

    [BrowserFact]
    public async Task Fails_instead_of_capturing_a_sign_in_page()
    {
        var url = Page("signin.html", "<html><body><form><input type=password></form></body></html>");

        var ex = await Assert.ThrowsAsync<CaptureException>(() => CreateCapturer().CaptureAsync(Request(url, CaptureFormat.Png), CancellationToken.None));
        Assert.Contains("sign-in page", ex.Message);
    }

    [BrowserFact]
    public async Task Warns_when_the_display_never_settles()
    {
        var url = Page("live.html", """
            <html><body><div id="v"></div>
            <script>setInterval(() => document.getElementById('v').textContent = Math.random(), 200);</script>
            </body></html>
            """);

        var result = await CreateCapturer().CaptureAsync(Request(url, CaptureFormat.Png) with { TimeoutSeconds = 15 }, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Contains("kept changing"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
