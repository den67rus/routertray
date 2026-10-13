#Requires -Version 7.3
[CmdletBinding()]
param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'StoreConfig.json'),
    [string] $Version,
    [switch] $UseExistingPackage,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
& (Join-Path $PSScriptRoot 'Update-PrivacyPolicy.ps1') -Check
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
if (-not [string]::IsNullOrWhiteSpace($Version)) { $config.Version = $Version }
foreach ($field in @('Version', 'IdentityName', 'Publisher', 'PublisherDisplayName', 'DisplayName')) {
    if ([string]::IsNullOrWhiteSpace($config.$field)) { throw "Fill in $field in $ConfigPath." }
}
if ($config.Version -notmatch '^[1-9][0-9]{0,4}\.[0-9]{1,5}\.[0-9]{1,5}\.0$' -or
    @($config.Version.Split('.') | Where-Object { [int] $_ -gt 65535 }).Count -gt 0) {
    throw 'Version must be Major.Minor.Build.0 with components at most 65535.'
}

if (-not $SkipTests) {
    dotnet restore (Join-Path $repositoryRoot 'tests\RouterTray.Tests\RouterTray.Tests.csproj') `
        -p:NuGetAudit=true -p:NuGetAuditMode=all -p:TreatWarningsAsErrors=true
    dotnet build (Join-Path $repositoryRoot 'src\RouterTray\RouterTray.csproj') -c Release --no-restore -warnaserror
    dotnet test (Join-Path $repositoryRoot 'tests\RouterTray.Tests\RouterTray.Tests.csproj') `
        -c Release --no-restore --logger 'trx;LogFileName=store-preparation.trx' `
        --results-directory (Join-Path $repositoryRoot 'artifacts\store-readiness\tests')
}
if (-not $UseExistingPackage) {
    $buildParameters = @{
        Version = $config.Version
        IdentityName = $config.IdentityName
        Publisher = $config.Publisher
        PublisherDisplayName = $config.PublisherDisplayName
        DisplayName = $config.DisplayName
        KeepPackageLayout = $true
    }
    & (Join-Path $PSScriptRoot 'Build-StorePackage.ps1') @buildParameters
}

$packagePath = Join-Path $repositoryRoot "artifacts\store\RouterTray_$($config.Version).msixupload"
$publicationRoot = Join-Path $repositoryRoot 'artifacts\store-publication'
$kitRoot = Join-Path $publicationRoot "RouterTray-Store-$($config.Version)"
$resolvedKitRoot = [IO.Path]::GetFullPath($kitRoot)
$allowedRoot = [IO.Path]::GetFullPath($publicationRoot).TrimEnd([char[]] @('\', '/')) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedKitRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publication output is outside its expected artifacts directory.'
}
if (Test-Path -LiteralPath $resolvedKitRoot) {
    Remove-Item -LiteralPath $resolvedKitRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $kitRoot | Out-Null
$effectiveConfigPath = Join-Path $kitRoot 'StoreConfig.json'
$config | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $effectiveConfigPath -Encoding utf8NoBOM

$validationReportPath = Join-Path $kitRoot 'package-validation.json'
& (Join-Path $PSScriptRoot 'Test-StoreSubmission.ps1') `
    -ConfigPath $effectiveConfigPath -PackagePath $packagePath -ReportPath $validationReportPath

Copy-Item -LiteralPath $packagePath -Destination $kitRoot
$listingSource = Join-Path $repositoryRoot 'docs\store-assets\partner-center-upload'
$listingDestination = Join-Path $kitRoot 'partner-center-upload'
New-Item -ItemType Directory -Force -Path $listingDestination | Out-Null
Copy-Item -LiteralPath (Join-Path $listingSource 'listingData.csv') -Destination $listingDestination
foreach ($image in (Get-ChildItem -LiteralPath $listingSource -Recurse -File -Filter '*.png')) {
    $relativePath = [IO.Path]::GetRelativePath($listingSource, $image.FullName)
    $imageDestination = Join-Path $listingDestination $relativePath
    New-Item -ItemType Directory -Force -Path (Split-Path $imageDestination -Parent) | Out-Null
    Copy-Item -LiteralPath $image.FullName -Destination $imageDestination
}
$localNotesRoot = Join-Path $publicationRoot 'notes'
foreach ($name in @('CERTIFICATION_NOTES.txt', 'PUBLICATION_GUIDE_RU.md')) {
    $localNote = Join-Path $localNotesRoot $name
    if (Test-Path -LiteralPath $localNote -PathType Leaf) {
        Copy-Item -LiteralPath $localNote -Destination $kitRoot
    }
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'PRIVACY.md') -Destination $kitRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\privacy\index.html') -Destination (Join-Path $kitRoot 'privacy.html')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $kitRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $kitRoot
$testReportPath = Join-Path $repositoryRoot 'artifacts\store-readiness\tests\store-preparation.trx'
if (Test-Path -LiteralPath $testReportPath) {
    Copy-Item -LiteralPath $testReportPath -Destination $kitRoot
}
$wackStatusPath = Join-Path $repositoryRoot 'artifacts\store-readiness\wack\status.json'
if (Test-Path -LiteralPath $wackStatusPath) {
    Copy-Item -LiteralPath $wackStatusPath -Destination (Join-Path $kitRoot 'wack-status.json')
}

$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
"$packageHash  $([IO.Path]::GetFileName($packagePath))" |
    Set-Content -LiteralPath (Join-Path $kitRoot 'SHA256SUMS.txt') -Encoding utf8NoBOM
$privacyStatus = if ([string]::IsNullOrWhiteSpace($config.PrivacyPolicyUrl)) {
    'Нужно публично разместить PRIVACY.md или privacy.html и внести действующий URL в Partner Center.'
} else {
    "Privacy URL: $($config.PrivacyPolicyUrl). Проверьте доступность без авторизации."
}
$readme = @"
RouterTray $($config.Version) — комплект для Microsoft Store

1. Packages: загрузите ТОЛЬКО RouterTray_$($config.Version).msixupload.
2. Store listings: Import folder → выберите папку partner-center-upload целиком.
3. Notes for certification: вставьте CERTIFICATION_NOTES.txt.
4. $privacyStatus
5. Support URL: $($config.SupportUrl)
6. Проверьте WACK и работу на совместимом роутере перед отправкой заявки.

Проверка пакетов и карточки: package-validation.json.
Документы не подтверждают сертификацию Microsoft.
Store ID: $($config.ProductId)
Identity: $($config.IdentityName)
Publisher: $($config.Publisher)
SHA-256 пакета: $packageHash

ZIP является архивом этого комплекта. Для публикации сначала распакуйте его.
"@
$readme | Set-Content -LiteralPath (Join-Path $kitRoot 'START_HERE.txt') -Encoding utf8BOM
$zipPath = "$kitRoot.zip"
Compress-Archive -LiteralPath $kitRoot -DestinationPath $zipPath -CompressionLevel Optimal -Force
Write-Host "Publication folder: $kitRoot"
Write-Host "Publication archive: $zipPath"
Write-Host $privacyStatus
