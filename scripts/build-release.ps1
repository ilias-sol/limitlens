[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [switch]$SkipInstaller,
    [switch]$IncludeShowcase
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$release = Join-Path $artifacts 'release'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Version must be a SemVer value without a leading v: $Version"
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

dotnet restore (Join-Path $root 'LimitLens.slnx') --locked-mode -p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet test (Join-Path $root 'LimitLens.slnx') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
dotnet publish (Join-Path $root 'src\LimitLens.App\LimitLens.App.csproj') -c Release -r win-x64 --self-contained true --no-restore -o $publish "/p:Version=$Version" '/p:DebugType=None' '/p:DebugSymbols=false' '/p:ContinuousIntegrationBuild=true'
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Get-ChildItem -LiteralPath $publish -Filter '*.pdb' -File | Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $publish
Copy-Item -LiteralPath (Join-Path $root 'PRIVACY.md') -Destination $publish
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $publish
$packageAssets = Join-Path $publish 'src\LimitLens.App\Assets'
New-Item -ItemType Directory -Path $packageAssets -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src\LimitLens.App\Assets\LimitLens-256.png') -Destination $packageAssets
Copy-Item -LiteralPath (Join-Path $root 'src\LimitLens.App\Assets\LimitLens-showcase.png') -Destination $packageAssets

& (Join-Path $PSScriptRoot 'package-portable.ps1') -PublishDirectory $publish -OutputDirectory $release -Version $Version
if ($IncludeShowcase) {
    & (Join-Path $PSScriptRoot 'package-portable.ps1') -PublishDirectory $publish -OutputDirectory $release -Version $Version -Showcase
}
if (-not $SkipInstaller) {
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -PublishDirectory $publish -OutputDirectory $release -Version $Version
}
& (Join-Path $PSScriptRoot 'write-checksums.ps1') -ReleaseDirectory $release
$verification = @{
    ReleaseDirectory = $release
    Version = $Version
    SkipInstaller = $SkipInstaller
    IncludeShowcase = $IncludeShowcase
}
& (Join-Path $PSScriptRoot 'verify-release.ps1') @verification
}
finally {
    if (Test-Path -LiteralPath $publish) {
        Remove-Item -LiteralPath $publish -Recurse -Force
    }
}
