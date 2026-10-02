using System.Globalization;

namespace TaikoNautsModloaderInstaller.Core;

internal static class Lang
{
    public static bool Japanese { get; set; } =
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase);

    public static string T(string english, string japanese) => Japanese ? japanese : english;
}

/// <summary>A failure the user can act on. The message is shown in the current language.</summary>
internal sealed class InstallException : Exception
{
    private readonly string english;
    private readonly string japanese;

    public InstallException(string english, string japanese)
        : base(english)
    {
        this.english = english;
        this.japanese = japanese;
    }

    public override string Message => Lang.T(english, japanese);
}
