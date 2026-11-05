# Shared helpers for the JsonNotepad build scripts. Dot-source it:  . "$PSScriptRoot\common.ps1"
# Works on Windows PowerShell 5.1 and PowerShell 7+. Keep this file ASCII-only (PS 5.1 reads BOM-less files as ANSI).

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:AppProject = Join-Path $script:RepoRoot 'src\JsonNotepad.App\JsonNotepad.App.csproj'
$script:ArtifactsDir = Join-Path $script:RepoRoot 'artifacts'

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Native {
    <# Runs an external program and throws if it exits with a non-zero code. #>
    param(
        [Parameter(Mandatory, Position = 0)] [string] $FilePath,
        [Parameter(Position = 1)] [string[]] $Arguments = @()
    )
    Write-Host "   > $([IO.Path]::GetFileName($FilePath)) $($Arguments -join ' ')" -ForegroundColor DarkGray
    # Out-Host keeps tool output on the console instead of leaking into the caller's return value.
    & $FilePath @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "'$([IO.Path]::GetFileName($FilePath))' failed with exit code $LASTEXITCODE."
    }
}

function Assert-DotNet {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET SDK (dotnet) was not found on PATH. Install the .NET 9 SDK from https://dotnet.microsoft.com/download.'
    }
}

function Assert-AppProject {
    if (-not (Test-Path $script:AppProject)) {
        throw "Application project not found: $script:AppProject"
    }
}

function Get-ProductVersion {
    <#
      Returns VersionPrefix (Major.Minor.Patch) - the single source of truth in Directory.Build.props.
      Asks MSBuild first (honours any overrides), falls back to parsing the props file.
    #>
    $version = $null
    if ((Test-Path $script:AppProject) -and (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        try {
            $out = & dotnet msbuild $script:AppProject -nologo -getProperty:VersionPrefix 2>$null
            if ($LASTEXITCODE -eq 0 -and $out) {
                $version = ([string]($out | Select-Object -Last 1)).Trim()
            }
        }
        catch {
            $version = $null
        }
    }

    if (-not $version) {
        $props = Join-Path $script:RepoRoot 'Directory.Build.props'
        [xml] $xml = Get-Content -Raw -Path $props
        $node = $xml.SelectSingleNode('//VersionPrefix')
        if (-not $node) { throw "VersionPrefix not found in $props." }
        $version = $node.InnerText.Trim()
    }

    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "VersionPrefix '$version' must be Major.Minor.Patch (three numeric parts). The 4th (revision) part is reserved: MSIX requires it to be 0."
    }
    return $version
}

function Publish-App {
    <#
      Non-single-file publish of JsonNotepad.App for one RID. Returns the output folder.
      Self-contained by default; -FrameworkDependent produces a ~2 MB build that needs the
      .NET 9 Desktop Runtime installed (output folder gets a '-fd' suffix).
    #>
    param(
        [Parameter(Mandatory)] [string] $RuntimeIdentifier,
        [string] $Configuration = 'Release',
        [switch] $FrameworkDependent
    )
    Assert-DotNet
    Assert-AppProject
    $folder = if ($FrameworkDependent) { "$RuntimeIdentifier-fd" } else { $RuntimeIdentifier }
    $out = Join-Path $script:ArtifactsDir "publish\$folder"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    Invoke-Native dotnet @('publish', $script:AppProject, '-c', $Configuration, '-r', $RuntimeIdentifier,
        '--self-contained', $selfContained, '-p:PublishSingleFile=false', '-o', $out, '-nologo')
    if (-not (Test-Path (Join-Path $out 'JsonNotepad.exe'))) {
        throw "Publish succeeded but JsonNotepad.exe is missing from $out (check AssemblyName in JsonNotepad.App.csproj)."
    }
    return $out
}

function Find-WindowsSdkTool {
    <#
      Locates a Windows SDK tool (makeappx.exe, makepri.exe, signtool.exe) in the newest installed
      Windows 10/11 SDK. Returns the full path or throws with install guidance.
    #>
    param([Parameter(Mandatory)] [string] $Name)

    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($key in 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots',
                     'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots') {
        $kitsRoot = (Get-ItemProperty -Path $key -Name KitsRoot10 -ErrorAction SilentlyContinue).KitsRoot10
        if ($kitsRoot) { $roots.Add($kitsRoot) }
    }
    foreach ($pf in ${env:ProgramFiles(x86)}, $env:ProgramFiles) {
        if ($pf) { $roots.Add((Join-Path $pf 'Windows Kits\10')) }
    }

    $hostArchs = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64', 'x64', 'x86' } else { 'x64', 'x86' }

    foreach ($root in ($roots | Select-Object -Unique)) {
        $bin = Join-Path $root 'bin'
        if (-not (Test-Path $bin)) { continue }
        $versions = Get-ChildItem $bin -Directory |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object { [version] $_.Name } -Descending
        foreach ($v in $versions) {
            foreach ($arch in $hostArchs) {
                $candidate = Join-Path $v.FullName "$arch\$Name"
                if (Test-Path $candidate) { return $candidate }
            }
        }
        foreach ($arch in $hostArchs) {
            $candidate = Join-Path $bin "$arch\$Name" # very old SDK layout
            if (Test-Path $candidate) { return $candidate }
        }
    }

    $onPath = Get-Command $Name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    throw @"
$Name was not found. It ships with the Windows SDK.
Install the Windows 11 SDK (10.0.22621 or newer) from https://developer.microsoft.com/windows/downloads/windows-sdk/
or via the Visual Studio Installer (Individual components > Windows 11 SDK), then re-run this script.
Searched: $((($roots | Select-Object -Unique) | ForEach-Object { Join-Path $_ 'bin\<version>\<arch>' }) -join '; ')
"@
}
