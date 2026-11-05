<#
.SYNOPSIS
    Builds the classic JsonNotepad setup.exe (Inno Setup) for win-x64.

.DESCRIPTION
    Publishes src/JsonNotepad.App self-contained for win-x64 into artifacts/publish/win-x64, then compiles
    installer/inno/JsonNotepad.iss with ISCC.exe, passing the version from Directory.Build.props.
    Output: artifacts/JsonNotepad_<version>_x64_setup.exe
    With -FrameworkDependent: artifacts/JsonNotepad_<version>_x64_fd_setup.exe (small; needs the .NET 9
    Desktop Runtime, which the installer checks for and offers to download).

.EXAMPLE
    ./build/package-inno.ps1
.EXAMPLE
    ./build/package-inno.ps1 -SkipPublish -IsccPath 'D:\Tools\Inno Setup 6\ISCC.exe'
.EXAMPLE
    ./build/package-inno.ps1 -UseDevCertificate
    Signs JsonNotepad.exe and the setup with the self-signed certificate created by
    package-msix.ps1 -CreateDevCertificate (local testing only; Windows does not trust it).
.EXAMPLE
    ./build/package-inno.ps1 -CertificateThumbprint <sha1> -TimestampUrl http://timestamp.digicert.com
    Signs with a trusted certificate from the certificate store / hardware token.
.EXAMPLE
    ./build/package-inno.ps1 -CertificatePath cert.pfx -CertificatePassword *** -TimestampUrl http://timestamp.digicert.com
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $SkipPublish,
    [switch] $FrameworkDependent,
    [string] $IsccPath,

    # Signing (all optional; with none of these the setup is unsigned)
    [string] $CertificatePath,
    [string] $CertificatePassword,
    [string] $CertificateThumbprint,
    [switch] $UseDevCertificate,
    [string] $TimestampUrl
)

. (Join-Path $PSScriptRoot 'common.ps1')

function Find-Iscc {
    if ($IsccPath) {
        if (Test-Path $IsccPath) { return (Resolve-Path $IsccPath).Path }
        throw "ISCC.exe not found at -IsccPath '$IsccPath'."
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    foreach ($key in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
                     'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
                     'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1') {
        $props = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($props -and $props.PSObject.Properties['InstallLocation'] -and $props.InstallLocation) {
            $candidates.Add((Join-Path $props.InstallLocation 'ISCC.exe'))
        }
    }
    foreach ($base in ${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs')) {
        if ($base) { $candidates.Add((Join-Path $base 'Inno Setup 6\ISCC.exe')) }
    }
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

$iscc = Find-Iscc
if (-not $iscc) {
    throw @"
Inno Setup compiler (ISCC.exe) was not found.
Install Inno Setup 6.3 or newer from https://jrsoftware.org/isdl.php
(or: winget install --id JRSoftware.InnoSetup -e), then re-run this script,
or point to it explicitly with -IsccPath.
"@
}

$version = Get-ProductVersion
Write-Host "JsonNotepad $version - Inno Setup ($iscc)"

$publishDir = Join-Path $ArtifactsDir ($(if ($FrameworkDependent) { 'publish\win-x64-fd' } else { 'publish\win-x64' }))
if ($SkipPublish) {
    if (-not (Test-Path (Join-Path $publishDir 'JsonNotepad.exe'))) {
        throw "-SkipPublish was given but $publishDir does not contain JsonNotepad.exe."
    }
}
else {
    Write-Step "Publishing win-x64 ($Configuration, $(if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }))"
    $publishDir = Publish-App -RuntimeIdentifier 'win-x64' -Configuration $Configuration -FrameworkDependent:$FrameworkDependent
}

# ---------------------------------------------------------------- signing setup
$signArgsBase = $null
if ($UseDevCertificate -and -not $CertificateThumbprint) {
    $dev = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.HasPrivateKey -and $_.Subject -like 'CN=JsonNotepad*' -and $_.NotAfter -gt (Get-Date) -and
                       ($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' }) } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
    if (-not $dev) { throw 'No JsonNotepad dev certificate found. Run ./build/package-msix.ps1 -CreateDevCertificate first.' }
    $CertificateThumbprint = $dev.Thumbprint
}
if ($CertificateThumbprint) {
    $signArgsBase = @('sign', '/fd', 'SHA256', '/sha1', $CertificateThumbprint)
}
elseif ($CertificatePath) {
    $signArgsBase = @('sign', '/fd', 'SHA256', '/f', (Resolve-Path $CertificatePath).Path)
    if ($CertificatePassword) { $signArgsBase += @('/p', $CertificatePassword) }
}
if ($signArgsBase) {
    if ($TimestampUrl) { $signArgsBase += @('/tr', $TimestampUrl, '/td', 'SHA256') }
    $signtool = Find-WindowsSdkTool 'signtool.exe'
}

function Invoke-Sign([string] $File) {
    Write-Host "   > signtool sign $File" -ForegroundColor DarkGray
    & $signtool @($signArgsBase + $File) | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE) for $File." }
}

if ($signArgsBase) {
    Write-Step 'Signing application executable'
    Invoke-Sign (Join-Path $publishDir 'JsonNotepad.exe')
}

Write-Step 'Compiling installer'
$iss = Join-Path $RepoRoot 'installer\inno\JsonNotepad.iss'
$isccArgs = @("/DAppVersion=$version", "/DSourceDir=$publishDir", "/DOutputDir=$ArtifactsDir", '/Q')
if ($FrameworkDependent) { $isccArgs += '/DFrameworkDependent=1' }
Invoke-Native $iscc ($isccArgs + $iss)

$suffix = if ($FrameworkDependent) { '_x64_fd_setup.exe' } else { '_x64_setup.exe' }
$setup = Join-Path $ArtifactsDir "JsonNotepad_${version}$suffix"
if ($signArgsBase) {
    Write-Step 'Signing installer'
    Invoke-Sign $setup
}
Write-Step 'Done'
Write-Host "  $setup" -ForegroundColor Green
