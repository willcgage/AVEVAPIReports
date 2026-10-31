using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PiReports.Core.Capture;

public static partial class FileNameBuilder
{
    // Windows rules regardless of the host OS, since files land on Windows shares.
    private static readonly char[] Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>Expands {name} and {date:format} tokens, then makes the result safe for a Windows file name.</summary>
    public static string Build(string pattern, string name, DateTimeOffset localTime, string extension)
    {
        var expanded = TokenRegex().Replace(string.IsNullOrWhiteSpace(pattern) ? "{name}_{date:yyyyMMdd_HHmm}" : pattern, m =>
        {
            var token = m.Groups["token"].Value;
            var format = m.Groups["format"].Success ? m.Groups["format"].Value : null;
            return token.ToLowerInvariant() switch
            {
                "name" => name,
                "date" => localTime.ToString(format ?? "yyyyMMdd_HHmm", CultureInfo.InvariantCulture),
                _ => m.Value,
            };
        });

        return Sanitize(expanded) + "." + extension.TrimStart('.');
    }

    public static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(char.IsControl(c) || Invalid.Contains(c) ? '_' : c);
        }

        var result = sb.ToString().Trim().TrimEnd('.');
        return result.Length == 0 ? "report" : result;
    }

    [GeneratedRegex(@"\{(?<token>[a-zA-Z]+)(:(?<format>[^}]+))?\}")]
    private static partial Regex TokenRegex();
}
