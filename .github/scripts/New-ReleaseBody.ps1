#Requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ReleaseDataPath,
    [Parameter(Mandatory)]
    [string] $OutputPath,
    [string] $TemplatePath = (Join-Path $PSScriptRoot '..\release-template.md')
)

$ErrorActionPreference = 'Stop'
$release = Get-Content -LiteralPath $ReleaseDataPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($release.tag_name)) {
    throw 'Release data must include its tag name.'
}

$values = @{
    RELEASE_TAG = [Net.WebUtility]::HtmlEncode([string] $release.tag_name)
}
$downloads = [ordered] @{
    X64_SETUP_URL = 'RouterTray.App-win-x64-Setup.exe'
    X64_PORTABLE_URL = 'RouterTray.App-win-x64-Portable.zip'
    ARM64_SETUP_URL = 'RouterTray.App-win-arm64-Setup.exe'
    ARM64_PORTABLE_URL = 'RouterTray.App-win-arm64-Portable.zip'
    X86_SETUP_URL = 'RouterTray.App-win-x86-Setup.exe'
    X86_PORTABLE_URL = 'RouterTray.App-win-x86-Portable.zip'
}
foreach ($download in $downloads.GetEnumerator()) {
    $assets = @($release.assets | Where-Object name -CEQ $download.Value)
    if ($assets.Count -ne 1 -or $assets[0].state -ne 'uploaded') {
        throw "Expected one uploaded release asset: $($download.Value)."
    }
    $url = [string] $assets[0].browser_download_url
    $uri = $null
    if (-not [Uri]::TryCreate($url, [UriKind]::Absolute, [ref] $uri) -or $uri.Scheme -ne 'https') {
        throw "Missing or invalid download URL for $($download.Value)."
    }
    $values[$download.Key] = [Net.WebUtility]::HtmlEncode($url)
}

# Replace only the generated download block, preserving the author's release notes.
$existingBody = [string] $release.body
$startMarker = '<!-- routertray-downloads:start -->'
$endMarker = '<!-- routertray-downloads:end -->'
$startCount = [Regex]::Matches($existingBody, [Regex]::Escape($startMarker)).Count
$endCount = [Regex]::Matches($existingBody, [Regex]::Escape($endMarker)).Count
$blockPattern = '(?s)' + [Regex]::Escape($startMarker) + '.*?' + [Regex]::Escape($endMarker)
if ($startCount -gt 1 -or $endCount -gt 1 -or $startCount -ne $endCount -or
    ($startCount -eq 1 -and -not [Regex]::IsMatch($existingBody, $blockPattern))) {
    throw 'The existing release body has an incomplete generated download block.'
}
$values.RELEASE_NOTES = [Regex]::Replace($existingBody, $blockPattern, '').Trim([char[]] @("`r", "`n"))

$template = Get-Content -LiteralPath $TemplatePath -Raw
foreach ($required in (@('RELEASE_TAG', 'RELEASE_NOTES') + @($downloads.Keys))) {
    if (-not $template.Contains("{{$required}}")) {
        throw "The release template is missing {{$required}}."
    }
}
# Evaluate placeholders once so placeholder-like text in release notes stays literal.
$body = [Regex]::Replace($template, '\{\{([A-Z][A-Z0-9_]*)\}\}', [Text.RegularExpressions.MatchEvaluator] {
    param($match)
    $key = $match.Groups[1].Value
    if (-not $values.ContainsKey($key)) {
        throw "Unknown release template placeholder: {{$key}}."
    }
    return $values[$key]
})
$body = $body.Replace("`r`n", "`n").TrimEnd([char[]] @("`r", "`n")) + "`n"
$resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $resolvedOutputPath -Parent) | Out-Null
[IO.File]::WriteAllText($resolvedOutputPath, $body, [Text.UTF8Encoding]::new($false))
Write-Host "Prepared release description for $($release.tag_name) with all six downloads."
