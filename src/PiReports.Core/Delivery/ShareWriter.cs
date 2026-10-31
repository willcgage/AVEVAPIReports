namespace PiReports.Core.Delivery;

public static class ShareWriter
{
    /// <summary>
    /// Maps a capture's shared folder into a redirect root, e.g. \\files\reports\Unit 3
    /// under D:\TestShares becomes D:\TestShares\files_reports_Unit 3.
    /// </summary>
    public static string RedirectPath(string redirectRoot, string original)
    {
        var flattened = original.Trim().TrimStart('\\', '/').Replace('\\', '_').Replace('/', '_');
        return Path.Combine(redirectRoot, Capture.FileNameBuilder.Sanitize(flattened));
    }

    /// <summary>
    /// Writes to a hidden temp name, then renames, so anyone watching the folder
    /// never opens a half-written file.
    /// </summary>
    public static async Task<string> WriteAtomicAsync(string folder, string fileName, byte[] content, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var final = Path.Combine(folder, fileName);
        var temp = Path.Combine(folder, $".{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, content, ct);
            File.Move(temp, final, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return final;
    }
}
