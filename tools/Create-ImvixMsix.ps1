[CmdletBinding()]
param(
    [string]$PublishDirectory = "Imvix-v1.3.4-win-x64",
    [string]$PackageIdentityName = "D787ABC4.Imvix",
    [string]$Publisher = "CN=FA0F6293-29B7-43FB-AB9B-49D0FB5F198C",
    [string]$PublisherDisplayName = (([char]0x5DF2) + ([char]0x901D) + ([char]0x60C5) + ([char]0x6B87)),
    [string]$DisplayName = "Imvix",
    [string]$Description = "Imvix image conversion desktop application",
    [string]$PackageVersion,
    [string]$MinVersion = "10.0.17763.0",
    [string]$MaxVersionTested = "10.0.26100.0",
    [switch]$SkipSigning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-LatestWindowsKitToolPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolName
    )

    $binRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $binRoot)) {
        throw "Windows 10 SDK not found: $binRoot"
    }

    $versionedCandidate = Get-ChildItem -LiteralPath $binRoot -Directory |
        Where-Object { $_.Name -match "^\d+\.\d+\.\d+\.\d+$" } |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$ToolName" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1

    if ($versionedCandidate) {
        return (Resolve-Path -LiteralPath $versionedCandidate).Path
    }

    $fallback = Join-Path $binRoot "x64\$ToolName"
    if (Test-Path -LiteralPath $fallback) {
        return (Resolve-Path -LiteralPath $fallback).Path
    }

    throw "Unable to find $ToolName under $binRoot"
}

function Invoke-ExternalTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolPath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    & $ToolPath @Arguments | Out-Host
    $exitCode = $LASTEXITCODE

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw ("Command failed with exit code {0}: {1} {2}" -f $exitCode, $ToolPath, ($Arguments -join " "))
    }

    return $exitCode
}

function Convert-ToPackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionText
    )

    $segments = $VersionText.Split(".", [System.StringSplitOptions]::RemoveEmptyEntries)
    switch ($segments.Count) {
        1 { return "$VersionText.0.0.0" }
        2 { return "$VersionText.0.0" }
        3 { return "$VersionText.0" }
        4 { return $VersionText }
        default { throw "Version '$VersionText' is not a valid package version." }
    }
}

function Get-ProjectVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath
    $versionNode = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if (-not $versionNode) {
        throw "Unable to find <Version> in $ProjectPath"
    }

    return Convert-ToPackageVersion -VersionText $versionNode
}

function Get-PackageFileVersionLabel {
    param(
        [Parameter(Mandatory = $true)]
        [string]$VersionText
    )

    $segments = $VersionText.Split(".", [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($segments.Count -eq 4 -and $segments[3] -eq "0") {
        return ($segments[0..2] -join ".")
    }

    return $VersionText
}

function Test-IsPackagingArtifact {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileSystemInfo]$Entry
    )

    if ($Entry.PSIsContainer) {
        return $false
    }

    return $Entry.Extension -in @(".msix", ".appx", ".msixupload", ".appxupload")
}

function Save-ScaledPng {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath,
        [Parameter(Mandatory = $true)]
        [int]$Width,
        [Parameter(Mandatory = $true)]
        [int]$Height,
        [double]$PaddingRatio = 0.12
    )

    Add-Type -AssemblyName System.Drawing

    $image = [System.Drawing.Image]::FromFile($SourcePath)
    try {
        $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

                $maxWidth = [int][Math]::Round($Width * (1 - ($PaddingRatio * 2)))
                $maxHeight = [int][Math]::Round($Height * (1 - ($PaddingRatio * 2)))
                $scale = [Math]::Min($maxWidth / $image.Width, $maxHeight / $image.Height)
                $drawWidth = [int][Math]::Max(1, [Math]::Round($image.Width * $scale))
                $drawHeight = [int][Math]::Max(1, [Math]::Round($image.Height * $scale))
                $left = [int][Math]::Round(($Width - $drawWidth) / 2)
                $top = [int][Math]::Round(($Height - $drawHeight) / 2)

                $graphics.DrawImage($image, $left, $top, $drawWidth, $drawHeight)
            }
            finally {
                $graphics.Dispose()
            }

            $bitmap.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $image.Dispose()
    }
}

