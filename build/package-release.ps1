<#
.SYNOPSIS
    Stages the files for a GitHub release in artifacts/release/<version>.

.DESCRIPTION
    Builds every download and gives each a stable, version-free asset name, so links of the form
        https://github.com/ismailian/JsonNotepad/releases/latest/download/<asset>
    (used by the website and the README) always point at the newest release:

      JsonNotepad-setup.exe             installer, needs the .NET 9 Desktop Runtime (~2 MB)
      JsonNotepad-setup-standalone.exe  installer with .NET included, no prerequisites (~34 MB)
      JsonNotepad-portable.zip          no installer, needs the .NET 9 Desktop Runtime (<1 MB)
      SHA256SUMS.txt                    checksums of the above

    The standalone installer is too large for Cloudflare Pages (25 MiB per file limit), which is why it is
    distributed through GitHub Releases only.

    With -Publish, the script also creates the release using the GitHub CLI (gh); this requires the tag's
    commit to be pushed and 'gh auth login' to have been run.

.EXAMPLE
    ./build/package-release.ps1
.EXAMPLE
    ./build/package-release.ps1 -SkipBuild -Publish -Draft
#>
[CmdletBinding()]
param(
    [switch] $SkipBuild,
    [switch] $Publish,
    [switch] $Draft,
    [string] $Repository = 'ismailian/JsonNotepad'
)

. (Join-Path $PSScriptRoot 'common.ps1')

$version = Get-ProductVersion
$tag = "v$version"
$standalone = Join-Path $ArtifactsDir "JsonNotepad_${version}_x64_setup.exe"
$small = Join-Path $ArtifactsDir "JsonNotepad_${version}_x64_fd_setup.exe"
$zip = Join-Path $ArtifactsDir "JsonNotepad_${version}_x64_portable.zip"

if (-not $SkipBuild) {
    Write-Step 'Building standalone installer (self-contained)'
    & (Join-Path $PSScriptRoot 'package-inno.ps1')
    if (-not $?) { throw 'package-inno.ps1 failed.' }

    Write-Step 'Building small installer (framework-dependent)'
    & (Join-Path $PSScriptRoot 'package-inno.ps1') -FrameworkDependent
    if (-not $?) { throw 'package-inno.ps1 -FrameworkDependent failed.' }

    Write-Step 'Creating portable zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $ArtifactsDir 'publish\win-x64-fd\*') -DestinationPath $zip -CompressionLevel Optimal
}

$assets = [ordered]@{
    'JsonNotepad-setup.exe'            = $small
    'JsonNotepad-setup-standalone.exe' = $standalone
    'JsonNotepad-portable.zip'         = $zip
}
foreach ($source in $assets.Values) {
    if (-not (Test-Path $source)) { throw "Missing $source. Run without -SkipBuild." }
}

Write-Step "Staging release $tag"
$releaseDir = Join-Path $ArtifactsDir "release\$version"
if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

$sums = New-Object System.Collections.Generic.List[string]
foreach ($name in $assets.Keys) {
    $dest = Join-Path $releaseDir $name
    Copy-Item $assets[$name] $dest
    $hash = (Get-FileHash $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    $sums.Add("$hash  $name")
    Write-Host ("  {0,-34} {1,8:N2} MB  {2}" -f $name, ((Get-Item $dest).Length / 1MB), $hash)
}
$sumsPath = Join-Path $releaseDir 'SHA256SUMS.txt'
Set-Content -Path $sumsPath -Value $sums -Encoding ascii

$notes = @"
## JsonNotepad $version

| Download | Size | Notes |
|---|---|---|
| [JsonNotepad-setup.exe](https://github.com/$Repository/releases/download/$tag/JsonNotepad-setup.exe) | $([math]::Round((Get-Item (Join-Path $releaseDir 'JsonNotepad-setup.exe')).Length / 1MB, 1)) MB | Installer. Needs the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0); setup tells you if it's missing. |
| [JsonNotepad-setup-standalone.exe](https://github.com/$Repository/releases/download/$tag/JsonNotepad-setup-standalone.exe) | $([math]::Round((Get-Item (Join-Path $releaseDir 'JsonNotepad-setup-standalone.exe')).Length / 1MB, 1)) MB | Installer with .NET included. No prerequisites. |
| [JsonNotepad-portable.zip](https://github.com/$Repository/releases/download/$tag/JsonNotepad-portable.zip) | $([math]::Round((Get-Item (Join-Path $releaseDir 'JsonNotepad-portable.zip')).Length / 1MB, 1)) MB | No installer. Needs the .NET 9 Desktop Runtime. |

Windows 10 1809 or later, or Windows 11 (x64). SHA-256 checksums are in ``SHA256SUMS.txt``.
"@
$notesPath = Join-Path $releaseDir 'RELEASE_NOTES.md'
Set-Content -Path $notesPath -Value $notes -Encoding utf8

if ($Publish) {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) { throw 'GitHub CLI (gh) not found. Install it (winget install GitHub.cli) or upload the files in the GitHub web UI.' }
    Write-Step "Creating GitHub release $tag in $Repository"
    $ghArgs = @('release', 'create', $tag, '--repo', $Repository, '--title', "JsonNotepad $version", '--notes-file', $notesPath)
    if ($Draft) { $ghArgs += '--draft' }
    $ghArgs += (Get-ChildItem $releaseDir -File | Where-Object Name -ne 'RELEASE_NOTES.md' | ForEach-Object FullName)
    Invoke-Native $gh.Source $ghArgs
}

Write-Step 'Done'
Write-Host "  $releaseDir" -ForegroundColor Green
if (-not $Publish) {
    Write-Host @"
To publish: push the commit, then on https://github.com/$Repository/releases/new
create tag $tag, paste RELEASE_NOTES.md as the description and attach every other file in the folder.
(Or install the GitHub CLI and re-run with -SkipBuild -Publish.)
Keep the asset names unchanged: the website links to /releases/latest/download/<asset name>.
"@ -ForegroundColor Yellow
}
