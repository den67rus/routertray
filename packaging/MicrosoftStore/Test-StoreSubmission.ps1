[CmdletBinding()]
param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'StoreConfig.json'),
    [string] $PackagePath,
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.Drawing
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
& (Join-Path $PSScriptRoot 'Update-PrivacyPolicy.ps1') -Check
$expectedPrivacy = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'docs\privacy\index.html')).Replace("`r`n", "`n")
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $repositoryRoot "artifacts\store\RouterTray_$($config.Version).msixupload"
}
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $repositoryRoot 'artifacts\store-readiness\package-validation.json'
}
$expectedLanguages = @('en-US', 'da-DK', 'de-DE', 'es-ES', 'fi-FI', 'fr-FR', 'it-IT',
    'pl-PL', 'pt-PT', 'ru-RU', 'sv-SE', 'tr-TR', 'uk-UA')

function Assert-Submission([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Read-EntryText($Archive, [string] $Name) {
    $entry = $Archive.GetEntry($Name)
    Assert-Submission ($null -ne $entry) "Missing package entry: $Name."
    $reader = [IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Open-NestedArchive($Entry) {
    $buffer = [IO.MemoryStream]::new()
    $stream = $Entry.Open()
    try { $stream.CopyTo($buffer) } finally { $stream.Dispose() }
    $buffer.Position = 0
    return [IO.Compression.ZipArchive]::new($buffer, [IO.Compression.ZipArchiveMode]::Read, $false)
}

$architectures = [Collections.Generic.List[object]]::new()
$upload = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($PackagePath))
try {
    $bundles = @($upload.Entries | Where-Object { $_.Name -like '*.msixbundle' })
    Assert-Submission ($bundles.Count -eq 1) 'Upload must contain exactly one .msixbundle.'
    Assert-Submission (@($upload.Entries | Where-Object { $_.Name -like '*.msix' }).Count -eq 0) `
        'Upload must not contain standalone MSIX packages alongside the bundle.'
    $bundle = Open-NestedArchive $bundles[0]
    try {
        [xml] $bundleManifest = Read-EntryText $bundle 'AppxMetadata/AppxBundleManifest.xml'
        $bundleIdentity = $bundleManifest.DocumentElement.Identity
        Assert-Submission ($bundleIdentity.Name -ceq $config.IdentityName) 'Bundle identity differs from Partner Center.'
        Assert-Submission ($bundleIdentity.Publisher -ceq $config.Publisher) 'Bundle publisher differs from Partner Center.'
        Assert-Submission ($bundleIdentity.Version -eq $config.Version) 'Bundle version differs from configuration.'
        $bundlePackages = @($bundleManifest.DocumentElement.Packages.Package)
        Assert-Submission ($bundlePackages.Count -eq 3) 'Expected x86, x64 and ARM64 application packages.'
        foreach ($item in $bundlePackages) {
            $packageEntry = $bundle.GetEntry($item.FileName)
            Assert-Submission ($null -ne $packageEntry) "Missing bundled package: $($item.FileName)."
            $package = Open-NestedArchive $packageEntry
            try {
                [xml] $manifest = Read-EntryText $package 'AppxManifest.xml'
                $identity = $manifest.DocumentElement.Identity
                Assert-Submission ($identity.Name -ceq $config.IdentityName) 'Package identity differs from configuration.'
                Assert-Submission ($identity.Publisher -ceq $config.Publisher) 'Package publisher differs from configuration.'
                Assert-Submission ($identity.Version -eq $config.Version) 'Package version differs from configuration.'
                Assert-Submission ($identity.ProcessorArchitecture -eq $item.Architecture) 'Bundle/package architecture mismatch.'
                Assert-Submission ($manifest.DocumentElement.Properties.PublisherDisplayName -ceq $config.PublisherDisplayName) `
                    'Publisher display name differs from Partner Center.'
                Assert-Submission ($manifest.DocumentElement.Dependencies.TargetDeviceFamily.Name -eq 'Windows.Desktop') `
                    'Only the Windows desktop device family is supported.'
                $languages = @($manifest.DocumentElement.Resources.Resource | ForEach-Object Language)
                Assert-Submission (@(Compare-Object $expectedLanguages $languages).Count -eq 0) 'Manifest must declare all 13 UI languages.'
                Assert-Submission (@($package.Entries | Where-Object { $_.Name -like 'Velopack*' }).Count -eq 0) `
                    'Store package must not include Velopack.'
                Assert-Submission (@($package.Entries | Where-Object { $_.Name -like '*.pdb' }).Count -eq 0) `
                    'Debug symbols belong in .appxsym, not the installed package.'
                $application = $manifest.DocumentElement.Applications.Application
                Assert-Submission ($application.Executable -eq 'RouterTray.exe') 'Application executable is incorrect.'
                $capability = $manifest.SelectNodes('//*[local-name()="Capability"]')
                Assert-Submission ($capability.Count -eq 1 -and $capability[0].Name -eq 'runFullTrust') `
                    'Expected only the runFullTrust capability.'
                $startup = $manifest.SelectSingleNode('//*[local-name()="StartupTask"]')
                Assert-Submission ($startup.TaskId -eq 'RouterTrayStartup' -and $startup.Enabled -eq 'false') `
                    'Startup task must be declared and disabled at installation.'

                $runtimeConfig = Read-EntryText $package 'RouterTray.runtimeconfig.json' | ConvertFrom-Json
                $frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
                Assert-Submission ($frameworks.Count -eq 2) 'Expected a self-contained .NET and Windows Desktop runtime.'
                foreach ($framework in $frameworks) {
                    Assert-Submission ($framework.version -match '^10\.0\.') 'Store release must use .NET 10 LTS.'
                    $runtimeName = $framework.name.ToLowerInvariant() + '.runtime.win-' + $identity.ProcessorArchitecture
                    $licensePrefix = "Licenses/$runtimeName/$($framework.version)/"
                    Assert-Submission (@($package.Entries | Where-Object {
                        $_.FullName.StartsWith($licensePrefix) -and $_.Name -match '^LICENSE(\..*)?$'
                    }).Count -gt 0) `
                        "Missing original license for $runtimeName."
                }
                $null = Read-EntryText $package 'LICENSE'
                $null = Read-EntryText $package 'THIRD_PARTY_NOTICES.md'
                $privacy = Read-EntryText $package 'privacy.html'
                Assert-Submission ($privacy.Replace("`r`n", "`n") -ceq $expectedPrivacy) `
                    'The bundled privacy policy differs from the current English policy. Rebuild the package.'
                Assert-Submission ($privacy.Contains('<html lang="en">') -and $privacy -notmatch '\p{IsCyrillic}') `
                    'The bundled privacy policy must be English only.'
                $defaults = Read-EntryText $package 'appsettings.json' | ConvertFrom-Json
                foreach ($settings in (@($defaults) + @($defaults.Profiles))) {
                    foreach ($field in @('Login', 'Password', 'ProtectedPassword', 'AccessToken', 'ProtectedAccessToken')) {
                        Assert-Submission ([string]::IsNullOrEmpty($settings.$field)) 'Package contains preconfigured credentials.'
                    }
                }
                $expectedMachine = @{ x86 = 0x014c; x64 = 0x8664; arm64 = 0xaa64 }[$identity.ProcessorArchitecture]
                $exe = $package.GetEntry('RouterTray.exe').Open()
                $header = [IO.MemoryStream]::new()
                try {
                    $exe.CopyTo($header)
                    $reader = [IO.BinaryReader]::new($header, [Text.Encoding]::UTF8, $true)
                    try {
                        $header.Position = 0x3c
                        $peOffset = $reader.ReadUInt32()
                        $header.Position = $peOffset
                        Assert-Submission ($reader.ReadUInt32() -eq 0x00004550) 'Invalid executable PE signature.'
                        Assert-Submission ($reader.ReadUInt16() -eq $expectedMachine) 'Executable architecture differs from manifest.'
                    } finally { $reader.Dispose() }
                } finally { $exe.Dispose(); $header.Dispose() }
                $architectures.Add([ordered] @{
                    Architecture = $identity.ProcessorArchitecture
                    Version = $identity.Version
                    RuntimeVersion = $frameworks[0].version
                    Languages = $languages.Count
                    Files = $package.Entries.Count
                })
            } finally { $package.Dispose() }
        }
        Assert-Submission (@(Compare-Object @('x86', 'x64', 'arm64') @($architectures.Architecture)).Count -eq 0) `
            'An architecture is missing or duplicated.'
    } finally { $bundle.Dispose() }
    Assert-Submission (@($upload.Entries | Where-Object { $_.Name -like '*.appxsym' }).Count -eq 3) `
        'Expected one symbol archive per architecture.'
} finally { $upload.Dispose() }

