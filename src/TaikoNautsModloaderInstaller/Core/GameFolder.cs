using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace TaikoNautsModloaderInstaller.Core;

internal enum LoaderState
{
    NotInstalled,
    Installed,
}

/// <summary>The folder that holds TaikoNauts.exe, and what is installed in it.</summary>
internal sealed class GameFolder
{
    public const string ExecutableName = "TaikoNauts.exe";

    private GameFolder(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }
    public string ExecutablePath => Path.Combine(Directory, ExecutableName);
    public string RaylibPath => Path.Combine(Directory, "raylib.dll");
    public string RaylibOriginalPath => Path.Combine(Directory, "raylib_original.dll");
    public string LoaderDllPath => Path.Combine(Directory, "raylib_mod_loader.dll");
    public string ManagerPath => Path.Combine(Directory, "TaikoNauts.ModManager.exe");
    public string ModsDirectory => Path.Combine(Directory, "mods");
    public string SkinsDirectory => Path.Combine(Directory, "Skins");
    public string GameConfigPath => Path.Combine(Directory, "Config", "GameConfig.json");
    public string ModDirectory => Path.Combine(ModsDirectory, Sources.ModFolder);

    /// <summary>Accepts the path of TaikoNauts.exe (or the folder holding it).</summary>
    public static bool TryOpen(string? path, out GameFolder? folder, out string error)
    {
        folder = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "no-path";
            return false;
        }

        string candidate = path.Trim().Trim('"');
        if (System.IO.Directory.Exists(candidate))
        {
            candidate = Path.Combine(candidate, ExecutableName);
        }

        if (!File.Exists(candidate))
        {
            error = "not-found";
            return false;
        }

        if (!string.Equals(Path.GetFileName(candidate), ExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            error = "wrong-file";
            return false;
        }

        folder = new GameFolder(Path.GetDirectoryName(Path.GetFullPath(candidate))!);
        return true;
    }

    /// <summary>Looks for TaikoNauts.exe next to the installer and in its parent folders.</summary>
    public static GameFolder? Detect(string startDirectory)
    {
        string? current = startDirectory;
        for (int depth = 0; depth < 4 && !string.IsNullOrEmpty(current); depth++)
        {
            if (TryOpen(current, out GameFolder? found, out _))
            {
                return found;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    public string? GameVersion
    {
        get
        {
            try
            {
                string? version = FileVersionInfo.GetVersionInfo(ExecutablePath).ProductVersion;
                int plus = version?.IndexOf('+') ?? -1;
                return plus > 0 ? version![..plus] : version;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public static bool IsGameRunning() => Process.GetProcessesByName("TaikoNauts").Length > 0;

    public static bool IsManagerRunning() => Process.GetProcessesByName("TaikoNauts.ModManager").Length > 0;

    /// <summary>The loader counts as installed when raylib.dll is the proxy and the original is backed up.</summary>
    public LoaderState GetLoaderState()
    {
        if (!File.Exists(RaylibOriginalPath) || !File.Exists(LoaderDllPath) || !File.Exists(RaylibPath))
        {
            return LoaderState.NotInstalled;
        }

        return HashOf(RaylibPath) == HashOf(LoaderDllPath) ? LoaderState.Installed : LoaderState.NotInstalled;
    }

    /// <summary>Version of the installed ModLoader, read from its Mod Manager.</summary>
    public Version? InstalledLoaderVersion
    {
        get
        {
            try
            {
                string? text = FileVersionInfo.GetVersionInfo(ManagerPath).FileVersion;
                return text != null && Version.TryParse(text, out Version? version) ? version : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public string? InstalledModVersion
    {
        get
        {
            string manifest = Path.Combine(ModDirectory, "modconfig.json");
            if (!File.Exists(manifest))
            {
                return null;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
                return document.RootElement.TryGetProperty("version", out JsonElement version)
                    ? version.GetString()
                    : "?";
            }
            catch (JsonException)
            {
                return "?";
            }
        }
    }

    /// <summary>The skin the game uses, from Config\GameConfig.json ("skinPath": "Skins//name").</summary>
    public string? SelectedSkin
    {
        get
        {
            if (!File.Exists(GameConfigPath))
            {
                return null;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(GameConfigPath));
                if (!document.RootElement.TryGetProperty("skinPath", out JsonElement element))
                {
                    return null;
                }

                string? value = element.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                string[] parts = value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length == 0 ? null : parts[^1];
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public IReadOnlyList<string> Skins()
    {
        if (!System.IO.Directory.Exists(SkinsDirectory))
        {
            return Array.Empty<string>();
        }

        return System.IO.Directory.GetDirectories(SkinsDirectory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string HashOf(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
