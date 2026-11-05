<#
.SYNOPSIS
    Builds JsonNotepad MSIX packages (and optionally an .msixbundle) for the Microsoft Store or sideloading.

.DESCRIPTION
    For each architecture:
      1. dotnet publish src/JsonNotepad.App self-contained for win-<arch>  -> artifacts/publish/win-<arch>
      2. stage the package layout (publish output + Assets + stamped AppxManifest.xml) -> artifacts/msix/win-<arch>
      3. makepri: generate resources.pri (needed for the targetsize/altform-unplated icon variants)
      4. makeappx pack -> artifacts/JsonNotepad_<version>_<arch>.msix
    Then optionally bundles all architectures into artifacts/JsonNotepad_<version>.msixbundle and signs everything.

    The version comes from VersionPrefix in Directory.Build.props (Major.Minor.Patch); the MSIX version is
    Major.Minor.Patch.0 because the Store requires the revision part to be 0.

.PARAMETER Architectures
    Target CPU architectures. Default: x64, arm64.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Bundle
    Also produce a single .msixbundle containing every architecture (recommended for Store upload).

.PARAMETER Sign
    Sign the packages with a .pfx given by -CertificatePath (and -CertificatePassword). The certificate
    Subject must equal the Publisher in installer/msix/package.config.psd1.

.PARAMETER CreateDevCertificate
    Create (or reuse) a self-signed code-signing certificate whose Subject equals the Publisher, in
    Cert:\CurrentUser\My, sign the packages with it and export the public part to artifacts/JsonNotepad-dev.cer.

.PARAMETER TimestampUrl
    Optional RFC 3161 timestamp server used when signing (e.g. http://timestamp.digicert.com).

.PARAMETER SkipPublish
    Reuse the existing artifacts/publish/win-<arch> folders instead of running dotnet publish.

.EXAMPLE
    ./build/package-msix.ps1 -Architectures x64
    Unsigned x64 package (fine for Store upload; cannot be installed locally).

.EXAMPLE
    ./build/package-msix.ps1 -CreateDevCertificate
    Signed x64 + arm64 packages that can be sideloaded after trusting artifacts/JsonNotepad-dev.cer.

.EXAMPLE
    ./build/package-msix.ps1 -Bundle
    artifacts/JsonNotepad_<ver>.msixbundle for Partner Center (the Store re-signs it).
#>
[CmdletBinding(DefaultParameterSetName = 'Unsigned')]
param(
    [ValidateSet('x64', 'arm64')]
    [string[]] $Architectures = @('x64', 'arm64'),

    [string] $Configuration = 'Release',

    [switch] $Bundle,

    [Parameter(ParameterSetName = 'Pfx', Mandatory)]
    [switch] $Sign,

    [Parameter(ParameterSetName = 'Pfx', Mandatory)]
    [string] $CertificatePath,

    [Parameter(ParameterSetName = 'Pfx')]
    [string] $CertificatePassword,

    [Parameter(ParameterSetName = 'DevCert', Mandatory)]
    [switch] $CreateDevCertificate,

    [string] $TimestampUrl,

    [string] $IdentityName,
    [string] $Publisher,
    [string] $PublisherDisplayName,

    [switch] $SkipPublish
)

. (Join-Path $PSScriptRoot 'common.ps1')

$msixDir = Join-Path $RepoRoot 'installer\msix'
$assetsDir = Join-Path $msixDir 'Assets'
$templatePath = Join-Path $msixDir 'AppxManifest.template.xml'
$configPath = Join-Path $msixDir 'package.config.psd1'

# ---------------------------------------------------------------- inputs
$config = Import-PowerShellDataFile -Path $configPath
if (-not $IdentityName) { $IdentityName = $config.IdentityName }
if (-not $Publisher) { $Publisher = $config.Publisher }
if (-not $PublisherDisplayName) { $PublisherDisplayName = $config.PublisherDisplayName }

if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') {
    throw "IdentityName '$IdentityName' is invalid (3-50 characters: letters, digits, '.' and '-')."
}
if ($Publisher -notmatch '^CN=') {
    throw "Publisher '$Publisher' must be a distinguished name starting with 'CN=' (copy it from Partner Center)."
}

