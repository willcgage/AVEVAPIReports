using System.Text;
using PiReports.Core.Model;

namespace PiReports.Core.Capture;

/// <summary>
/// Turns a display URL copied from the browser into a kiosk-mode URL with the
/// definition's time range and asset applied.
/// </summary>
/// <remarks>
/// PI Vision routes inside the hash fragment (https://host/PIVision/#/Displays/12/Name?mode=kiosk),
/// so the query string usually lives after the '#'. Both forms are handled.
/// </remarks>
public static class PiVisionUrlBuilder
{
    public static string Build(CaptureDefinition d)
    {
        var parameters = new List<KeyValuePair<string, string?>>
        {
            new("mode", "kiosk"),
            new("HideToolbar", d.HideToolbar ? "true" : null),
            new("HideTimebar", d.HideTimebar ? "true" : null),
            new("StartTime", Blank(d.StartTime)),
            new("EndTime", Blank(d.EndTime)),
            new("Asset", Blank(d.Asset)),
        };
        return Apply(d.DisplayUrl, parameters);
    }

    /// <summary>Sets each parameter, replacing any existing one of the same name. A null value removes it.</summary>
    public static string Apply(string url, IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException($"'{url}' is not an absolute http(s) URL.", nameof(url));
        }

        var raw = uri.OriginalString.Trim();
        var hashIndex = raw.IndexOf('#');
        var routesInFragment = hashIndex >= 0 && raw.AsSpan(hashIndex + 1).StartsWith("/");

        string prefix, target, suffix;
        if (routesInFragment)
        {
            prefix = raw[..(hashIndex + 1)];
            target = raw[(hashIndex + 1)..];
            suffix = "";
        }
        else
        {
            prefix = "";
            target = hashIndex >= 0 ? raw[..hashIndex] : raw;
            suffix = hashIndex >= 0 ? raw[hashIndex..] : "";
        }

        var queryIndex = target.IndexOf('?');
        var path = queryIndex >= 0 ? target[..queryIndex] : target;
        var existing = queryIndex >= 0 ? ParseQuery(target[(queryIndex + 1)..]) : [];

        foreach (var (key, value) in parameters)
        {
            existing.RemoveAll(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            if (value is not null)
            {
                existing.Add(new(key, value));
            }
        }

        var query = new StringBuilder();
        foreach (var (key, value) in existing)
        {
            query.Append(query.Length == 0 ? '?' : '&')
                .Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        return prefix + path + query + suffix;
    }

    private static List<KeyValuePair<string, string>> ParseQuery(string query) =>
        query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var eq = pair.IndexOf('=');
                var key = eq >= 0 ? pair[..eq] : pair;
                var value = eq >= 0 ? pair[(eq + 1)..] : "";
                return new KeyValuePair<string, string>(Uri.UnescapeDataString(key), Uri.UnescapeDataString(value.Replace('+', ' ')));
            })
            .ToList();

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
