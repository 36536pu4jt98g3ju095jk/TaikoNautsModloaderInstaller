param(
    [Parameter(Mandatory)] [string]$Exe,
    [Parameter(Mandatory)] [string]$LoaderZip,
    [Parameter(Mandatory)] [string]$ModZip,
    [Parameter(Mandatory)] [string]$OriginalRaylib
)

# Installs into throw-away game folders with local packages, so it needs no network and never
# touches a real game. OriginalRaylib is the unmodified raylib.dll the ModLoader supports.

$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) ('installer-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$failures = 0

function Assert($condition, $message) {
    if ($condition) { Write-Host "  ok   $message" -ForegroundColor Green }
    else { Write-Host "  FAIL $message" -ForegroundColor Red; $script:failures++ }
}

function New-FakeGame($name, [byte[]]$raylib) {
    $dir = Join-Path $work $name
    New-Item -ItemType Directory -Path "$dir\Config", "$dir\Skins\ksty", "$dir\Skins\K-Style" -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\find.exe') -Destination "$dir\TaikoNauts.exe"
    [IO.File]::WriteAllText("$dir\Config\GameConfig.json", '{"skinPath": "Skins//ksty"}')
    if ($raylib) { [IO.File]::WriteAllBytes("$dir\raylib.dll", $raylib) }
    else { Copy-Item -LiteralPath $OriginalRaylib -Destination "$dir\raylib.dll" }
    return $dir
}

function Invoke-Installer($arguments, $logName) {
    $log = Join-Path $work $logName
    $process = Start-Process -FilePath $Exe -ArgumentList ($arguments + @('--lang', 'en', '--log', "`"$log`"")) `
        -Wait -PassThru -WindowStyle Hidden
    return [pscustomobject]@{ Code = $process.ExitCode; Log = (Get-Content -LiteralPath $log -Raw) }
}

try {
    Write-Host 'full install'
    $game = New-FakeGame 'full' $null
    $run = Invoke-Installer @('--game', "`"$game`"", '--loader-zip', "`"$LoaderZip`"", '--mod-zip', "`"$ModZip`"") 'full.log'
    Assert ($run.Code -eq 0) 'exit code 0'
    $same = (Get-FileHash "$game\raylib.dll").Hash -eq (Get-FileHash "$game\raylib_mod_loader.dll").Hash
    Assert $same 'raylib.dll is the ModLoader proxy'
    $original = (Get-FileHash "$game\raylib_original.dll").Hash -eq (Get-FileHash $OriginalRaylib).Hash
    Assert $original 'the original raylib.dll is backed up'
    Assert (Test-Path "$game\mods\nulm-background\nulm_background.dll") 'the mod is installed'
    Assert (Test-Path "$game\Skins\ksty\Lumens") 'Lumens is created in the skin in use'
    Assert (-not (Test-Path "$game\Skins\K-Style\Lumens")) 'other skins are left alone'

    Write-Host 'the bundled ModLoader'
    $bundled = New-FakeGame 'bundled' $null
    $run = Invoke-Installer @('--game', "`"$bundled`"", '--no-mod', '--no-lumens') 'bundled.log'
    Assert ($run.Code -eq 0) 'installs with no package given (nothing is downloaded)'
    Assert ($run.Log -match 'bundled ModLoader') 'says it uses the bundled ModLoader'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $LoaderZip))
    $entry = $archive.GetEntry('raylib_mod_loader.dll')
    $sha = [Security.Cryptography.SHA256]::Create()
    $zipHash = [BitConverter]::ToString($sha.ComputeHash($entry.Open())).Replace('-', '')
    $archive.Dispose()
    Assert ((Get-FileHash "$bundled\raylib.dll").Hash -eq $zipHash) 'raylib.dll is the bundled ModLoader'
    Assert (Test-Path "$bundled\TaikoNauts.ModManager.exe") 'the Mod Manager is installed'
    $run = Invoke-Installer @('--game', "`"$bundled`"", '--no-mod', '--no-lumens') 'bundled2.log'
    Assert ($run.Code -eq 0 -and $run.Log -match 'already installed') 'a second run does not install it again'
    $run = Invoke-Installer @('--game', "`"$bundled`"", '--no-mod', '--no-lumens', '--force') 'bundled3.log'
    Assert ($run.Code -eq 0 -and $run.Log -match 'Extracting the ModLoader') '--force installs it again'
    $versionFile = Join-Path $work 'version.txt'
    Start-Process -FilePath $Exe -ArgumentList '--version' -Wait -WindowStyle Hidden -RedirectStandardOutput $versionFile
    Assert ((Get-Content $versionFile -Raw) -match 'bundled ModLoader: v\d') '--version reports the bundled ModLoader'

    Write-Host 'a second run keeps user files'
    [IO.File]::WriteAllText("$game\mods\nulm-background\config.json", '{"mine": true}')
    New-Item -ItemType Directory -Path "$game\Skins\ksty\Lumens\bg_nomal_a_01" -Force | Out-Null
    [IO.File]::WriteAllText("$game\Skins\ksty\Lumens\bg_nomal_a_01\bg_nomal_a_01.nulm", 'pack')
    $run = Invoke-Installer @('--game', "`"$game\TaikoNauts.exe`"", '--no-loader', '--mod-zip', "`"$ModZip`"") 'second.log'
    Assert ($run.Code -eq 0) 'exit code 0'
    Assert ((Get-Content "$game\mods\nulm-background\config.json" -Raw) -match 'mine') 'config.json is kept'
    Assert (Test-Path "$game\Skins\ksty\Lumens\bg_nomal_a_01\bg_nomal_a_01.nulm") 'packs in Lumens are kept'

    Write-Host 'a chosen skin'
    $run = Invoke-Installer @('--game', "`"$game`"", '--no-loader', '--no-mod', '--skin', 'K-Style') 'skin.log'
    Assert ($run.Code -eq 0 -and (Test-Path "$game\Skins\K-Style\Lumens")) 'Lumens is created in the chosen skin'

    Write-Host 'a Lumens ZIP'
    function New-Zip($path, $files) {
        Add-Type -AssemblyName System.IO.Compression
        if (Test-Path $path) { [IO.File]::Delete($path) }
        $stream = [IO.File]::Create($path)
        $archive = New-Object IO.Compression.ZipArchive($stream, 'Create')
        foreach ($name in $files.Keys) {
            $entry = $archive.CreateEntry($name)
            $writer = New-Object IO.StreamWriter($entry.Open())
            $writer.Write($files[$name]); $writer.Dispose()
        }
        $archive.Dispose(); $stream.Dispose()
    }
    $lumens = "$game\Skins\ksty\Lumens"

    New-Zip "$work\l1.zip" ([ordered]@{
        'bg_nomal_a_01/bg_nomal_a_01.nulm' = 'new'
        'bg_nomal_a_01/bg_nomal_a_01_0.png' = 'png'
        'bg_nomal_a_01/readme.txt' = 'text'
        'bg_nomal_a_01/evil.exe' = 'exe'
        'donbg_a_01_1p/donbg_a_01_1p.nulm' = 'nulm'
        'donbg_a_01_1p/donbg_a_01_1p_0.png' = 'png'
    })
    $run = Invoke-Installer @('--game', "`"$game`"", '--no-loader', '--no-mod', '--lumens-zip', "`"$work\l1.zip`"") 'l1.log'
    Assert ($run.Code -eq 0) 'packs at the top of the ZIP: exit code 0'
    Assert ((Test-Path "$lumens\bg_nomal_a_01\bg_nomal_a_01_0.png") -and (Test-Path "$lumens\donbg_a_01_1p\donbg_a_01_1p.nulm")) 'both packs are installed'
    Assert ((Get-Content "$lumens\bg_nomal_a_01\bg_nomal_a_01.nulm" -Raw) -eq 'new') 'an existing pack is replaced'
    Assert (-not (Test-Path "$lumens\bg_nomal_a_01\evil.exe") -and -not (Test-Path "$lumens\bg_nomal_a_01\readme.txt")) 'only .nulm and .png files are taken'

    New-Zip "$work\l2.zip" ([ordered]@{
        'Lumens/bg_fever_a_01/bg_fever_a_01.nulm' = 'x'
        'Lumens/bg_fever_a_01/bg_fever_a_01_0.png' = 'x'
        'MyPacks/Set1/bg_dai_a_01/bg_dai_a_01.nulm' = 'x'
        'MyPacks/Set1/bg_dai_a_01/bg_dai_a_01_0.png' = 'x'
    })
    $run = Invoke-Installer @('--game', "`"$game`"", '--no-loader', '--no-mod', '--lumens-zip', "`"$work\l2.zip`"") 'l2.log'
    Assert ($run.Code -eq 0 -and (Test-Path "$lumens\bg_fever_a_01\bg_fever_a_01.nulm") -and (Test-Path "$lumens\bg_dai_a_01\bg_dai_a_01_0.png")) 'packs inside wrapper folders are found'

    New-Zip "$work\l3.zip" ([ordered]@{
        'bg_nomal_a_02.nulm' = 'x'
        'bg_nomal_a_02_0.png' = 'x'
        'unrelated.png' = 'x'
    })
    $run = Invoke-Installer @('--game', "`"$game`"", '--no-loader', '--no-mod', '--lumens-zip', "`"$work\l3.zip`"") 'l3.log'
    Assert ($run.Code -eq 0 -and (Test-Path "$lumens\bg_nomal_a_02\bg_nomal_a_02_0.png") -and -not (Test-Path "$lumens\bg_nomal_a_02\unrelated.png")) 'a flat ZIP is put into a folder'

    New-Zip "$work\l4.zip" ([ordered]@{ 'mypack/mypack.nulm' = 'x'; 'mypack/mypack_0.png' = 'x' })
    $run = Invoke-Installer @('--game', "`"$game`"", '--no-loader', '--no-mod', '--lumens-zip', "`"$work\l4.zip`"") 'l4.log'
    Assert ($run.Code -eq 0 -and $run.Log -match 'does not start with') 'a pack with an unknown name is installed with a note'

    New-Zip "$work\l5.zip" ([ordered]@{ 'readme.txt' = 'nothing here' })
    $run = Invoke-Installer @('--game', "`"$game`"", '--no-loader', '--no-mod', '--lumens-zip', "`"$work\l5.zip`"") 'l5.log'
    Assert ($run.Code -eq 1 -and $run.Log -match 'no NULM pack') 'a ZIP without packs is refused'

    New-Zip "$work\l6.zip" ([ordered]@{ 'bg_x/../../escape.nulm' = 'x'; '../escape.png' = 'x' })
    $run = Invoke-Installer @('--game', "`"$game`"", '--no-loader', '--no-mod', '--lumens-zip', "`"$work\l6.zip`"") 'l6.log'
    Assert ($run.Code -eq 1) 'a ZIP that tries to escape holds no usable pack'
    Assert (-not (Test-Path "$game\Skins\ksty\escape.nulm") -and -not (Test-Path "$game\Skins\escape.png") -and -not (Test-Path "$lumens\escape.nulm")) 'nothing is written outside Lumens'

    $run = Invoke-Installer @('--game', "`"$game`"", '--no-loader', '--no-mod', '--lumens-zip', "`"$work\missing.zip`"") 'l7.log'
    Assert ($run.Code -eq 1 -and $run.Log -match 'was not found') 'a missing ZIP is reported'

    $run = Invoke-Installer @('--game', "`"$game`"", '--no-loader', '--no-mod', '--skin', 'K-Style', '--lumens-zip', "`"$work\l1.zip`"") 'l8.log'
    Assert ($run.Code -eq 0 -and (Test-Path "$game\Skins\K-Style\Lumens\bg_nomal_a_01\bg_nomal_a_01.nulm")) 'the ZIP goes into the chosen skin'

    Write-Host 'the mod needs the ModLoader'
    $bare = New-FakeGame 'bare' $null
    $run = Invoke-Installer @('--game', "`"$bare`"", '--no-loader', '--mod-zip', "`"$ModZip`"") 'bare.log'
    Assert ($run.Code -eq 1 -and $run.Log -match 'ModLoader is not installed') 'refuses without the ModLoader'

    Write-Host 'an unsupported raylib.dll'
    $odd = New-FakeGame 'odd' ([byte[]](1..200))
    $run = Invoke-Installer @('--game', "`"$odd`"", '--no-mod', '--loader-zip', "`"$LoaderZip`"") 'odd.log'
    Assert ($run.Code -eq 1 -and $run.Log -match 'Unsupported or already modified') 'is refused with the loader''s message'
    Assert ((Get-Item "$odd\raylib.dll").Length -eq 200) 'raylib.dll is left untouched'

    Write-Host 'a package that escapes the folder'
    Add-Type -AssemblyName System.IO.Compression
    $evil = Join-Path $work 'evil.zip'
    $stream = [IO.File]::Create($evil)
    $archive = New-Object IO.Compression.ZipArchive($stream, 'Create')
    $writer = New-Object IO.StreamWriter(($archive.CreateEntry('../evil.txt')).Open())
    $writer.Write('x'); $writer.Dispose(); $archive.Dispose(); $stream.Dispose()
    $run = Invoke-Installer @('--game', "`"$game`"", '--no-loader', '--no-lumens', '--mod-zip', "`"$evil`"") 'evil.log'
    Assert ($run.Code -eq 1 -and $run.Log -match 'unsafe path') 'is rejected'
    Assert (-not (Test-Path (Join-Path $game 'evil.txt')) -and -not (Test-Path (Join-Path $work 'evil.txt'))) 'nothing is written outside'

    Write-Host 'wrong input'
    $run = Invoke-Installer @('--game', "`"$game\raylib.dll`"") 'wrong.log'
    Assert ($run.Code -eq 2) 'a file that is not TaikoNauts.exe is refused'

    Write-Host 'the window'
    $ui = Start-Process -FilePath $Exe -ArgumentList '--selftest-ui' -Wait -PassThru
    Assert ($ui.ExitCode -eq 0) 'is created without errors'
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures -gt 0) { Write-Host "$failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All checks passed.' -ForegroundColor Green