foreach ($required in 'Square44x44Logo.png', 'Square150x150Logo.png', 'StoreLogo.png', 'Wide310x150Logo.png') {
    if (-not (Test-Path (Join-Path $assetsDir $required))) {
        throw "MSIX asset '$required' is missing from $assetsDir. Regenerate the assets with: ./build/generate-icons.ps1"
    }
}

$version = Get-ProductVersion
$msixVersion = "$version.0"
Write-Host "JsonNotepad $version  (MSIX $msixVersion)  identity=$IdentityName  publisher='$Publisher'"

# Resolve SDK tools up front so a missing SDK fails fast, before the slow publish.
$makeappx = Find-WindowsSdkTool 'makeappx.exe'
$makepri = Find-WindowsSdkTool 'makepri.exe'
$signtool = $null
if ($Sign -or $CreateDevCertificate) { $signtool = Find-WindowsSdkTool 'signtool.exe' }
Write-Host "Windows SDK: $(Split-Path (Split-Path $makeappx))"

if ($Sign -and -not (Test-Path $CertificatePath)) {
    throw "Certificate file not found: $CertificatePath"
}

New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

# ---------------------------------------------------------------- dev certificate
$devThumbprint = $null
if ($CreateDevCertificate) {
    Write-Step "Preparing self-signed certificate '$Publisher'"
    $now = Get-Date
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object {
            $_.Subject -eq $Publisher -and $_.HasPrivateKey -and $_.NotAfter -gt $now.AddDays(7) -and
            ($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })
        } |
        Sort-Object NotAfter -Descending | Select-Object -First 1

    if ($cert) {
        Write-Host "Reusing existing certificate $($cert.Thumbprint) (expires $($cert.NotAfter.ToString('yyyy-MM-dd')))."
    }
    else {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher `
            -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
            -FriendlyName 'JsonNotepad MSIX development signing' `
            -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter $now.AddYears(2) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Write-Host "Created certificate $($cert.Thumbprint) in Cert:\CurrentUser\My."
    }
    $devThumbprint = $cert.Thumbprint
    $cerPath = Join-Path $ArtifactsDir 'JsonNotepad-dev.cer'
    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
    Write-Host "Exported public certificate: $cerPath"
}

function Invoke-Sign([string] $File) {
    $signArgs = @('sign', '/fd', 'SHA256')
    if ($devThumbprint) {
        $signArgs += @('/sha1', $devThumbprint, '/s', 'My')
    }
    else {
        $signArgs += @('/f', (Resolve-Path $CertificatePath).Path)
        if ($CertificatePassword) { $signArgs += @('/p', $CertificatePassword) }
    }
    if ($TimestampUrl) { $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256') }
    $signArgs += $File
    Write-Host "   > signtool sign $File" -ForegroundColor DarkGray
    & $signtool @signArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed ($LASTEXITCODE). The certificate Subject must equal the manifest Publisher '$Publisher'."
    }
}

# ---------------------------------------------------------------- per-architecture packages
$template = Get-Content -Raw -Path $templatePath
$packages = @()