function New-TemporaryCodeSigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Subject,
        [Parameter(Mandatory = $true)]
        [string]$PfxPath
    )

    $password = "Imvix-" + [Guid]::NewGuid().ToString("N")
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        $dn = New-Object System.Security.Cryptography.X509Certificates.X500DistinguishedName $Subject
        $hashAlgorithm = [System.Security.Cryptography.HashAlgorithmName]::SHA256
        $padding = [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
        $request = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest $dn, $rsa, $hashAlgorithm, $padding

        $basicConstraints = New-Object System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension $false, $false, 0, $false
        $keyUsage = New-Object System.Security.Cryptography.X509Certificates.X509KeyUsageExtension ([System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature), $false
        $enhancedKeyUsages = New-Object System.Security.Cryptography.OidCollection
        [void]$enhancedKeyUsages.Add((New-Object System.Security.Cryptography.Oid "1.3.6.1.5.5.7.3.3"))
        $eku = New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension $enhancedKeyUsages, $false
        $subjectKeyIdentifier = New-Object System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension $request.PublicKey, $false

        [void]$request.CertificateExtensions.Add($basicConstraints)
        [void]$request.CertificateExtensions.Add($keyUsage)
        [void]$request.CertificateExtensions.Add($eku)
        [void]$request.CertificateExtensions.Add($subjectKeyIdentifier)

        $cert = $request.CreateSelfSigned((Get-Date).AddDays(-1), (Get-Date).AddYears(3))
        try {
            $pfxBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password)
            Set-Content -LiteralPath $PfxPath -Value $pfxBytes -Encoding Byte

            return [pscustomobject]@{
                PfxPath = $PfxPath
                Password = $password
                Thumbprint = $cert.Thumbprint
            }
        }
        finally {
            $cert.Dispose()
        }
    }
    finally {
        $rsa.Dispose()
    }
}

