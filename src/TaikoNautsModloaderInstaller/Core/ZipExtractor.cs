using System.IO.Compression;

namespace TaikoNautsModloaderInstaller.Core;

internal static class ZipExtractor
{
    /// <summary>Extracts every file into the directory, overwriting existing ones. Entries that would
    /// land outside the directory are rejected.</summary>
    public static int ExtractOverwriting(string zipPath, string directory)
    {
        string root = Path.GetFullPath(directory);
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        int count = 0;

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destination = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InstallException(
                    $"The package contains an unsafe path: {entry.FullName}",
                    $"パッケージに安全でないパスが含まれています: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            count++;
        }

        return count;
    }
}