foreach ($arch in $Architectures) {
    $rid = "win-$arch"

    if ($SkipPublish) {
        $publishDir = Join-Path $ArtifactsDir "publish\$rid"
        if (-not (Test-Path (Join-Path $publishDir 'JsonNotepad.exe'))) {
            throw "-SkipPublish was given but $publishDir does not contain JsonNotepad.exe."
        }
    }
    else {
        Write-Step "Publishing $rid ($Configuration, self-contained)"
        $publishDir = Publish-App -RuntimeIdentifier $rid -Configuration $Configuration
    }

    Write-Step "Staging package layout for $rid"
    $layout = Join-Path $ArtifactsDir "msix\$rid"
    if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $layout | Out-Null
    Copy-Item -Path (Join-Path $publishDir '*') -Destination $layout -Recurse -Exclude '*.pdb'
    Copy-Item -Path $assetsDir -Destination (Join-Path $layout 'Assets') -Recurse

    # Drop the template's documentation comment (it mentions the tokens) before stamping.
    $manifest = ([regex] '(?s)<!--.*?-->\s*').Replace($template, '', 1)
    $manifest = $manifest.Replace('$IDENTITY_NAME$', $IdentityName)
    $manifest = $manifest.Replace('$PUBLISHER_DISPLAY_NAME$', [Security.SecurityElement]::Escape($PublisherDisplayName))
    $manifest = $manifest.Replace('$PUBLISHER$', [Security.SecurityElement]::Escape($Publisher))
    $manifest = $manifest.Replace('$VERSION$', $msixVersion)
    $manifest = $manifest.Replace('$ARCH$', $arch)
    $manifestPath = Join-Path $layout 'AppxManifest.xml'
    [IO.File]::WriteAllText($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))

    # resources.pri: index only the manifest + Assets (in a scratch root with the same relative paths),
    # so the hundreds of runtime DLLs are not catalogued as resources.
    Write-Step "Generating resources.pri for $rid"
    $priRoot = Join-Path $ArtifactsDir "msix\pri-$rid"
    if (Test-Path $priRoot) { Remove-Item $priRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $priRoot | Out-Null
    Copy-Item -Path $assetsDir -Destination (Join-Path $priRoot 'Assets') -Recurse
    Copy-Item -Path $manifestPath -Destination $priRoot
    $priConfig = Join-Path $priRoot 'priconfig.xml'
    Invoke-Native $makepri @('createconfig', '/cf', $priConfig, '/dq', 'en-US', '/pv', '10.0.0', '/o')
    # Drop the <packaging> section: we want one resources.pri, not split resource packages.
    [xml] $priXml = Get-Content -Raw -Path $priConfig
    $packaging = $priXml.SelectSingleNode('//packaging')
    if ($packaging) { [void] $packaging.ParentNode.RemoveChild($packaging) }
    $priXml.Save($priConfig)
    Invoke-Native $makepri @('new', '/pr', $priRoot, '/cf', $priConfig, '/mn', (Join-Path $priRoot 'AppxManifest.xml'),
        '/of', (Join-Path $layout 'resources.pri'), '/o')

    Write-Step "Packing $rid"
    $msixPath = Join-Path $ArtifactsDir "JsonNotepad_${version}_$arch.msix"
    Invoke-Native $makeappx @('pack', '/d', $layout, '/p', $msixPath, '/o')

    if ($signtool -and -not $Bundle) { Invoke-Sign $msixPath }
    $packages += $msixPath
}

# ---------------------------------------------------------------- bundle
$outputs = @($packages)
if ($Bundle) {
    Write-Step 'Creating .msixbundle'
    $bundleInput = Join-Path $ArtifactsDir 'msix\bundle-input'
    if (Test-Path $bundleInput) { Remove-Item $bundleInput -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $bundleInput | Out-Null
    $packages | ForEach-Object { Copy-Item $_ $bundleInput }
    $bundlePath = Join-Path $ArtifactsDir "JsonNotepad_$version.msixbundle"
    Invoke-Native $makeappx @('bundle', '/d', $bundleInput, '/p', $bundlePath, '/bv', $msixVersion, '/o')
    if ($signtool) {
        Invoke-Sign $bundlePath
        $packages | ForEach-Object { Invoke-Sign $_ } # the standalone .msix files stay installable too
    }
    $outputs += $bundlePath
}

# ---------------------------------------------------------------- summary
Write-Step 'Done'
$outputs | ForEach-Object { Write-Host "  $_" -ForegroundColor Green }
if ($CreateDevCertificate) {
    Write-Host @"

To install the dev-signed package on this (or another) machine, trust the certificate once from an
ELEVATED PowerShell, then double-click the .msix/.msixbundle or use Add-AppxPackage:
  Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople
  Add-AppxPackage -Path '$($outputs[-1])'
"@
}
elseif (-not $Sign) {
    Write-Host ''
    Write-Host 'Packages are unsigned: upload them to Partner Center (the Store signs them), or re-run with' -ForegroundColor Yellow
    Write-Host '-CreateDevCertificate / -Sign to produce packages that can be installed locally.' -ForegroundColor Yellow
}
