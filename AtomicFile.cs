using System.Text;

namespace RouterTray;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents, bool createBackup)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
                        throw new InvalidOperationException("Settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, contents + Environment.NewLine, new UTF8Encoding(false));

            if (createBackup && File.Exists(fullPath))
            {
                File.Copy(fullPath, fullPath + ".bak", true);
            }

            File.Move(tempPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
