using PiReports.Core.Model;

namespace PiReports.Core.Capture;

public record CaptureRequest(
    string Url,
    int ViewportWidth,
    int ViewportHeight,
    CaptureFormat Format,
    bool Landscape,
    int SettleSeconds,
    int TimeoutSeconds);

public record CaptureResult(byte[] Content, string Extension, string ContentType, IReadOnlyList<string> Warnings);

/// <summary>Thrown when the capture produced nothing trustworthy, e.g. a login page instead of the display.</summary>
public class CaptureException(string message, Exception? inner = null) : Exception(message, inner);

public interface IScreenCapturer
{
    Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken ct);
}
