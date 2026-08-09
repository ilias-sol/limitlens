[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReleaseDirectory,
    [Parameter(Mandatory)]
    [string]$Version,
    [switch]$SkipInstaller,
    [switch]$IncludeShowcase
)

$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$expected = [Collections.Generic.List[string]]::new()
$expected.Add("LimitLens-$Version-portable-win-x64.zip")
if ($IncludeShowcase) {
    $expected.Add("LimitLens-$Version-showcase-win-x64.zip")
}
if (-not $SkipInstaller) {
    $expected.Add("LimitLens-Setup-$Version-win-x64.exe")
}

foreach ($name in $expected) {
    if (-not (Test-Path -LiteralPath (Join-Path $release $name) -PathType Leaf)) {
        throw "Missing release artifact: $name"
    }
}

$unexpectedPayload = @(Get-ChildItem -LiteralPath $release -File |
    Where-Object { $_.Extension -in '.exe', '.zip' -and $_.Name -notin $expected } |
    ForEach-Object Name)
if ($unexpectedPayload.Count -gt 0) {
    throw "Unexpected release payload: $($unexpectedPayload -join ', ')"
}

$checksumPath = Join-Path $release 'SHA256SUMS.txt'
$checksumLines = @(Get-Content -LiteralPath $checksumPath)
if ($checksumLines.Count -ne $expected.Count) {
    throw "Expected $($expected.Count) checksum entries, found $($checksumLines.Count)."
}

$checksumNames = [Collections.Generic.List[string]]::new()
foreach ($line in $checksumLines) {
    if ($line -notmatch '^([a-f0-9]{64}) \*(.+)$') {
        throw 'SHA256SUMS.txt contains an invalid line.'
    }

    $checksumName = $Matches[2]
    if ($checksumName -notin $expected) {
        throw "Checksum references an unexpected file: $checksumName"
    }
    if ($checksumNames.Contains($checksumName)) {
        throw "Duplicate checksum entry: $checksumName"
    }
    $checksumNames.Add($checksumName)

    $path = Join-Path $release $checksumName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Checksum references a missing file: $checksumName"
    }

    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Matches[1]) {
        throw "Checksum mismatch: $checksumName"
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$variants = [Collections.Generic.List[string]]::new()
$variants.Add('portable')
if ($IncludeShowcase) {
    $variants.Add('showcase')
}

foreach ($variant in $variants) {
    $zipPath = Join-Path $release "LimitLens-$Version-$variant-win-x64.zip"
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "LimitLens-verify-$PID-$([Guid]::NewGuid().ToString('N'))"
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
        if ($entries -notcontains 'LimitLens.exe' -or $entries -notcontains 'portable.flag') {
            throw "$variant ZIP is missing LimitLens.exe or portable.flag."
        }
        if ($variant -eq 'showcase' -and $entries -notcontains 'showcase.flag') {
            throw 'Showcase ZIP is missing showcase.flag.'
        }
        if ($variant -eq 'portable' -and $entries -contains 'showcase.flag') {
            throw 'Portable ZIP unexpectedly contains showcase.flag.'
        }
        if ($entries | Where-Object { $_ -match '^(?:Data[/\\]|.*\.(?:pdb|user)$)' }) {
            throw "$variant ZIP contains private data or development files."
        }
        if ($entries | Where-Object { $_ -match '(?i)codex.?usage' }) {
            throw "$variant ZIP contains a legacy product name."
        }
        foreach ($documentationAsset in @(
            'src/LimitLens.App/Assets/LimitLens-256.png',
            'src/LimitLens.App/Assets/LimitLens-showcase.png')) {
            if ($entries -notcontains $documentationAsset) {
                throw "$variant ZIP is missing README asset: $documentationAsset"
            }
        }

        New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
        $temporaryExecutable = Join-Path $temporaryDirectory 'LimitLens.exe'
        $executableEntry = $archive.GetEntry('LimitLens.exe')
        [IO.Compression.ZipFileExtensions]::ExtractToFile($executableEntry, $temporaryExecutable, $true)
        & (Join-Path $PSScriptRoot 'audit-dotnet-bundle.ps1') -ExecutablePath $temporaryExecutable
    }
    finally {
        $archive.Dispose()
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }
}

if (-not $SkipInstaller) {
    $installerInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo(
        (Join-Path $release "LimitLens-Setup-$Version-win-x64.exe"))
    if ($installerInfo.ProductName.Trim() -ne 'Limit Lens') {
        throw "Unexpected installer product name: $($installerInfo.ProductName)"
    }
}

Write-Output "Release verification passed for Limit Lens $Version."
