using System.Diagnostics;
using TaikoNautsModloaderInstaller.Core;

namespace TaikoNautsModloaderInstaller.UI;

internal sealed class MainForm : Form
{
    private readonly Label title = new() { AutoSize = true };
    private readonly Label intro = new() { AutoSize = true, MaximumSize = new Size(560, 0) };
    private readonly LinkLabel languageLink = new() { AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
    private readonly Label pathLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly TextBox pathBox = new() { Dock = DockStyle.Fill };
    private readonly Button browseButton = new() { AutoSize = true };
    private readonly GroupBox statusGroup = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly Label gameStatus = new() { AutoSize = true };
    private readonly Label loaderStatus = new() { AutoSize = true };
    private readonly Label modStatus = new() { AutoSize = true };
    private readonly GroupBox optionsGroup = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly CheckBox loaderCheck = new() { AutoSize = true, Checked = true };
    private readonly CheckBox modCheck = new() { AutoSize = true, Checked = true };
    private readonly CheckBox lumensCheck = new() { AutoSize = true, Checked = true };
    private readonly ComboBox skinBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ProgressBar progressBar = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000 };
    private readonly TextBox logBox = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
    };
    private readonly Button installButton = new() { AutoSize = true, Enabled = false };
    private readonly Button openLumensButton = new() { AutoSize = true, Enabled = false };
    private readonly Button closeButton = new() { AutoSize = true };

    private GameFolder? game;
    private string? lumensPath;
    private bool busy;

    public MainForm()
    {
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(600, 640);
        AllowDrop = true;
        Icon = SystemIcons.Application;

        title.Font = new Font(Font.FontFamily, 14f, FontStyle.Bold);

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(languageLink, 1, 0);

        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(pathLabel, 0, 0);
        pathRow.Controls.Add(pathBox, 1, 0);
        pathRow.Controls.Add(browseButton, 2, 0);

        var statusPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        statusPanel.Controls.AddRange(new Control[] { gameStatus, loaderStatus, modStatus });
        statusGroup.Controls.Add(statusPanel);
        statusGroup.Padding = new Padding(10, 6, 10, 8);

        var skinRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        skinRow.Controls.Add(lumensCheck);
        skinRow.Controls.Add(skinBox);
        var optionsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        optionsPanel.Controls.AddRange(new Control[] { loaderCheck, modCheck, skinRow });
        optionsGroup.Controls.Add(optionsPanel);
        optionsGroup.Padding = new Padding(10, 6, 10, 8);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(openLumensButton);
        buttons.Controls.Add(installButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(16, 12, 16, 12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(intro, 0, 1);
        layout.Controls.Add(pathRow, 0, 2);
        layout.Controls.Add(statusGroup, 0, 3);
        layout.Controls.Add(optionsGroup, 0, 4);
        layout.Controls.Add(progressBar, 0, 5);
        layout.Controls.Add(logBox, 0, 6);
        layout.Controls.Add(buttons, 0, 7);
        Controls.Add(layout);

        languageLink.LinkClicked += (_, _) => { Lang.Japanese = !Lang.Japanese; ApplyLanguage(); };
        browseButton.Click += (_, _) => Browse();
        pathBox.Leave += (_, _) => SetGame(pathBox.Text);
        pathBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SetGame(pathBox.Text);
            }
        };
        lumensCheck.CheckedChanged += (_, _) => skinBox.Enabled = lumensCheck.Checked && !busy;
        installButton.Click += async (_, _) => await InstallAsync();
        openLumensButton.Click += (_, _) => OpenLumens();
        closeButton.Click += (_, _) => Close();
        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                SetGame(files[0]);
            }
        };

        ApplyLanguage();
        GameFolder? detected = GameFolder.Detect(AppContext.BaseDirectory);
        if (detected != null)
        {
            SetGame(detected.ExecutablePath);
        }
        else if (LastGamePath.Read() is { } last)
        {
            SetGame(last);
        }
    }

    private void ApplyLanguage()
    {
        Text = Lang.T("TaikoNauts ModLoader Installer", "TaikoNauts ModLoader インストーラー");
        title.Text = Text;
        intro.Text = Lang.T(
            "Select TaikoNauts.exe. The installer sets up the ModLoader and the NULM Background mod for you.",
            "TaikoNauts.exe を選ぶと、ModLoader と NULM Background MOD を自動で導入します。");
        languageLink.Text = Lang.Japanese ? "English" : "日本語";
        pathLabel.Text = "TaikoNauts.exe";
        browseButton.Text = Lang.T("Browse...", "参照...");
        statusGroup.Text = Lang.T("Status", "状態");
        optionsGroup.Text = Lang.T("Install", "導入するもの");
        loaderCheck.Text = Lang.T("TaikoNauts ModLoader (latest)", "TaikoNauts ModLoader(最新版)");
        modCheck.Text = Lang.T("NULM Background mod (latest)", "NULM Background MOD(最新版)");
        lumensCheck.Text = Lang.T("Create a Lumens folder in skin:", "Lumens フォルダを作るスキン:");
        installButton.Text = Lang.T("Install", "インストール");
        openLumensButton.Text = Lang.T("Open Lumens folder", "Lumens フォルダを開く");
        closeButton.Text = Lang.T("Close", "閉じる");
        RefreshStatus();
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = Lang.T("Select TaikoNauts.exe", "TaikoNauts.exe を選択"),
            Filter = "TaikoNauts.exe|TaikoNauts.exe",
            CheckFileExists = true,
        };
        if (game != null)
        {
            dialog.InitialDirectory = game.Directory;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetGame(dialog.FileName);
        }
    }

    private void SetGame(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!GameFolder.TryOpen(path, out GameFolder? opened, out string error))
        {
            game = null;
            installButton.Enabled = false;
            string message = error == "wrong-file"
                ? Lang.T("Select the file named TaikoNauts.exe.", "TaikoNauts.exe というファイルを選んでください。")
                : Lang.T("TaikoNauts.exe was not found there.", "そこに TaikoNauts.exe が見つかりません。");
            AppendLog(message);
            RefreshStatus();
            return;
        }

        game = opened;
        pathBox.Text = opened!.ExecutablePath;
        LastGamePath.Write(opened.ExecutablePath);

        skinBox.Items.Clear();
        foreach (string skin in opened.Skins())
        {
            skinBox.Items.Add(skin);
        }

        string? selected = opened.SelectedSkin;
        int index = selected == null ? -1 : skinBox.FindStringExact(selected);
        skinBox.SelectedIndex = index >= 0 ? index : (skinBox.Items.Count > 0 ? 0 : -1);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        string none = Lang.T("not installed", "未導入");
        if (game == null)
        {
            gameStatus.Text = Lang.T("Game: not selected", "ゲーム: 未選択");
            loaderStatus.Text = $"ModLoader: {none}";
            modStatus.Text = $"NULM Background: {none}";
            installButton.Enabled = false;
            return;
        }

        gameStatus.Text = Lang.T("Game: ", "ゲーム: ") + (game.GameVersion ?? "?");
        string loader = game.GetLoaderState() == LoaderState.Installed
            ? Lang.T("installed", "導入済み") + (game.InstalledLoaderVersion is { } v ? $" (v{v.ToString(3)})" : string.Empty)
            : none;
        loaderStatus.Text = $"ModLoader: {loader}";
        modStatus.Text = $"NULM Background: {(game.InstalledModVersion is { } mod ? Lang.T("installed", "導入済み") + $" (v{mod})" : none)}";
        installButton.Enabled = !busy;
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(line));
            return;
        }

        logBox.AppendText(line + Environment.NewLine);
    }

    private void SetBusy(bool value)
    {
        busy = value;
        foreach (Control control in new Control[] { browseButton, pathBox, loaderCheck, modCheck, lumensCheck, closeButton })
        {
            control.Enabled = !value;
        }

        skinBox.Enabled = !value && lumensCheck.Checked;
        installButton.Enabled = !value && game != null;
    }

    private async Task InstallAsync()
    {
        if (game == null || busy)
        {
            return;
        }

        SetBusy(true);
        openLumensButton.Enabled = false;
        logBox.Clear();
        progressBar.Style = ProgressBarStyle.Marquee;

        var pipeline = new InstallPipeline();
        pipeline.Log += AppendLog;
        pipeline.Progress += value => BeginInvoke(() =>
        {
            if (value is { } fraction)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = Math.Clamp((int)(fraction * 1000), 0, 1000);
            }
            else
            {
                progressBar.Style = ProgressBarStyle.Marquee;
            }
        });

        var options = new InstallOptions
        {
            Game = game,
            InstallLoader = loaderCheck.Checked,
            InstallMod = modCheck.Checked,
            CreateLumens = lumensCheck.Checked && skinBox.SelectedItem != null,
            Skin = skinBox.SelectedItem as string,
        };

        InstallResult result = await Task.Run(() => pipeline.RunAsync(options, CancellationToken.None));
        progressBar.Style = ProgressBarStyle.Continuous;
        progressBar.Value = result.Success ? progressBar.Maximum : 0;
        SetBusy(false);
        RefreshStatus();

        if (result.Success)
        {
            lumensPath = result.LumensPath;
            openLumensButton.Enabled = lumensPath != null;
            string message = Lang.T("The installation finished.", "インストールが完了しました。");
            if (lumensPath != null)
            {
                message += Environment.NewLine + Environment.NewLine + Lang.T(
                    "Put your NULM packs in the Lumens folder, then start the game.",
                    "NULM のパックを Lumens フォルダに置いて、ゲームを起動してください。");
            }

            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            AppendLog(Lang.T("Failed: ", "失敗: ") + result.Error);
            MessageBox.Show(this, result.Error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenLumens()
    {
        if (lumensPath != null && Directory.Exists(lumensPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{lumensPath}\"") { UseShellExecute = true });
        }
    }
}

/// <summary>Remembers the last selected TaikoNauts.exe between runs.</summary>
internal static class LastGamePath
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaikoNautsModloaderInstaller", "last-game.txt");

    public static string? Read()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void Write(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
