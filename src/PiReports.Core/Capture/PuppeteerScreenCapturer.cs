using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PiReports.Core.Model;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace PiReports.Core.Capture;

/// <summary>
/// Captures a display with a headless Chromium-based browser (Edge on the server).
/// The browser runs as the service's own Windows identity, so PI Vision sees the
/// service account through Kerberos or NTLM with no stored password.
/// </summary>
public class PuppeteerScreenCapturer(IOptions<PiReportsOptions> options, ILogger<PuppeteerScreenCapturer> logger) : IScreenCapturer
{
    private static readonly string[] EdgeCandidates =
    [
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    ];

    private static readonly TimeSpan StabilityInterval = TimeSpan.FromSeconds(2);

    public async Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken ct)
    {
        var warnings = new List<string>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(request.TimeoutSeconds, 15));
        var allowlist = options.Value.IntegratedAuthAllowlist is { Length: > 0 } configured ? configured : new Uri(request.Url).Host;

        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = ResolveBrowserPath(),
            Args =
            [
                $"--auth-server-allowlist={allowlist}",
                $"--auth-negotiate-delegate-allowlist={allowlist}",
                "--hide-scrollbars",
                "--no-first-run",
                "--no-default-browser-check",
                .. options.Value.BrowserArgs,
            ],
        });

        await using var page = await browser.NewPageAsync();
        await page.SetViewportAsync(new ViewPortOptions { Width = request.ViewportWidth, Height = request.ViewportHeight });

        var consoleErrors = new List<string>();
        page.Console += (_, e) =>
        {
            if (e.Message.Type == ConsoleType.Error && consoleErrors.Count < 5)
            {
                consoleErrors.Add(e.Message.Text);
            }
        };

        IResponse? response = null;
        try
        {
            response = await page.GoToAsync(request.Url, new NavigationOptions
            {
                // PI Vision keeps a live connection open for updates, so the network never goes fully idle.
                WaitUntil = [WaitUntilNavigation.Networkidle2],
                Timeout = (int)Remaining(deadline).TotalMilliseconds,
            });
        }
        catch (NavigationException ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("The display was still loading data when the page-load wait ran out.");
        }

        if (response is { Status: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden })
        {
            throw new CaptureException(
                $"PI Vision returned {(int)response.Status}. The service account could not sign in or has no access to this display. " +
                "Check that the display is shared with the account, and that IntegratedAuthAllowlist covers the PI Vision host.");
        }

        await ThrowIfLoginPageAsync(page);

        await Task.Delay(TimeSpan.FromSeconds(Math.Max(request.SettleSeconds, 0)), ct);

        var png = await WaitForStableScreenshotAsync(page, deadline, warnings, ct);

        // A redirect to a sign-in page can happen after the first load, e.g. when a token expires.
        await ThrowIfLoginPageAsync(page);

        warnings.AddRange(consoleErrors.Select(e => $"Browser console error: {e}"));

        if (request.Format == CaptureFormat.Png)
        {
            return new CaptureResult(png, "png", "image/png", warnings);
        }

        var pdf = await RenderPdfAsync(browser, png, request.Landscape);
        return new CaptureResult(pdf, "pdf", "application/pdf", warnings);
    }

    private string ResolveBrowserPath()
    {
        if (options.Value.BrowserExecutablePath is { Length: > 0 } path)
        {
            return File.Exists(path) ? path : throw new CaptureException($"BrowserExecutablePath '{path}' does not exist.");
        }

        return EdgeCandidates.FirstOrDefault(File.Exists)
            ?? throw new CaptureException("No browser found. Set PiReports:BrowserExecutablePath to msedge.exe or chrome.exe.");
    }

    private static async Task ThrowIfLoginPageAsync(IPage page)
    {
        var url = page.Url;
        var looksLikeLogin = url.Contains("login", StringComparison.OrdinalIgnoreCase)
            || url.Contains("signin", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/adfs/", StringComparison.OrdinalIgnoreCase)
            || await page.QuerySelectorAsync("input[type=password]") is not null;

        if (looksLikeLogin)
        {
            throw new CaptureException(
                $"The browser landed on a sign-in page ({url}) instead of the display. " +
                "PI Vision must accept Windows authentication for the service account.");
        }
    }

    /// <summary>
    /// Takes screenshots until two in a row match, so symbols that are still drawing
    /// don't end up half-rendered in the report.
    /// </summary>
    private async Task<byte[]> WaitForStableScreenshotAsync(IPage page, DateTimeOffset deadline, List<string> warnings, CancellationToken ct)
    {
        var previous = await page.ScreenshotDataAsync(new ScreenshotOptions { Type = ScreenshotType.Png });
        var previousHash = SHA256.HashData(previous);

        while (Remaining(deadline) > StabilityInterval)
        {
            await Task.Delay(StabilityInterval, ct);
            var current = await page.ScreenshotDataAsync(new ScreenshotOptions { Type = ScreenshotType.Png });
            var currentHash = SHA256.HashData(current);
            if (currentHash.AsSpan().SequenceEqual(previousHash))
            {
                return current;
            }

            previous = current;
            previousHash = currentHash;
        }

        logger.LogWarning("Display did not settle before the capture timeout; using the last screenshot");
        warnings.Add("The display kept changing until the timeout (live values or animation). The last frame was used.");
        return previous;
    }

    private static async Task<byte[]> RenderPdfAsync(IBrowser browser, byte[] png, bool landscape)
    {
        await using var page = await browser.NewPageAsync();
        var html = $$"""
            <!doctype html>
            <html><head><style>
              @page { margin: 0.4in; }
              html, body { margin: 0; height: 100%; }
              body { display: flex; align-items: center; justify-content: center; }
              img { max-width: 100%; max-height: 100%; object-fit: contain; }
            </style></head>
            <body><img src="data:image/png;base64,{{Convert.ToBase64String(png)}}"></body></html>
            """;
        await page.SetContentAsync(html);
        return await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.Letter,
            Landscape = landscape,
            PrintBackground = true,
        });
    }

    private static TimeSpan Remaining(DateTimeOffset deadline)
    {
        var left = deadline - DateTimeOffset.UtcNow;
        return left > TimeSpan.Zero ? left : TimeSpan.FromMilliseconds(1);
    }
}
