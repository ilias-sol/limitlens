[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$release = Join-Path $artifacts 'release'
$portableStage = Join-Path $release "LimitLens-$Version-portable-win-x64"
$portableData = Join-Path $portableStage 'Data'
$dataBackup = $null

if (Test-Path -LiteralPath $portableData) {
    $dataBackup = Join-Path ([System.IO.Path]::GetTempPath()) "LimitLens-build-data-$PID"
    New-Item -ItemType Directory -Path $dataBackup -Force | Out-Null
    Copy-Item -Path (Join-Path $portableData '*') -Destination $dataBackup -Recurse -Force
}

try {
foreach ($target in @($publish, $release)) {
    if (Test-Path -LiteralPath $target) {
        $resolved = (Resolve-Path -LiteralPath $target).Path
        if (-not $resolved.StartsWith($artifacts, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clear unexpected artifact path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
}

dotnet restore (Join-Path $root 'LimitLens.slnx') --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet test (Join-Path $root 'LimitLens.slnx') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
dotnet publish (Join-Path $root 'src\LimitLens.App\LimitLens.App.csproj') -c Release -r win-x64 --self-contained true --no-restore -o $publish "/p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Get-ChildItem -LiteralPath $publish -Filter '*.pdb' -File | Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $publish
Copy-Item -LiteralPath (Join-Path $root 'PRIVACY.md') -Destination $publish
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $publish

& (Join-Path $PSScriptRoot 'package-portable.ps1') -PublishDirectory $publish -OutputDirectory $release -Version $Version
& (Join-Path $PSScriptRoot 'package-portable.ps1') -PublishDirectory $publish -OutputDirectory $release -Version $Version -Showcase
if (-not $SkipInstaller) {
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -PublishDirectory $publish -OutputDirectory $release -Version $Version
}
& (Join-Path $PSScriptRoot 'write-checksums.ps1') -ReleaseDirectory $release
& (Join-Path $PSScriptRoot 'verify-release.ps1') -ReleaseDirectory $release -Version $Version
}
finally {
    if ($dataBackup -and (Test-Path -LiteralPath $dataBackup)) {
        New-Item -ItemType Directory -Path $portableData -Force | Out-Null
        Copy-Item -Path (Join-Path $dataBackup '*') -Destination $portableData -Recurse -Force
        Remove-Item -LiteralPath $dataBackup -Recurse -Force
    }
    if (Test-Path -LiteralPath $publish) {
        Remove-Item -LiteralPath $publish -Recurse -Force
    }
}
