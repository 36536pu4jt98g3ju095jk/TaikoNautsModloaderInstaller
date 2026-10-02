using System.Reflection;

namespace TaikoNautsModloaderInstaller.Core;

/// <summary>The ModLoader package that is embedded in this executable at build time.</summary>
internal static class BundledLoader
{
    private const string PackageName = "ModLoader.zip";
    private const string VersionName = "ModLoader.version";

    private static readonly Assembly Source = typeof(BundledLoader).Assembly;

    public static bool Available => Source.GetManifestResourceInfo(PackageName) != null;

    /// <summary>Version of the embedded ModLoader, for example 1.3.1.</summary>
    public static Version? Version
    {
        get
        {
            using Stream? stream = Source.GetManifestResourceStream(VersionName);
            if (stream == null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd().Trim().TrimStart('v', 'V');
            return Version.TryParse(text, out Version? parsed) ? parsed : null;
        }
    }

    /// <summary>Writes the embedded ZIP to a file and returns its path.</summary>
    public static string WriteTo(string directory)
    {
        using Stream stream = Source.GetManifestResourceStream(PackageName)
            ?? throw new InstallException(
                "This build of the installer does not contain the ModLoader.",
                "このビルドのインストーラーには、ModLoader が含まれていません。");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ModLoader.zip");
        using FileStream target = File.Create(path);
        stream.CopyTo(target);
        return path;
    }
}
