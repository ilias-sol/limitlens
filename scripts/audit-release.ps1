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
    @{ Name = 'credential assignment'; Pattern = '(?im)\b(?:api[_-]?key|client[_-]?secret|access[_-]?token|password)\b\s*[:=]\s*["'']?[^\s"''${}<>]{8,}' },
    @{ Name = 'personal email'; Pattern = '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b' },
    @{ Name = 'absolute user path'; Pattern = '(?i)\b[A-Z]:\\Users\\[^\\\s]+' }
)

$findings = [Collections.Generic.List[object]]::new()
$binaryExtensions = @('.ico', '.png', '.jpg', '.jpeg', '.gif', '.zip', '.exe', '.dll')
$tracked = @(git -C $root ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not enumerate tracked files.'
}

foreach ($relative in $tracked) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        $binaryExtensions -contains [IO.Path]::GetExtension($path).ToLowerInvariant()) {
        continue
    }

    $content = [IO.File]::ReadAllText($path)
    foreach ($entry in $patterns) {
        foreach ($match in [regex]::Matches($content, $entry.Pattern)) {
            if ($entry.Name -eq 'personal email' -and $match.Value.EndsWith('.invalid', [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            $line = 1 + [regex]::Matches($content.Substring(0, $match.Index), "`n").Count
            $findings.Add([pscustomobject]@{
                Kind = $entry.Name
                File = $relative
                Line = $line
            })
        }
    }
}

if ($IncludeGitMetadata) {
    $expectedIdentity = 'Limit Lens Contributors <contributors@limitlens.invalid>'
    $identities = @(git -C $root log --all --format='%an <%ae>' | Sort-Object -Unique)
    foreach ($identity in $identities) {
        if ($identity -ne $expectedIdentity) {
            $findings.Add([pscustomobject]@{
                Kind = 'unexpected Git author identity'
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

Write-Output "Release audit passed: $($tracked.Count) tracked files checked; no credentials, personal emails, or absolute user paths found."
