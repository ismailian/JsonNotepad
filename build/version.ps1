<#
.SYNOPSIS
    Shows or bumps the product version (VersionPrefix in Directory.Build.props).

.DESCRIPTION
    Directory.Build.props is the single source of truth for the version. Assemblies, the MSIX manifest
    (Major.Minor.Patch.0) and the Inno Setup installer all derive from it.
    Without arguments the current version is printed.

.EXAMPLE
    ./build/version.ps1               # prints 1.0.0
.EXAMPLE
    ./build/version.ps1 patch         # 1.0.0 -> 1.0.1
.EXAMPLE
    ./build/version.ps1 minor         # 1.0.1 -> 1.1.0
.EXAMPLE
    ./build/version.ps1 -Set 2.0.0
#>
[CmdletBinding(DefaultParameterSetName = 'Show')]
param(
    [Parameter(ParameterSetName = 'Bump', Mandatory, Position = 0)]
    [ValidateSet('major', 'minor', 'patch')]
    [string] $Part,

    [Parameter(ParameterSetName = 'Set', Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Set
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$props = (Resolve-Path (Join-Path $PSScriptRoot '..\Directory.Build.props')).Path
$bytes = [IO.File]::ReadAllBytes($props)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$text = [IO.File]::ReadAllText($props)

$pattern = '<VersionPrefix>\s*(\d+)\.(\d+)\.(\d+)\s*</VersionPrefix>'
$match = [regex]::Match($text, $pattern)
if (-not $match.Success) {
    throw "No <VersionPrefix>Major.Minor.Patch</VersionPrefix> element found in $props."
}

$current = "$($match.Groups[1].Value).$($match.Groups[2].Value).$($match.Groups[3].Value)"
if ($PSCmdlet.ParameterSetName -eq 'Show') {
    Write-Output $current
    return
}

if ($PSCmdlet.ParameterSetName -eq 'Set') {
    $new = $Set
}
else {
    [int] $major = $match.Groups[1].Value
    [int] $minor = $match.Groups[2].Value
    [int] $patch = $match.Groups[3].Value
    switch ($Part) {
        'major' { $major++; $minor = 0; $patch = 0 }
        'minor' { $minor++; $patch = 0 }
        'patch' { $patch++ }
    }
    $new = "$major.$minor.$patch"
}

$updated = $text.Substring(0, $match.Index) + "<VersionPrefix>$new</VersionPrefix>" +
    $text.Substring($match.Index + $match.Length)
[IO.File]::WriteAllText($props, $updated, (New-Object System.Text.UTF8Encoding($hasBom)))

Write-Host "VersionPrefix: $current -> $new"
Write-Host "Next: commit, then tag the release:  git tag v$new"
