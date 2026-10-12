#Requires -Version 7.3
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$generator = Join-Path $PSScriptRoot 'New-ReleaseBody.ps1'
$templatePath = Join-Path $PSScriptRoot '..\release-template.md'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "routertray-release-tests-$([Guid]::NewGuid())"
New-Item -ItemType Directory -Path $testRoot | Out-Null
$dataPath = Join-Path $testRoot 'release.json'
$outputPath = Join-Path $testRoot 'body.md'
$relativeOutputPath = Join-Path $testRoot 'relative-body.md'
$customTemplatePath = Join-Path $testRoot 'template.md'
$startMarker = '<!-- routertray-downloads:start -->'
$endMarker = '<!-- routertray-downloads:end -->'
$blockPattern = '(?s)' + [Regex]::Escape($startMarker) + '.*?' + [Regex]::Escape($endMarker)
$expectedDownloads = [ordered] @{
    'Download x64 installer EXE' = 'RouterTray.App-win-x64-Setup.exe'
    'Download x64 portable ZIP' = 'RouterTray.App-win-x64-Portable.zip'
    'Download ARM64 installer EXE' = 'RouterTray.App-win-arm64-Setup.exe'
    'Download ARM64 portable ZIP' = 'RouterTray.App-win-arm64-Portable.zip'
    'Download x86 installer EXE' = 'RouterTray.App-win-x86-Setup.exe'
    'Download x86 portable ZIP' = 'RouterTray.App-win-x86-Portable.zip'
}
$passed = 0

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function New-ReleaseFixture([string] $Tag, [string] $Notes = '') {
    $assets = @(
        foreach ($target in @('arm64', 'x86', 'x64')) {
            foreach ($package in @('Portable.zip', 'Setup.exe')) {
                $name = "RouterTray.App-win-$target-$package"
                [PSCustomObject] @{
                    name = $name
                    state = 'uploaded'
                    browser_download_url = "https://github.com/fixtures/routertray/releases/download/$Tag/$name"
                }
            }
        }
        [PSCustomObject] @{
            name = 'releases.win-x64.json'
            state = 'uploaded'
            browser_download_url = "https://github.com/fixtures/routertray/releases/download/$Tag/releases.win-x64.json"
        }
    )
    return [PSCustomObject] @{ tag_name = $Tag; body = $Notes; assets = $assets }
}

function Invoke-Generator($Release, [string] $Template = $templatePath) {
    $Release | ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath $dataPath -Encoding utf8NoBOM
    & $generator -ReleaseDataPath $dataPath -OutputPath $outputPath -TemplatePath $Template
    return [IO.File]::ReadAllText($outputPath)
}

function Assert-GeneratedDownloads([string] $Body, $Release) {
    Assert-True ([Regex]::Matches($Body, [Regex]::Escape($startMarker)).Count -eq 1) 'Expected one generated block start.'
    Assert-True ([Regex]::Matches($Body, [Regex]::Escape($endMarker)).Count -eq 1) 'Expected one generated block end.'
    $block = [Regex]::Match($Body, $blockPattern).Value
    Assert-True ($block.Contains("<!-- Release: $($Release.tag_name) -->")) 'The current release tag was not substituted.'
    Assert-True ($block -notmatch '\{\{[A-Z][A-Z0-9_]*\}\}') 'The download block contains unresolved placeholders.'
    foreach ($download in $expectedDownloads.GetEnumerator()) {
        $anchors = @([Regex]::Matches($block, '(?s)<a\b[^>]*>.*?</a>') |
            Where-Object { $_.Value.Contains('alt="' + $download.Key + '"') })
        Assert-True ($anchors.Count -eq 1) "Expected one download button: $($download.Key)."
        $actualUrl = [Net.WebUtility]::HtmlDecode([Regex]::Match($anchors[0].Value, 'href="([^"]+)"').Groups[1].Value)
        $asset = $Release.assets | Where-Object name -EQ $download.Value
        Assert-True ($actualUrl -ceq $asset.browser_download_url) "The wrong package is linked by $($download.Key)."
    }
}

function Assert-Rejected($Release, [string] $ExpectedMessage, [string] $Template = $templatePath) {
    [IO.File]::WriteAllText($outputPath, 'Existing output must survive invalid release data.')
    $failure = $null
    try { Invoke-Generator $Release $Template | Out-Null } catch { $failure = $_ }
    Assert-True ($null -ne $failure) 'Invalid release data was accepted.'
    Assert-True ($failure.Exception.Message -like $ExpectedMessage) "Unexpected failure: $($failure.Exception.Message)"
    Assert-True ([IO.File]::ReadAllText($outputPath) -ceq 'Existing output must survive invalid release data.') 'Invalid data overwrote the output.'
}