$listingRoot = Join-Path $repositoryRoot 'docs\store-assets\partner-center-upload'
$listing = @(Import-Csv -LiteralPath (Join-Path $listingRoot 'listingData.csv'))
$listingLanguages = @($listing[0].PSObject.Properties.Name | Select-Object -Skip 4)
Assert-Submission ($listingLanguages.Count -eq 13 -and
    @(Compare-Object $expectedLanguages $listingLanguages).Count -eq 0) `
    'Store listing language codes must match the 13 package language codes.'
$imagePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($language in $listingLanguages) {
    $description = ($listing | Where-Object Field -eq 'Description').$language
    Assert-Submission (-not [string]::IsNullOrWhiteSpace($description) -and $description.Length -le 10000) `
        "Missing or oversized description for $language."
    Assert-Submission (-not $description.Contains('\n')) "Literal escaped newlines remain in $language."
    $notes = ($listing | Where-Object Field -eq 'ReleaseNotes').$language
    Assert-Submission ($notes.Length -le 1500) "Release notes exceed 1500 characters for $language."
    $terms = @($listing | Where-Object { $_.Field -match '^SearchTerm\d+$' -and $_.$language } | ForEach-Object { $_.$language })
    Assert-Submission ($terms.Count -le 7 -and @($terms | Sort-Object -Unique).Count -eq $terms.Count) `
        "Too many or duplicate search terms for $language."
    $screenshots = @($listing | Where-Object { $_.Field -match '^DesktopScreenshot\d+$' -and $_.$language })
    Assert-Submission ($screenshots.Count -ge 1 -and $screenshots.Count -le 10) "Invalid screenshot count for $language."
    foreach ($row in $screenshots) {
        $relative = $row.$language
        Assert-Submission ($relative.StartsWith('partner-center-upload/')) 'Screenshot path must include the import folder name.'
        $imagePath = [IO.Path]::GetFullPath((Join-Path (Split-Path $listingRoot -Parent) $relative))
        Assert-Submission ($imagePath.StartsWith($listingRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) `
            'Screenshot path escapes the import folder.'
        Assert-Submission (Test-Path -LiteralPath $imagePath -PathType Leaf) "Screenshot not found: $relative."
        if ($imagePaths.Add($imagePath)) {
            $image = [Drawing.Image]::FromFile($imagePath)
            try {
                Assert-Submission ($image.RawFormat.Guid -eq [Drawing.Imaging.ImageFormat]::Png.Guid) 'Screenshots must be PNG.'
                Assert-Submission ($image.Width -ge 1366 -and $image.Height -ge 768) "Screenshot is too small: $relative."
                Assert-Submission ((Get-Item -LiteralPath $imagePath).Length -le 50MB) "Screenshot exceeds 50 MB: $relative."
            } finally { $image.Dispose() }
        }
    }
    foreach ($caption in ($listing | Where-Object { $_.Field -match '^DesktopScreenshotCaption\d+$' })) {
        Assert-Submission ($caption.$language.Length -le 200) "Screenshot caption exceeds 200 characters for $language."
    }
}

$report = [ordered] @{
    CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Status = 'Passed'
    Package = [IO.Path]::GetFullPath($PackagePath)
    SHA256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash
    Identity = $config.IdentityName
    Publisher = $config.Publisher
    Version = $config.Version
    Architectures = @($architectures.ToArray())
    ListingLanguages = $listingLanguages
    Screenshots = $imagePaths.Count
    WindowsAppCertificationKit = 'Separate check required; this script does not run WACK.'
    BundledPrivacyPolicyLanguage = 'en'
    BundledPrivacyPolicyMatchesSource = $true
    PrivacyPolicySHA256 = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'docs\privacy\index.html') -Algorithm SHA256).Hash
    PrivacyPolicyUrl = $config.PrivacyPolicyUrl
    PrivacyPolicyUrlConfigured = -not [string]::IsNullOrWhiteSpace($config.PrivacyPolicyUrl)
}
New-Item -ItemType Directory -Force -Path (Split-Path $ReportPath -Parent) | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8NoBOM
Write-Host "Validated x86, x64, ARM64; $($listingLanguages.Count) languages; $($imagePaths.Count) PNG screenshots."
Write-Host "Report: $ReportPath"
