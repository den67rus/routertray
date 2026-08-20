[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[1-9][0-9]{0,4}\.[0-9]{1,5}\.[0-9]{1,5}\.0$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $IdentityName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Publisher,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $PublisherDisplayName,

    [string] $DisplayName = 'RouterTray',

    [string] $OutputDirectory,

    [ValidateSet('win-x86', 'win-x64', 'win-arm64')]
    [string[]] $RuntimeIdentifiers = @('win-x86', 'win-x64', 'win-arm64'),

    [switch] $KeepPackageLayout
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\store'
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$trimmedOutputRoot = $outputRoot.TrimEnd([char[]] @('\', '/'))
$trimmedRepositoryRoot = $repositoryRoot.TrimEnd([char[]] @('\', '/'))
$trimmedDriveRoot = ([IO.Path]::GetPathRoot($outputRoot)).TrimEnd([char[]] @('\', '/'))
if ([string]::Equals($trimmedOutputRoot, $trimmedDriveRoot, [StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($trimmedOutputRoot, $trimmedRepositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must not be a drive root or the repository root: '$outputRoot'."
}

$projectPath = Join-Path $repositoryRoot 'RouterTray.csproj'
$manifestTemplatePath = Join-Path $PSScriptRoot 'AppxManifest.template.xml'
$sourceIconPath = Join-Path $repositoryRoot 'docs\images\routertray-icon.png'
$workRoot = Join-Path $outputRoot ('.work-' + [Guid]::NewGuid().ToString('N'))
$packageOutputRoot = Join-Path $outputRoot 'packages'
$bundleInputRoot = Join-Path $workRoot 'bundle-input'
$uploadInputRoot = Join-Path $workRoot 'upload-input'

function Assert-StoreVersion {
    param([string] $Value)

    $parts = $Value.Split('.')
    if ($parts.Count -ne 4 -or $parts[3] -ne '0') {
        throw "Store package version '$Value' must have four numeric parts and end in .0."
    }

    foreach ($part in $parts) {
        $number = 0
        if (-not [int]::TryParse($part, [ref] $number) -or $number -lt 0 -or $number -gt 65535) {
            throw "Every Store package version component must be between 0 and 65535: '$Value'."
        }
    }

    if ([int] $parts[0] -eq 0) {
        throw "The first Store package version component cannot be zero: '$Value'."
    }
}

function Get-WindowsSdkTool {
    param([Parameter(Mandatory)][string] $Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidates = Get-ChildItem -LiteralPath $kitsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [Version] $_.Name } -Descending
    foreach ($candidate in $candidates) {
        $toolPath = Join-Path $candidate.FullName "x64\$Name"
        if (Test-Path -LiteralPath $toolPath -PathType Leaf) {
            return $toolPath
        }
    }

    throw "$Name was not found. Install the Windows 10/11 SDK."
}

function New-LogoAsset {
    param(
        [Parameter(Mandatory)][System.Drawing.Image] $Source,
        [Parameter(Mandatory)][int] $Size,
        [Parameter(Mandatory)][string] $Path
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($Source, 0, 0, $Size, $Size)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function New-PackageAssets {
    param([Parameter(Mandatory)][string] $AssetsDirectory)

    New-Item -ItemType Directory -Force -Path $AssetsDirectory | Out-Null
    Add-Type -AssemblyName System.Drawing
    $source = [System.Drawing.Image]::FromFile($sourceIconPath)
    try {
        New-LogoAsset -Source $source -Size 50 -Path (Join-Path $AssetsDirectory 'StoreLogo.png')
        New-LogoAsset -Source $source -Size 44 -Path (Join-Path $AssetsDirectory 'Square44x44Logo.png')
        New-LogoAsset -Source $source -Size 150 -Path (Join-Path $AssetsDirectory 'Square150x150Logo.png')
        foreach ($targetSize in @(16, 24, 32, 44, 48, 256)) {
            $name = "Square44x44Logo.targetsize-$($targetSize)_altform-unplated.png"
            New-LogoAsset -Source $source -Size $targetSize -Path (Join-Path $AssetsDirectory $name)
        }
    }
    finally {
        $source.Dispose()
    }
}

function New-PackageManifest {
    param(
        [Parameter(Mandatory)][string] $Architecture,
        [Parameter(Mandatory)][string] $Destination
    )

    [xml] $manifest = Get-Content -LiteralPath $manifestTemplatePath -Raw
    $namespace = 'http://schemas.microsoft.com/appx/manifest/foundation/windows10'
    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaceManager.AddNamespace('f', $namespace)
    $namespaceManager.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
    $namespaceManager.AddNamespace('uap5', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5')

    $identity = $manifest.SelectSingleNode('/f:Package/f:Identity', $namespaceManager)
    $identity.SetAttribute('Name', $IdentityName)
    $identity.SetAttribute('Publisher', $Publisher)
    $identity.SetAttribute('Version', $Version)
    $identity.SetAttribute('ProcessorArchitecture', $Architecture)

    $manifest.SelectSingleNode('/f:Package/f:Properties/f:DisplayName', $namespaceManager).InnerText = $DisplayName
    $manifest.SelectSingleNode('/f:Package/f:Properties/f:PublisherDisplayName', $namespaceManager).InnerText = $PublisherDisplayName

    $visualElements = $manifest.SelectSingleNode(
        '/f:Package/f:Applications/f:Application/uap:VisualElements',
        $namespaceManager)
    $visualElements.SetAttribute('DisplayName', $DisplayName)

    $startupTask = $manifest.SelectSingleNode(
        '/f:Package/f:Applications/f:Application/f:Extensions/uap5:Extension/uap5:StartupTask',
        $namespaceManager)
    $startupTask.SetAttribute('DisplayName', $DisplayName)

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $writer = [System.Xml.XmlWriter]::Create($Destination, $settings)
    try {
        $manifest.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

function New-AppxSymbolPackage {
    param(
        [Parameter(Mandatory)][string] $RuntimeIdentifier,
        [Parameter(Mandatory)][string] $PdbPath,
        [Parameter(Mandatory)][string] $Destination
    )

    $symbolStaging = Join-Path $workRoot "symbols-$RuntimeIdentifier"
    New-Item -ItemType Directory -Force -Path $symbolStaging | Out-Null
    Copy-Item -LiteralPath $PdbPath -Destination $symbolStaging
    $zipPath = [IO.Path]::ChangeExtension($Destination, '.zip')
    Compress-Archive -Path (Join-Path $symbolStaging '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Move-Item -LiteralPath $zipPath -Destination $Destination
}

function Invoke-MakeAppx {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $FailureMessage
    )

    $toolOutput = & $makeAppx @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $toolOutput | Write-Host
        throw $FailureMessage
    }

    $successLine = $toolOutput |
        Where-Object { $_ -match '(Package|Bundle) creation succeeded' } |
        Select-Object -Last 1
    if ($null -ne $successLine) {
        Write-Host $successLine
    }
}

Assert-StoreVersion $Version
$makeAppx = Get-WindowsSdkTool -Name 'makeappx.exe'
$architectureMap = @{
    'win-x86' = 'x86'
    'win-x64' = 'x64'
    'win-arm64' = 'arm64'
}

New-Item -ItemType Directory -Force -Path $outputRoot, $packageOutputRoot, $workRoot, $bundleInputRoot, $uploadInputRoot | Out-Null

try {
    foreach ($runtimeIdentifier in $RuntimeIdentifiers) {
        $architecture = $architectureMap[$runtimeIdentifier]
        $publishDirectory = Join-Path $workRoot "publish\$runtimeIdentifier"
        $packageLayout = Join-Path $workRoot "layout\$runtimeIdentifier"
        $packagePath = Join-Path $packageOutputRoot "RouterTray_$($Version)_$architecture.msix"

        dotnet publish $projectPath `
            -c Release `
            -r $runtimeIdentifier `
            --self-contained true `
            -o $publishDirectory `
            -p:StoreBuild=true `
            -p:TreatWarningsAsErrors=true `
            -p:Version=$Version `
            -p:InformationalVersion=$Version `
            -p:AssemblyVersion=$Version `
            -p:FileVersion=$Version
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $runtimeIdentifier."
        }

        $unexpectedVelopackFiles = @(
            Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
                Where-Object { $_.Name -like 'Velopack*' }
        )
        if ($unexpectedVelopackFiles.Count -gt 0) {
            throw "The Store publish output unexpectedly contains Velopack files: $($unexpectedVelopackFiles.Name -join ', ')."
        }

        New-Item -ItemType Directory -Force -Path $packageLayout | Out-Null
        Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $packageLayout -Recurse
        Get-ChildItem -LiteralPath $packageLayout -Recurse -File -Filter '*.pdb' | Remove-Item -Force
        New-PackageAssets -AssetsDirectory (Join-Path $packageLayout 'Assets')
        New-PackageManifest -Architecture $architecture -Destination (Join-Path $packageLayout 'AppxManifest.xml')

        Invoke-MakeAppx `
            -Arguments @('pack', '/d', $packageLayout, '/p', $packagePath, '/o') `
            -FailureMessage "MakeAppx failed to create $packagePath."
        Copy-Item -LiteralPath $packagePath -Destination $bundleInputRoot

        $pdbPath = Join-Path $publishDirectory 'RouterTray.pdb'
        if (Test-Path -LiteralPath $pdbPath) {
            $symbolPath = Join-Path $uploadInputRoot "RouterTray_$($Version)_$architecture.appxsym"
            New-AppxSymbolPackage -RuntimeIdentifier $runtimeIdentifier -PdbPath $pdbPath -Destination $symbolPath
        }

        if ($KeepPackageLayout) {
            $persistentLayout = Join-Path $outputRoot "layout\$runtimeIdentifier"
            if (Test-Path -LiteralPath $persistentLayout) {
                Remove-Item -LiteralPath $persistentLayout -Recurse -Force
            }
            New-Item -ItemType Directory -Force -Path $persistentLayout | Out-Null
            Copy-Item -Path (Join-Path $packageLayout '*') -Destination $persistentLayout -Recurse
        }
    }

    $bundlePath = Join-Path $outputRoot "RouterTray_$($Version).msixbundle"
    Invoke-MakeAppx `
        -Arguments @('bundle', '/d', $bundleInputRoot, '/p', $bundlePath, '/bv', $Version, '/o') `
        -FailureMessage "MakeAppx failed to create $bundlePath."

    Copy-Item -LiteralPath $bundlePath -Destination $uploadInputRoot
    $uploadPath = Join-Path $outputRoot "RouterTray_$($Version).msixupload"
    $uploadZipPath = [IO.Path]::ChangeExtension($uploadPath, '.zip')
    if (Test-Path -LiteralPath $uploadZipPath) {
        Remove-Item -LiteralPath $uploadZipPath -Force
    }
    if (Test-Path -LiteralPath $uploadPath) {
        Remove-Item -LiteralPath $uploadPath -Force
    }
    Compress-Archive -Path (Join-Path $uploadInputRoot '*') -DestinationPath $uploadZipPath -CompressionLevel Optimal
    Move-Item -LiteralPath $uploadZipPath -Destination $uploadPath

    Write-Host "Microsoft Store upload package: $uploadPath"
    Write-Host "Unsigned bundle for inspection: $bundlePath"
}
finally {
    $resolvedWorkRoot = [IO.Path]::GetFullPath($workRoot)
    $resolvedOutputRoot = [IO.Path]::GetFullPath($outputRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedWorkRoot.StartsWith($resolvedOutputRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedWorkRoot)) {
        Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force
    }
}