try {
    $notes = "## Изменения`r`n`r`nЗаметки автора и буквальные {{RELEASE_TAG}} / {{X64_SETUP_URL}}."
    $stable = New-ReleaseFixture 'v2.3.4' $notes
    $stableBody = Invoke-Generator $stable
    Assert-GeneratedDownloads $stableBody $stable
    Assert-True ($stableBody.Contains($notes.Replace("`r`n", "`n"))) 'Author notes or their literal placeholders changed.'
    $passed++

    $beta = New-ReleaseFixture 'v2.4.0-beta7'
    $betaBody = Invoke-Generator $beta
    Assert-GeneratedDownloads $betaBody $beta
    Assert-True ($betaBody -notmatch 'v1\.2\.0-beta2|v2\.3\.4') 'A previous release version leaked into the new release.'
    $passed++

    $stable.body = $stableBody
    $repeatedBody = Invoke-Generator $stable
    Assert-True ($repeatedBody -ceq $stableBody) 'A repeated run changed or duplicated the release description.'
    $passed++

    $beta.body = "Notes before.`n`n$startMarker`nOld download table for v1.2.0-beta2.`n$endMarker`n`nNotes after."
    $updatedBody = Invoke-Generator $beta
    Assert-GeneratedDownloads $updatedBody $beta
    Assert-True ($updatedBody.Contains('Notes before.') -and $updatedBody.Contains('Notes after.')) 'Notes around an existing table were lost.'
    Assert-True ($updatedBody -notmatch 'Old download table|v1\.2\.0-beta2') 'The obsolete download block survived.'
    $passed++

    $escaped = New-ReleaseFixture 'v3.0.0'
    $escaped.assets[0].browser_download_url += '?download=1&source=release'
    $escapedBody = Invoke-Generator $escaped
    Assert-GeneratedDownloads $escapedBody $escaped
    Assert-True ($escapedBody.Contains('?download=1&amp;source=release')) 'An asset URL was not escaped for its HTML attribute.'
    $passed++

    Push-Location $testRoot
    try {
        & $generator -ReleaseDataPath 'release.json' -OutputPath 'relative-body.md'
        Assert-True (Test-Path -LiteralPath $relativeOutputPath) 'A relative output path did not use the PowerShell working directory.'
        Assert-GeneratedDownloads ([IO.File]::ReadAllText($relativeOutputPath)) $escaped
        $passed++
    } finally {
        Pop-Location
    }

    $invalidCases = @(
        @{ Change = { param($release) $release.tag_name = '' }; Error = '*tag name*' }
        @{ Change = { param($release) $release.assets = @($release.assets | Where-Object name -NE 'RouterTray.App-win-x86-Portable.zip') }; Error = '*one uploaded release asset*' }
        @{ Change = { param($release) $release.assets += $release.assets[0] }; Error = '*one uploaded release asset*' }
        @{ Change = { param($release) $release.assets[0].state = 'starter' }; Error = '*one uploaded release asset*' }
        @{ Change = { param($release) $release.assets[0].browser_download_url = 'http://example.invalid/package.zip' }; Error = '*invalid download URL*' }
        @{ Change = { param($release) $release.body = '<!-- routertray-downloads:start -->' }; Error = '*incomplete generated download block*' }
        @{ Change = { param($release) $release.body = '<!-- routertray-downloads:end --><!-- routertray-downloads:start -->' }; Error = '*incomplete generated download block*' }
        @{ Change = { param($release) $release.body = '<!-- routertray-downloads:start --><!-- routertray-downloads:end --><!-- routertray-downloads:start --><!-- routertray-downloads:end -->' }; Error = '*incomplete generated download block*' }
    )
    foreach ($case in $invalidCases) {
        $invalid = New-ReleaseFixture 'v4.0.0'
        & $case.Change $invalid
        Assert-Rejected $invalid $case.Error
        $passed++
    }

    $template = Get-Content -LiteralPath $templatePath -Raw
    $template.Replace('{{X64_SETUP_URL}}', '') |
        Set-Content -LiteralPath $customTemplatePath -Encoding utf8NoBOM
    Assert-Rejected (New-ReleaseFixture 'v4.0.0') '*template is missing*' $customTemplatePath
    $passed++

    ($template + "`n{{UNKNOWN_URL}}") |
        Set-Content -LiteralPath $customTemplatePath -Encoding utf8NoBOM
    Assert-Rejected (New-ReleaseFixture 'v4.0.0') '*Unknown release template placeholder*' $customTemplatePath
    $passed++

    Write-Host "Passed $passed release description checks: versions, six package links, notes, reruns, and invalid input."
} finally {
    foreach ($testFile in @($dataPath, $outputPath, $relativeOutputPath, $customTemplatePath)) {
        if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
    }
    Remove-Item -LiteralPath $testRoot
}
