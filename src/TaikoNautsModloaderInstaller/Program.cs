using System.Runtime.InteropServices;
using TaikoNautsModloaderInstaller.Core;
using TaikoNautsModloaderInstaller.UI;

namespace TaikoNautsModloaderInstaller;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private const string Usage =
        "TaikoNautsModloaderInstaller [options]\r\n" +
        "  (no options)          open the installer window\r\n" +
        "  --game <path>         TaikoNauts.exe (or its folder); installs without a window\r\n" +
        "  --no-loader           do not install the ModLoader\r\n" +
        "  --no-mod              do not install the NULM Background mod\r\n" +
        "  --no-lumens           do not create the Lumens folder\r\n" +
        "  --skin <name>         skin that gets the Lumens folder (default: the one in use)\r\n" +
        "  --force               reinstall the ModLoader even when it is up to date\r\n" +
        "  --loader-zip <file>   use a local ModLoader package instead of downloading\r\n" +
        "  --mod-zip <file>      use a local mod package instead of downloading\r\n" +
        "  --log <file>          also write the log to a file\r\n" +
        "  --lang ja|en          language of the messages\r\n" +
        "  --selftest-ui         create the window once and exit\r\n" +
        "  --shot <file.png>     with --selftest-ui: save the window as an image\r\n" +
        "  --shot-path <text>    with --shot: text to show in the path box\r\n";

    [STAThread]
    private static int Main(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] withValue = { "--game", "--skin", "--loader-zip", "--mod-zip", "--log", "--lang", "--shot", "--shot-path" };
        for (int i = 0; i < args.Length; i++)
        {
            if (withValue.Contains(args[i], StringComparer.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                values[args[i]] = args[++i];
            }
            else
            {
                flags.Add(args[i]);
            }
        }

        if (values.TryGetValue("--lang", out string? lang))
        {
            Lang.Japanese = string.Equals(lang, "ja", StringComparison.OrdinalIgnoreCase);
        }

        if (flags.Contains("--help") || flags.Contains("-h") || flags.Contains("/?"))
        {
            AttachConsole(-1);
            Console.Write(Usage);
            return 0;
        }

        if (flags.Contains("--selftest-ui"))
        {
            ApplicationConfiguration.Initialize();
            using var form = new MainForm();
            form.CreateControl();
            _ = form.Handle;
            if (values.TryGetValue("--shot", out string? shot) && !string.IsNullOrEmpty(shot))
            {
                // draws the form into an image, without touching the screen; the form is shown off-screen
                // first so that every control is laid out and painted
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                if (values.TryGetValue("--shot-path", out string? shown) && !string.IsNullOrEmpty(shown))
                {
                    form.ShowPathForScreenshot(shown);
                }

                form.PerformLayout();
                using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
            }

            return 0;
        }

        if (values.ContainsKey("--game"))
        {
            AttachConsole(-1);
            return RunCommandLine(values, flags);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int RunCommandLine(Dictionary<string, string?> values, HashSet<string> flags)
    {
        StreamWriter? logFile = null;
        void Write(string line)
        {
            Console.WriteLine(line);
            logFile?.WriteLine(line);
            logFile?.Flush();
        }

        try
        {
            if (values.TryGetValue("--log", out string? logPath) && !string.IsNullOrEmpty(logPath))
            {
                logFile = new StreamWriter(logPath, append: false, new System.Text.UTF8Encoding(false));
            }

            if (!GameFolder.TryOpen(values["--game"], out GameFolder? game, out string error) || game == null)
            {
                string reason = error == "wrong-file"
                    ? Lang.T("Select the file named TaikoNauts.exe: ", "TaikoNauts.exe というファイルを指定してください: ")
                    : Lang.T("TaikoNauts.exe was not found: ", "TaikoNauts.exe が見つかりません: ");
                Write(reason + values["--game"]);
                return 2;
            }

            var options = new InstallOptions
            {
                Game = game,
                InstallLoader = !flags.Contains("--no-loader"),
                InstallMod = !flags.Contains("--no-mod"),
                CreateLumens = !flags.Contains("--no-lumens"),
                Skin = values.GetValueOrDefault("--skin"),
                Force = flags.Contains("--force"),
                LoaderZipPath = values.GetValueOrDefault("--loader-zip"),
                ModZipPath = values.GetValueOrDefault("--mod-zip"),
            };

            var pipeline = new InstallPipeline();
            pipeline.Log += Write;
            InstallResult result = pipeline.RunAsync(options, CancellationToken.None).GetAwaiter().GetResult();
            if (!result.Success)
            {
                Write(Lang.T("Failed: ", "失敗: ") + result.Error);
                return 1;
            }

            if (result.LumensPath != null)
            {
                Write("Lumens: " + result.LumensPath);
            }

            return 0;
        }
        finally
        {
            logFile?.Dispose();
        }
    }
}
