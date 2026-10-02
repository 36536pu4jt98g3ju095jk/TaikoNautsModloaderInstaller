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
