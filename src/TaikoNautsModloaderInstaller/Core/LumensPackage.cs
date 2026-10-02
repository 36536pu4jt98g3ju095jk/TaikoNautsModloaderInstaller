using System.IO.Compression;
using System.Text.RegularExpressions;

namespace TaikoNautsModloaderInstaller.Core;

/// <summary>One NULM pack found in a ZIP: a folder X holding X.nulm and its X_n.png atlases.</summary>
internal sealed record LumensPack(string Name, string Prefix, IReadOnlyList<ZipArchiveEntry> Files)
{
    private static readonly string[] KnownPrefixes = { "donbg_", "bg_nomal_", "bg_normal_", "bg_fever_", "bg_dai_" };

    /// <summary>The mod only uses packs whose names start with one of the known prefixes.</summary>
    public bool IsKnownKind => KnownPrefixes.Any(prefix => Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Finds NULM packs in a ZIP and installs them into a skin's Lumens folder.</summary>
internal static partial class LumensPackage
{
    private const long MaxTotalBytes = 1L << 30;
    private static readonly string[] AllowedExtensions = { ".nulm", ".png" };

    [GeneratedRegex("^[A-Za-z0-9._-]{1,63}$")]
    private static partial Regex SafeName();

    /// <summary>Opens the ZIP and lists its packs. The caller disposes the archive.</summary>
    public static IReadOnlyList<LumensPack> Find(ZipArchive archive)
    {
        var packs = new List<LumensPack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ZipArchiveEntry nulm in archive.Entries.Where(entry =>
                     entry.FullName.EndsWith(".nulm", StringComparison.OrdinalIgnoreCase)))
        {
            string path = nulm.FullName.Replace('\\', '/');
            int slash = path.LastIndexOf('/');
            string prefix = slash < 0 ? string.Empty : path[..(slash + 1)];
            string file = path[(slash + 1)..];
            string baseName = Path.GetFileNameWithoutExtension(file);

            // a folder named like the file (X/X.nulm), or a flat ZIP with X.nulm at the top
            string folderName = prefix.TrimEnd('/');
            folderName = folderName[(folderName.LastIndexOf('/') + 1)..];
            bool inFolder = prefix.Length > 0 && string.Equals(folderName, baseName, StringComparison.OrdinalIgnoreCase);
            bool flat = prefix.Length == 0;
            if (!inFolder && !flat)
            {
                continue;
            }

            if (!SafeName().IsMatch(baseName) || !seen.Add(baseName))
            {
                continue;
            }

            IEnumerable<ZipArchiveEntry> files = archive.Entries.Where(entry =>
            {
                string name = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(entry.Name) || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string relative = name[prefix.Length..];
                if (relative.Contains('/') ||
                    !AllowedExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }

                // in a flat ZIP only the files that belong to this pack
                return !flat || relative.Equals(file, StringComparison.OrdinalIgnoreCase) ||
                       (relative.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase) &&
                        relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            });
            packs.Add(new LumensPack(baseName, prefix, files.ToList()));
        }

        return packs;
    }

    /// <summary>Checks that the ZIP is readable and holds at least one pack.</summary>
    public static IReadOnlyList<string> Inspect(string zipPath)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            IReadOnlyList<LumensPack> packs = Find(archive);
            if (packs.Count == 0)
            {
                throw new InstallException(
                    "The ZIP has no NULM pack. A pack is a folder X that holds X.nulm and its X_0.png images.",
                    "ZIP に NULM のパックがありません。パックは、X.nulm と X_0.png などの画像が入った、X という名前のフォルダです。");
            }

            return packs.Select(pack => pack.Name).ToList();
        }
        catch (InvalidDataException)
        {
            throw new InstallException(
                "The Lumens file is not a valid ZIP.",
                "Lumens の ZIP として読み込めません。");
        }
    }

    /// <summary>Extracts the packs into the Lumens folder, replacing the files of packs with the same name.</summary>
    public static IReadOnlyList<LumensPack> Install(string zipPath, string lumensDirectory)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        IReadOnlyList<LumensPack> packs = Find(archive);
        long total = packs.SelectMany(pack => pack.Files).Sum(entry => entry.Length);
        if (total > MaxTotalBytes)
        {
            throw new InstallException(
                "The packs in the ZIP are larger than 1 GB, so they were not installed.",
                "ZIP の中身が 1 GB を超えているため、導入しませんでした。");
        }

        string root = Path.GetFullPath(lumensDirectory);
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        foreach (LumensPack pack in packs)
        {
            string packDirectory = Path.GetFullPath(Path.Combine(root, pack.Name));
            if (!packDirectory.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InstallException($"Unsafe pack name: {pack.Name}", $"安全でないパック名です: {pack.Name}");
            }

            Directory.CreateDirectory(packDirectory);
            foreach (ZipArchiveEntry entry in pack.Files)
            {
                string destination = Path.GetFullPath(Path.Combine(packDirectory, entry.Name));
                if (!destination.StartsWith(packDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InstallException($"Unsafe file name: {entry.Name}", $"安全でないファイル名です: {entry.Name}");
                }

                entry.ExtractToFile(destination, overwrite: true);
            }
        }

        return packs;
    }
}
