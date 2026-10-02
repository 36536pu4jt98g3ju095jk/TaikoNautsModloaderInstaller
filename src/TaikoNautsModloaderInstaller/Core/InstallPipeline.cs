using System.Diagnostics;
using System.Text;

namespace TaikoNautsModloaderInstaller.Core;

internal sealed class InstallOptions
{
    public required GameFolder Game { get; init; }
    public bool InstallLoader { get; init; } = true;
    public bool InstallMod { get; init; } = true;
    public bool CreateLumens { get; init; } = true;

    /// <summary>Skin that gets the Lumens folder. Defaults to the skin the game uses.</summary>
    public string? Skin { get; init; }

    /// <summary>Reinstall the ModLoader even when it is already up to date.</summary>
    public bool Force { get; init; }

    /// <summary>Use local packages instead of downloading (offline installs and tests).</summary>
    public string? LoaderZipPath { get; init; }
    public string? ModZipPath { get; init; }

    /// <summary>A ZIP of NULM packs to put into the skin's Lumens folder. Implies the Lumens step.</summary>
    public string? LumensZipPath { get; init; }
}

internal sealed record InstallResult(bool Success, string? LumensPath, string? Error);

internal sealed class InstallPipeline
{
    public event Action<string>? Log;

    /// <summary>0..1 while a step has a known length; null when it does not.</summary>
    public event Action<double?>? Progress;

    private void Say(string english, string japanese) => Log?.Invoke(Lang.T(english, japanese));

