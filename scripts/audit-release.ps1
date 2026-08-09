[CmdletBinding()]
param(
    [switch]$IncludeGitMetadata
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$patterns = @(
    @{ Name = 'private key'; Pattern = '-----BEGIN (?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----' },
    @{ Name = 'OpenAI-style token'; Pattern = '\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b' },
    @{ Name = 'GitHub token'; Pattern = '\b(?:github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{20,})\b' },
    @{ Name = 'AWS access key'; Pattern = '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b' },
    @{ Name = 'Slack token'; Pattern = '\bxox[baprs]-[A-Za-z0-9-]{20,}\b' },
    @{ Name = 'Stripe live key'; Pattern = '\b[rs]k_live_[A-Za-z0-9]{16,}\b' },
    @{ Name = 'Google API key'; Pattern = '\bAIza[0-9A-Za-z_-]{30,}\b' },
    @{ Name = 'npm token'; Pattern = '\bnpm_[A-Za-z0-9]{30,}\b' },
    @{ Name = 'JWT-like token'; Pattern = '\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b' },
    @{ Name = 'cloud account key'; Pattern = '(?i)\bAccountKey\s*=\s*[A-Za-z0-9+/=]{20,}' },
    @{ Name = 'credential assignment'; Pattern = '(?im)\b(?:api[_-]?key|client[_-]?secret|access[_-]?token|password)\b\s*[:=]\s*["'']?[^\s"''${}<>]{8,}' },
    @{ Name = 'personal email'; Pattern = '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b' },
    @{ Name = 'absolute user path'; Pattern = '(?i)\b[A-Z]:\\Users\\[^\\\s]+' },
    @{ Name = 'unexpected copyright holder'; Pattern = '(?im)^Copyright \(c\) \d{4} (?!Limit Lens contributors\s*$).+$' }
)
$binaryExtensions = @(
    '.ico', '.png', '.jpg', '.jpeg', '.gif', '.zip', '.exe', '.dll', '.pdb',
    '.db', '.sqlite', '.sqlite3', '.pfx', '.p12', '.kdbx')
$forbiddenTrackedFiles = @(
    @{ Name = 'tracked environment file'; Pattern = '(?i)(?:^|/)\.env(?:$|\.)' },
    @{ Name = 'tracked private-key container'; Pattern = '(?i)\.(?:pem|key|pfx|p12|kdbx)$' },
    @{ Name = 'tracked local database'; Pattern = '(?i)\.(?:db|db-wal|db-shm|sqlite|sqlite3)$' },
    @{ Name = 'tracked build/release binary'; Pattern = '(?i)\.(?:exe|zip|nupkg|snupkg|pdb)$' },
    @{ Name = 'tracked local data directory'; Pattern = '(?i)(?:^|/)Data/' }
)
$findings = [Collections.Generic.List[object]]::new()

function Add-ContentFindings {
    param(
        [Parameter(Mandatory)]
        [string]$Content,
        [Parameter(Mandatory)]
        [string]$DisplayPath,
        [string]$KindSuffix = ''
    )

    foreach ($entry in $patterns) {
        foreach ($match in [regex]::Matches($Content, $entry.Pattern)) {
            if ($entry.Name -eq 'personal email' -and
                $match.Value.EndsWith('.invalid', [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            $line = 1 + [regex]::Matches($Content.Substring(0, $match.Index), "`n").Count
            $findings.Add([pscustomobject]@{
                Kind = "$($entry.Name)$KindSuffix"
                File = $DisplayPath
                Line = $line
            })
        }
    }
}

$trackedFiles = @(git -C $root ls-files --cached)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not enumerate tracked files.'
}
$currentFiles = @(git -C $root ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not enumerate current release candidates.'
}

foreach ($relative in $trackedFiles) {
    foreach ($entry in $forbiddenTrackedFiles) {
        if ($relative -match $entry.Pattern) {
            $findings.Add([pscustomobject]@{
                Kind = $entry.Name
                File = $relative
                Line = 0
            })
        }
    }
}

foreach ($relative in $currentFiles) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        $binaryExtensions -contains [IO.Path]::GetExtension($path).ToLowerInvariant()) {
        continue
    }

    Add-ContentFindings -Content ([IO.File]::ReadAllText($path)) -DisplayPath $relative
}

$commits = @(git -C $root rev-list --all)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not enumerate Git history.'
}
foreach ($commit in $commits) {
    $shortCommit = $commit.Substring(0, [Math]::Min(7, $commit.Length))
    foreach ($relative in @(git -C $root ls-tree -r --name-only $commit)) {
        if ($binaryExtensions -contains [IO.Path]::GetExtension($relative).ToLowerInvariant()) {
            continue
        }

        $content = @(git -C $root show "$($commit):$relative" 2>$null) -join "`n"
        if ($LASTEXITCODE -ne 0) {
            throw "Could not inspect $relative in commit $shortCommit."
        }
        Add-ContentFindings `
            -Content $content `
            -DisplayPath "$shortCommit`:$relative" `
            -KindSuffix ' in Git history'
    }
}

if ($IncludeGitMetadata) {
    $allowedIdentityPatterns = @(
        '^Limit Lens Contributors <contributors@limitlens\.invalid>$',
        '^[^<\r\n]+ <(?:\d+\+)?[A-Za-z0-9-]+@users\.noreply\.github\.com>$',
        '^GitHub <noreply@github\.com>$'
    )
    $identities = @(
        @(git -C $root log --all --format='%an <%ae>')
        @(git -C $root log --all --format='%cn <%ce>')
    ) | Where-Object { $_ } | Sort-Object -Unique
    foreach ($identity in $identities) {
        $allowed = $allowedIdentityPatterns | Where-Object { $identity -match $_ }
        if (-not $allowed) {
            $findings.Add([pscustomobject]@{
                Kind = 'unexpected Git identity'
                File = '.git history'
                Line = 0
            })
        }
    }

    if (@(git -C $root remote) -contains 'origin') {
        foreach ($remote in @(git -C $root remote get-url --all origin)) {
            if ($remote -match 'https://[^/@:]+:[^/@]+@') {
                $findings.Add([pscustomobject]@{
                    Kind = 'credential-bearing Git remote'
                    File = '.git/config'
                    Line = 0
                })
            }
        }
    }
}

if ($findings.Count -gt 0) {
    $findings | Sort-Object Kind, File, Line -Unique | Format-Table -AutoSize | Out-String | Write-Error
    throw "Release audit failed with $($findings.Count) potential finding(s). Values were redacted."
}

Write-Output "Release audit passed: $($currentFiles.Count) current files and $($commits.Count) reachable commits checked; no credentials, personal emails, absolute user paths, or forbidden tracked artifacts found."
