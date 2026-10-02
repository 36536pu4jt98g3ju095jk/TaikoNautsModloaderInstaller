param(
    # The ModLoader package to embed, for example TaikoNauts-ModLoader-v1.3.1-win-x64.zip.
    [string]$LoaderZip = '',

    # Version of that ModLoader. Read from the file name when it is not given.
    [string]$LoaderVersion = '',

    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$projectDirectory = Join-Path $root 'src\TaikoNautsModloaderInstaller'
$project = Join-Path $projectDirectory 'TaikoNautsModloaderInstaller.csproj'
$embedded = Join-Path $projectDirectory 'Embedded'
$dist = Join-Path $root 'dist'
$publish = Join-Path $root 'build\publish'

# ---- the ModLoader that travels inside the executable
if ($LoaderZip -eq '') {
    $existing = Join-Path $embedded 'ModLoader.zip'
    if (-not (Test-Path -LiteralPath $existing)) {
        throw 'Pass -LoaderZip <ModLoader package zip>. The installer embeds it so that it needs no download.'
    }
    Write-Host 'Using the ModLoader package already in src\...\Embedded.' -ForegroundColor DarkGray
} else {
    if (-not (Test-Path -LiteralPath $LoaderZip)) { throw "ModLoader package not found: $LoaderZip" }
    if ($LoaderVersion -eq '') {
        if ((Split-Path -Leaf $LoaderZip) -match 'v(\d+\.\d+\.\d+)') { $LoaderVersion = $Matches[1] }
        else { throw 'Could not read the ModLoader version from the file name; pass -LoaderVersion.' }
    }

    # the package must be a ModLoader package: it holds the loader DLL and its installer
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $LoaderZip))
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
        foreach ($required in 'raylib_mod_loader.dll', 'install.ps1', 'uninstall.ps1', 'TaikoNauts.ModManager.exe') {
            if ($names -notcontains $required) { throw "The ModLoader package has no $required." }
        }
    } finally {
        $archive.Dispose()
    }

    New-Item -ItemType Directory -Path $embedded -Force | Out-Null
    Copy-Item -LiteralPath $LoaderZip -Destination (Join-Path $embedded 'ModLoader.zip') -Force
    [IO.File]::WriteAllText((Join-Path $embedded 'ModLoader.version'), $LoaderVersion)
    Write-Host ("Embedding the ModLoader v{0} ({1:N1} MB)" -f $LoaderVersion, ((Get-Item -LiteralPath $LoaderZip).Length / 1MB)) -ForegroundColor Cyan
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (Test-Path -LiteralPath $publish) {
    Remove-Item -LiteralPath $publish -Recurse -Force
}

& dotnet publish $project -c $Configuration -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $publish 'TaikoNautsModloaderInstaller.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'The installer executable was not produced.' }

$target = Join-Path $dist 'TaikoNautsModloaderInstaller.exe'
Copy-Item -LiteralPath $exe -Destination $target -Force

# the executable must report the ModLoader it carries
$versionFile = Join-Path $publish 'version.txt'
Start-Process -FilePath $target -ArgumentList '--version' -Wait -WindowStyle Hidden -RedirectStandardOutput $versionFile
$version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
Write-Host $version -ForegroundColor DarkGray
if ($version -notmatch 'bundled ModLoader: v\d') { throw 'The executable does not contain the ModLoader.' }

$info = Get-Item -LiteralPath $target
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
Write-Host ("Built {0} ({1:N1} MB, version {2})" -f $target, ($info.Length / 1MB), $info.VersionInfo.FileVersion) -ForegroundColor Green
Write-Host "SHA256 $hash" -ForegroundColor Cyan
