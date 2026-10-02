param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project = Join-Path $root 'src\TaikoNautsModloaderInstaller\TaikoNautsModloaderInstaller.csproj'
$dist = Join-Path $root 'dist'
$publish = Join-Path $root 'build\publish'

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

$info = Get-Item -LiteralPath $target
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
Write-Host ("Built {0} ({1:N1} MB, version {2})" -f $target, ($info.Length / 1MB), $info.VersionInfo.FileVersion) -ForegroundColor Green
Write-Host "SHA256 $hash" -ForegroundColor Cyan