    public async Task<InstallResult> RunAsync(InstallOptions options, CancellationToken cancellation)
    {
        string work = Path.Combine(Path.GetTempPath(), "TaikoNautsModloaderInstaller", Guid.NewGuid().ToString("N"));
        try
        {
            GameFolder game = options.Game;
            Preflight(options);

            if (options.InstallLoader)
            {
                await InstallLoaderAsync(options, work, cancellation).ConfigureAwait(false);
            }

            if (options.InstallMod)
            {
                await InstallModAsync(options, work, cancellation).ConfigureAwait(false);
            }

            string? lumens = options.CreateLumens || options.LumensZipPath != null ? CreateLumens(options) : null;
            Progress?.Invoke(1);
            Say("Done.", "完了しました。");
            return new InstallResult(true, lumens, null);
        }
        catch (InstallException exception)
        {
            return new InstallResult(false, null, exception.Message);
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, null, Lang.T("Cancelled.", "中止しました。"));
        }
        catch (HttpRequestException exception)
        {
            return new InstallResult(false, null, Lang.T(
                $"Could not reach GitHub: {exception.Message}",
                $"GitHub に接続できませんでした: {exception.Message}"));
        }
        catch (UnauthorizedAccessException exception)
        {
            return new InstallResult(false, null, Lang.T(
                $"Access was denied: {exception.Message} If the game is in a protected folder, run the installer as administrator.",
                $"アクセスが拒否されました: {exception.Message} 保護されたフォルダにゲームがある場合は、管理者として実行してください。"));
        }
        catch (IOException exception)
        {
            return new InstallResult(false, null, Lang.T(
                $"A file could not be written: {exception.Message} Close the game and the Mod Manager and try again.",
                $"ファイルを書き込めませんでした: {exception.Message} ゲームと Mod Manager を閉じて、もう一度試してください。"));
        }
        finally
        {
            TryDelete(work);
        }
    }

    private void Preflight(InstallOptions options)
    {
        if (GameFolder.IsGameRunning())
        {
            throw new InstallException(
                "TaikoNauts is running. Close it and try again.",
                "TaikoNauts が起動しています。終了してから、もう一度試してください。");
        }

        if (options.InstallLoader && GameFolder.IsManagerRunning())
        {
            throw new InstallException(
                "The Mod Manager is running. Close it and try again.",
                "Mod Manager が起動しています。終了してから、もう一度試してください。");
        }

        if (!options.InstallLoader && !options.InstallMod && !options.CreateLumens && options.LumensZipPath == null)
        {
            throw new InstallException("Nothing is selected.", "何も選択されていません。");
        }

        if (options.LumensZipPath != null)
        {
            // fail before anything is downloaded when the ZIP is missing or holds no pack
            if (!File.Exists(options.LumensZipPath))
            {
                throw new InstallException(
                    $"The Lumens ZIP was not found: {options.LumensZipPath}",
                    $"Lumens の ZIP が見つかりません: {options.LumensZipPath}");
            }

            LumensPackage.Inspect(options.LumensZipPath);
        }
    }

    // ------------------------------------------------------------------ ModLoader

    private async Task InstallLoaderAsync(InstallOptions options, string work, CancellationToken cancellation)
    {
        GameFolder game = options.Game;
        string zip;
        ReleaseInfo? release = null;

        if (options.LoaderZipPath != null)
        {
            zip = options.LoaderZipPath;
            Say($"Using the local ModLoader package {zip}", $"ローカルの ModLoader パッケージを使います: {zip}");
        }
        else
        {
            Say("Checking the latest ModLoader...", "最新の ModLoader を確認しています...");
            release = await GitHubReleases.LatestAsync(Sources.LoaderRepo, cancellation).ConfigureAwait(false);
            Version? installed = game.InstalledLoaderVersion;
            if (!options.Force && game.GetLoaderState() == LoaderState.Installed &&
                installed != null && release.Version != null && installed >= release.Version)
            {
                Say($"The ModLoader {release.Tag} is already installed.", $"ModLoader {release.Tag} は導入済みです。");
                return;
            }

            ReleaseAsset asset = GitHubReleases.Pick(release, Sources.LoaderRepo,
                name => name.EndsWith(Sources.LoaderAssetSuffix, StringComparison.OrdinalIgnoreCase));
            Say($"Downloading the ModLoader {release.Tag} ({asset.Size / 1024 / 1024} MB)...",
                $"ModLoader {release.Tag} をダウンロードしています({asset.Size / 1024 / 1024} MB)...");
            zip = await GitHubReleases.DownloadAsync(asset, work, new Progress<double>(value => Progress?.Invoke(value)), cancellation)
                .ConfigureAwait(false);
            Say("Checked the SHA-256 of the download.", "ダウンロードの SHA-256 を確認しました。");
        }

        Progress?.Invoke(null);
        Say("Extracting the ModLoader...", "ModLoader を展開しています...");
        int files = await Task.Run(() => ZipExtractor.ExtractOverwriting(zip, game.Directory), cancellation).ConfigureAwait(false);
        Say($"Extracted {files} files.", $"{files} 個のファイルを展開しました。");

        await RunLoaderInstallerAsync(game, cancellation).ConfigureAwait(false);

        if (game.GetLoaderState() != LoaderState.Installed)
        {
            throw new InstallException(
                "The ModLoader installer finished, but raylib.dll is not the ModLoader proxy.",
                "ModLoader のインストーラーは終了しましたが、raylib.dll が ModLoader になっていません。");
        }

        Say("The ModLoader is installed.", "ModLoader を導入しました。");
    }

    /// <summary>Runs the ModLoader's own install.ps1, so its checks of the original raylib.dll apply.</summary>
    private async Task RunLoaderInstallerAsync(GameFolder game, CancellationToken cancellation)
    {
        string script = Path.Combine(game.Directory, "install.ps1");
        if (!File.Exists(script))
        {
            throw new InstallException(
                "install.ps1 was not found in the ModLoader package.",
                "ModLoader のパッケージに install.ps1 がありません。");
        }

        Say("Running the ModLoader installer...", "ModLoader のインストーラーを実行しています...");
        var info = new ProcessStartInfo("powershell.exe",
            $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{script}\"")
        {
            WorkingDirectory = game.Directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using Process process = Process.Start(info)
            ?? throw new InstallException("PowerShell could not be started.", "PowerShell を起動できませんでした。");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation);
        Task<string> errors = process.StandardError.ReadToEndAsync(cancellation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        string text = (await errors.ConfigureAwait(false) + "\n" + await output.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0)
        {
            string last = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(line => !line.StartsWith("At ", StringComparison.Ordinal) &&
                                        !line.StartsWith("+", StringComparison.Ordinal)) ?? text;
            throw new InstallException(
                $"The ModLoader installer failed: {last}",
                $"ModLoader のインストーラーが失敗しました: {last}");
        }
    }

    // ------------------------------------------------------------------ the mod

    private async Task InstallModAsync(InstallOptions options, string work, CancellationToken cancellation)
    {
        GameFolder game = options.Game;
        if (game.GetLoaderState() != LoaderState.Installed)
        {
            throw new InstallException(
                "The ModLoader is not installed. Select the ModLoader as well.",
                "ModLoader が導入されていません。ModLoader も選択してください。");
        }

        string zip;
        if (options.ModZipPath != null)
        {
            zip = options.ModZipPath;
            Say($"Using the local mod package {zip}", $"ローカルの MOD パッケージを使います: {zip}");
        }
        else
        {
            Say("Checking the latest NULM Background...", "最新の NULM Background を確認しています...");
            ReleaseInfo release = await GitHubReleases.LatestAsync(Sources.ModRepo, cancellation).ConfigureAwait(false);
            ReleaseAsset asset = GitHubReleases.Pick(release, Sources.ModRepo,
                name => name.StartsWith(Sources.ModAssetPrefix, StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            Say($"Downloading NULM Background {release.Tag}...", $"NULM Background {release.Tag} をダウンロードしています...");
            zip = await GitHubReleases.DownloadAsync(asset, work, new Progress<double>(value => Progress?.Invoke(value)), cancellation)
                .ConfigureAwait(false);
            Say("Checked the SHA-256 of the download.", "ダウンロードの SHA-256 を確認しました。");
        }

        Progress?.Invoke(null);
        Directory.CreateDirectory(game.ModsDirectory);
        int files = await Task.Run(() => ZipExtractor.ExtractOverwriting(zip, game.ModsDirectory), cancellation).ConfigureAwait(false);

        foreach (string required in new[] { "nulm_background.dll", "modconfig.json" })
        {
            if (!File.Exists(Path.Combine(game.ModDirectory, required)))
            {
                throw new InstallException(
                    $"The mod package does not contain {Sources.ModFolder}\\{required}.",
                    $"MOD のパッケージに {Sources.ModFolder}\\{required} がありません。");
            }
        }

        Say($"Installed NULM Background {game.InstalledModVersion} ({files} files). Your config.json and packs were kept.",
            $"NULM Background {game.InstalledModVersion} を導入しました({files} 個のファイル)。config.json とパックはそのままです。");
    }

    // ------------------------------------------------------------------ Lumens

    private string? CreateLumens(InstallOptions options)
    {
        GameFolder game = options.Game;
        string? skin = options.Skin ?? game.SelectedSkin;
        if (skin == null)
        {
            if (options.LumensZipPath != null)
            {
                throw new InstallException(
                    "The skin in use could not be read. Choose a skin for the Lumens ZIP.",
                    "使用中のスキンを読み取れません。Lumens の ZIP を入れるスキンを選んでください。");
            }

            Say("The skin in use could not be read, so no Lumens folder was created.",
                "使用中のスキンを読み取れなかったため、Lumens フォルダは作成しませんでした。");
            return null;
        }

        string skinDirectory = Path.Combine(game.SkinsDirectory, skin);
        if (!Directory.Exists(skinDirectory) && options.LumensZipPath != null)
        {
            throw new InstallException(
                $"The skin folder {skinDirectory} does not exist.",
                $"スキンのフォルダ {skinDirectory} がありません。");
        }

        if (!Directory.Exists(skinDirectory))
        {
            Say($"The skin folder {skinDirectory} does not exist, so no Lumens folder was created.",
                $"スキンのフォルダ {skinDirectory} がないため、Lumens フォルダは作成しませんでした。");
            return null;
        }

        string lumens = Path.Combine(skinDirectory, "Lumens");
        Directory.CreateDirectory(lumens);
        string note = Path.Combine(lumens, "README.txt");
        if (!File.Exists(note))
        {
            File.WriteAllText(note,
                "Put NULM packs here, one folder per pack. Names start with donbg_ (upper), bg_nomal_ (lower),\r\n" +
                "bg_fever_ (fever) or bg_dai_ (stand), for example:\r\n" +
                "  bg_nomal_a_01\\bg_nomal_a_01.nulm\r\n" +
                "  bg_nomal_a_01\\bg_nomal_a_01_0.png\r\n\r\n" +
                "NULM パックをここに置きます。パックごとに 1 つのフォルダを作り、名前の先頭は\r\n" +
                "donbg_(上背景)、bg_nomal_(下背景)、bg_fever_(fever)、bg_dai_(台)にします。\r\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        Say($"Created {lumens}", $"{lumens} を作成しました。");

        if (options.LumensZipPath != null)
        {
            IReadOnlyList<LumensPack> packs = LumensPackage.Install(options.LumensZipPath, lumens);
            Say($"Installed {packs.Count} NULM pack(s) into Lumens: {string.Join(", ", packs.Select(pack => pack.Name))}",
                $"NULM パックを {packs.Count} 個、Lumens に導入しました: {string.Join(", ", packs.Select(pack => pack.Name))}");
            foreach (LumensPack unknown in packs.Where(pack => !pack.IsKnownKind))
            {
                Say($"Note: {unknown.Name} does not start with donbg_, bg_nomal_, bg_fever_ or bg_dai_, so the mod will not use it.",
                    $"注意: {unknown.Name} は donbg_ / bg_nomal_ / bg_fever_ / bg_dai_ で始まらないため、MOD では使われません。");
            }
        }

        return lumens;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
