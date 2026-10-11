#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'StoreConfig.json'),
    [string] $PackagePath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $repositoryRoot "artifacts\store\RouterTray_$($config.Version).msixbundle"
}
$reportRoot = Join-Path $repositoryRoot 'artifacts\store-readiness\wack'
New-Item -ItemType Directory -Force -Path $reportRoot | Out-Null
$statusPath = Join-Path $reportRoot 'status.json'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    [ordered] @{
        Status = 'NotRun'
        Reason = 'Windows App Certification Kit requires an administrative PowerShell in an active Windows user session.'
        Package = [IO.Path]::GetFullPath($PackagePath)
        CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding utf8NoBOM
    throw 'Open PowerShell 7 as administrator and run this script. No WACK pass is claimed.'
}

$appCertPath = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
if (-not (Test-Path -LiteralPath $appCertPath -PathType Leaf)) {
    throw 'Windows App Certification Kit is not installed. Install it through Windows SDK setup.'
}
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) { throw "Package not found: $PackagePath." }
$reset = Start-Process -FilePath $appCertPath -ArgumentList 'reset' -WindowStyle Hidden -PassThru -Wait
if ($reset.ExitCode -ne 0) { throw "WACK reset failed with exit code $($reset.ExitCode)." }
$reportPath = Join-Path $reportRoot 'RouterTray-WACK.xml'
$arguments = @('test', '-appxpackagepath', ('"' + [IO.Path]::GetFullPath($PackagePath) + '"'),
    '-reportoutputpath', ('"' + $reportPath + '"'))
$test = Start-Process -FilePath $appCertPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
# A zero process exit code alone is not a certification pass. Read the report.
[ordered] @{
    Status = 'ReviewReport'
    ExitCode = $test.ExitCode
    Report = $reportPath
    ReportCreated = Test-Path -LiteralPath $reportPath
    CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
} | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding utf8NoBOM
if ($test.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath)) {
    throw "WACK did not complete successfully. Inspect $reportRoot; a local test installation may require a signed test copy."
}
Write-Host "WACK report: $reportPath. Review all mandatory test results before submitting."
