#Requires -Version 7.3
[CmdletBinding()]
param([switch] $Check)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$sourcePath = Join-Path $repositoryRoot 'PRIVACY.md'
$htmlPath = Join-Path $repositoryRoot 'docs\privacy\index.html'
$markdown = [IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n").TrimEnd() + "`n"
if (-not $markdown.StartsWith('# RouterTray Privacy Policy') -or $markdown -match '\p{IsCyrillic}') {
    throw 'PRIVACY.md must contain the English RouterTray privacy policy only.'
}
$sourceHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($markdown)))
$body = (ConvertFrom-Markdown -InputObject $markdown).Html.TrimEnd()
$html = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="routertray-policy-sha256" content="$sourceHash">
<title>RouterTray Privacy Policy</title>
<style>
body { margin: 0 auto; max-width: 850px; padding: 32px 24px; font: 17px/1.65 system-ui, sans-serif; color: #17202a; background: #fff; }
h1 { font-size: 30px; line-height: 1.25; } h2 { font-size: 22px; margin-top: 32px; }
a { color: #075aa5; } code { overflow-wrap: anywhere; font-size: .9em; } main { overflow-wrap: anywhere; }
@media (prefers-color-scheme: dark) { body { color: #eceff3; background: #161a20; } a { color: #81bcff; } }
</style>
</head>
<body>
<main>
$body
</main>
</body>
</html>
"@
$html = $html.Replace("`r`n", "`n").TrimEnd() + "`n"
if ($Check) {
    if (-not (Test-Path -LiteralPath $htmlPath -PathType Leaf) -or
        [IO.File]::ReadAllText($htmlPath).Replace("`r`n", "`n") -cne $html) {
        throw 'The bundled privacy HTML is stale. Run Update-PrivacyPolicy.ps1 before building.'
    }
} else {
    New-Item -ItemType Directory -Force -Path (Split-Path $htmlPath -Parent) | Out-Null
    [IO.File]::WriteAllText($htmlPath, $html, [Text.UTF8Encoding]::new($false))
}
Write-Host "Privacy policy: English; source SHA256 $sourceHash."