function New-AppxManifestXml {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,
        [Parameter(Mandatory = $true)]
        [string]$IdentityName,
        [Parameter(Mandatory = $true)]
        [string]$PublisherValue,
        [Parameter(Mandatory = $true)]
        [string]$VersionValue,
        [Parameter(Mandatory = $true)]
        [string]$DisplayNameValue,
        [Parameter(Mandatory = $true)]
        [string]$PublisherDisplayNameValue,
        [Parameter(Mandatory = $true)]
        [string]$DescriptionValue,
        [Parameter(Mandatory = $true)]
        [string]$MinVersionValue,
        [Parameter(Mandatory = $true)]
        [string]$MaxVersionTestedValue
    )

    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true

    $package = $xml.CreateElement("Package", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $package.SetAttribute("xmlns:uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
    $package.SetAttribute("xmlns:desktop", "http://schemas.microsoft.com/appx/manifest/desktop/windows10")
    $package.SetAttribute("xmlns:rescap", "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities")
    $package.SetAttribute("IgnorableNamespaces", "uap desktop rescap")
    [void]$xml.AppendChild($package)

    $identity = $xml.CreateElement("Identity", $package.NamespaceURI)
    $identity.SetAttribute("Name", $IdentityName)
    $identity.SetAttribute("Publisher", $PublisherValue)
    $identity.SetAttribute("Version", $VersionValue)
    $identity.SetAttribute("ProcessorArchitecture", "x64")
    [void]$package.AppendChild($identity)

    $properties = $xml.CreateElement("Properties", $package.NamespaceURI)
    [void]$package.AppendChild($properties)

    foreach ($nodeInfo in @(
        @{ Name = "DisplayName"; Value = $DisplayNameValue },
        @{ Name = "PublisherDisplayName"; Value = $PublisherDisplayNameValue },
        @{ Name = "Logo"; Value = "Assets\StoreLogo.png" },
        @{ Name = "Description"; Value = $DescriptionValue }
    )) {
        $element = $xml.CreateElement($nodeInfo.Name, $package.NamespaceURI)
        $element.InnerText = $nodeInfo.Value
        [void]$properties.AppendChild($element)
    }

    $resources = $xml.CreateElement("Resources", $package.NamespaceURI)
    [void]$package.AppendChild($resources)

    foreach ($language in @(
        "zh-Hans",
        "zh-Hant",
        "en-US",
        "ja-JP",
        "ko-KR",
        "de-DE",
        "fr-FR",
        "it-IT",
        "ru-RU",
        "th-TH",
        "vi-VN",
        "ar-SA"
    )) {
        $resource = $xml.CreateElement("Resource", $package.NamespaceURI)
        $resource.SetAttribute("Language", $language)
        [void]$resources.AppendChild($resource)
    }

    $dependencies = $xml.CreateElement("Dependencies", $package.NamespaceURI)
    [void]$package.AppendChild($dependencies)

    $targetDeviceFamily = $xml.CreateElement("TargetDeviceFamily", $package.NamespaceURI)
    $targetDeviceFamily.SetAttribute("Name", "Windows.Desktop")
    $targetDeviceFamily.SetAttribute("MinVersion", $MinVersionValue)
    $targetDeviceFamily.SetAttribute("MaxVersionTested", $MaxVersionTestedValue)
    [void]$dependencies.AppendChild($targetDeviceFamily)

    $applications = $xml.CreateElement("Applications", $package.NamespaceURI)
    [void]$package.AppendChild($applications)

    $application = $xml.CreateElement("Application", $package.NamespaceURI)
    $application.SetAttribute("Id", "Imvix")
    $application.SetAttribute("Executable", "Imvix.exe")
    $application.SetAttribute("EntryPoint", "Windows.FullTrustApplication")
    [void]$applications.AppendChild($application)

    $visualElements = $xml.CreateElement("uap", "VisualElements", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
    $visualElements.SetAttribute("DisplayName", $DisplayNameValue)
    $visualElements.SetAttribute("Description", $DescriptionValue)
    $visualElements.SetAttribute("BackgroundColor", "transparent")
    $visualElements.SetAttribute("Square44x44Logo", "Assets\Square44x44Logo.png")
    $visualElements.SetAttribute("Square150x150Logo", "Assets\Square150x150Logo.png")
    $visualElements.SetAttribute("AppListEntry", "default")
    [void]$application.AppendChild($visualElements)

    $defaultTile = $xml.CreateElement("uap", "DefaultTile", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
    $defaultTile.SetAttribute("Wide310x150Logo", "Assets\Wide310x150Logo.png")
    $defaultTile.SetAttribute("Square310x310Logo", "Assets\Square310x310Logo.png")
    $defaultTile.SetAttribute("ShortName", $DisplayNameValue)
    [void]$visualElements.AppendChild($defaultTile)

    $splashScreen = $xml.CreateElement("uap", "SplashScreen", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
    $splashScreen.SetAttribute("Image", "Assets\SplashScreen.png")
    $splashScreen.SetAttribute("BackgroundColor", "#FFFFFF")
    [void]$visualElements.AppendChild($splashScreen)

    $capabilities = $xml.CreateElement("Capabilities", $package.NamespaceURI)
    [void]$package.AppendChild($capabilities)

    $runFullTrust = $xml.CreateElement("rescap", "Capability", "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities")
    $runFullTrust.SetAttribute("Name", "runFullTrust")
    [void]$capabilities.AppendChild($runFullTrust)

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.IndentChars = "    "
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)

    $writer = [System.Xml.XmlWriter]::Create($ManifestPath, $settings)
    try {
        $xml.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = (Resolve-Path -LiteralPath (Join-Path $repoRoot $PublishDirectory)).Path
$projectPath = Join-Path $repoRoot "Imvix.csproj"

if (-not $PackageVersion) {
    $PackageVersion = Get-ProjectVersion -ProjectPath $projectPath
}
else {
    $PackageVersion = Convert-ToPackageVersion -VersionText $PackageVersion
}

$makeAppx = Get-LatestWindowsKitToolPath -ToolName "makeappx.exe"
$makePri = Get-LatestWindowsKitToolPath -ToolName "makepri.exe"
$signTool = Get-LatestWindowsKitToolPath -ToolName "signtool.exe"

$packageFileVersionLabel = Get-PackageFileVersionLabel -VersionText $PackageVersion
$outputName = "Imvix-v$packageFileVersionLabel-store-x64.msix"
$outputPath = Join-Path $publishRoot $outputName

$buildRoot = Join-Path $repoRoot "obj\msix-build"
$layoutRoot = Join-Path $buildRoot "layout"
$workRoot = Join-Path $buildRoot "work"
$assetsRoot = Join-Path $layoutRoot "Assets"
$manifestPath = Join-Path $layoutRoot "AppxManifest.xml"
$priConfigPath = Join-Path $workRoot "priconfig.xml"
$resourcesPriPath = Join-Path $layoutRoot "resources.pri"
$temporaryPfxPath = Join-Path $workRoot "publisher-signing.pfx"

if (Test-Path -LiteralPath $buildRoot) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $layoutRoot -Force | Out-Null
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
New-Item -ItemType Directory -Path $assetsRoot -Force | Out-Null

$publishEntries = Get-ChildItem -LiteralPath $publishRoot -Force |
    Where-Object { -not (Test-IsPackagingArtifact -Entry $_) }
if (-not $publishEntries) {
    throw "Publish directory is empty: $publishRoot"
}

foreach ($entry in $publishEntries) {
    Copy-Item -LiteralPath $entry.FullName -Destination $layoutRoot -Recurse -Force
}

$sourceFiles = Get-ChildItem -LiteralPath $publishRoot -File -Recurse |
    Where-Object { -not (Test-IsPackagingArtifact -Entry $_) }
foreach ($sourceFile in $sourceFiles) {
    $relativePath = $sourceFile.FullName.Substring($publishRoot.Length).TrimStart("\")
    $packagedPath = Join-Path $layoutRoot $relativePath
    if (-not (Test-Path -LiteralPath $packagedPath)) {
        throw "Missing copied publish file in package layout: $relativePath"
    }
}

$logoSource = (Resolve-Path -LiteralPath (Join-Path $repoRoot "Assets\logo.png")).Path

Save-ScaledPng -SourcePath $logoSource -DestinationPath (Join-Path $assetsRoot "StoreLogo.png") -Width 50 -Height 50 -PaddingRatio 0.08
Save-ScaledPng -SourcePath $logoSource -DestinationPath (Join-Path $assetsRoot "Square44x44Logo.png") -Width 44 -Height 44 -PaddingRatio 0.08
Save-ScaledPng -SourcePath $logoSource -DestinationPath (Join-Path $assetsRoot "Square44x44Logo.targetsize-44_altform-unplated.png") -Width 44 -Height 44 -PaddingRatio 0.08
Save-ScaledPng -SourcePath $logoSource -DestinationPath (Join-Path $assetsRoot "Square150x150Logo.png") -Width 150 -Height 150 -PaddingRatio 0.10
Save-ScaledPng -SourcePath $logoSource -DestinationPath (Join-Path $assetsRoot "Wide310x150Logo.png") -Width 310 -Height 150 -PaddingRatio 0.18
Save-ScaledPng -SourcePath $logoSource -DestinationPath (Join-Path $assetsRoot "Square310x310Logo.png") -Width 310 -Height 310 -PaddingRatio 0.12
Save-ScaledPng -SourcePath $logoSource -DestinationPath (Join-Path $assetsRoot "SplashScreen.png") -Width 620 -Height 300 -PaddingRatio 0.12

New-AppxManifestXml `
    -ManifestPath $manifestPath `
    -IdentityName $PackageIdentityName `
    -PublisherValue $Publisher `
    -VersionValue $PackageVersion `
    -DisplayNameValue $DisplayName `
    -PublisherDisplayNameValue $PublisherDisplayName `
    -DescriptionValue $Description `
    -MinVersionValue $MinVersion `
    -MaxVersionTestedValue $MaxVersionTested

Invoke-ExternalTool -ToolPath $makePri -Arguments @("createconfig", "/cf", $priConfigPath, "/dq", "en-US", "/pv", "10.0.0", "/o") | Out-Null
Invoke-ExternalTool -ToolPath $makePri -Arguments @("new", "/pr", $layoutRoot, "/cf", $priConfigPath, "/mn", $manifestPath, "/of", $resourcesPriPath, "/o") | Out-Null

Invoke-ExternalTool -ToolPath $makeAppx -Arguments @("pack", "/v", "/h", "SHA256", "/o", "/d", $layoutRoot, "/p", $outputPath) | Out-Null

$signingMode = "Skipped"
$certificateThumbprint = $null

if (-not $SkipSigning) {
    $certificate = New-TemporaryCodeSigningCertificate -Subject $Publisher -PfxPath $temporaryPfxPath
    $certificateThumbprint = $certificate.Thumbprint

    $signExitCode = Invoke-ExternalTool -ToolPath $signTool -Arguments @("sign", "/fd", "SHA256", "/f", $certificate.PfxPath, "/p", $certificate.Password, "/v", $outputPath) -AllowFailure
    if ($signExitCode -eq 0) {
        $signingMode = "Signed"
    }
    else {
        $signingMode = "Failed (package remains unsigned)"
        $certificateThumbprint = $null
    }
}

[pscustomobject]@{
    OutputPath = $outputPath
    PublishDirectory = $publishRoot
    PackageVersion = $PackageVersion
    Languages = "zh-Hans, zh-Hant, en-US, ja-JP, ko-KR, de-DE, fr-FR, it-IT, ru-RU, th-TH, vi-VN, ar-SA"
    PublishFileCount = $sourceFiles.Count
    Signing = $signingMode
    CertificateThumbprint = $certificateThumbprint
}
