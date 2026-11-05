<#
.SYNOPSIS
    Copies the downloadable installers into site/downloads for Cloudflare Pages.

.DESCRIPTION
    Builds (unless -SkipBuild) the small framework-dependent installer and a portable zip, copies them
    into site/downloads with stable "latest" names plus versioned names, and writes SHA256SUMS.txt.

    Cloudflare Pages rejects any single file larger than 25 MiB, so the self-contained setup (~34 MB)
    and the MSIX (~45 MB) are NOT published here. Ship those through GitHub Releases or the Microsoft Store.
    The script fails if anything in site/downloads would exceed the limit.

.EXAMPLE
    ./build/package-site.ps1
    npx wrangler pages deploy site --project-name jsonnotepad
#>
[CmdletBinding()]
param(
    [switch] $SkipBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')

$MaxBytes = 25MB
$version = Get-ProductVersion
$siteDownloads = Join-Path $RepoRoot 'site\downloads'
if (-not (Test-Path (Join-Path $RepoRoot 'site\index.html'))) {
    throw "site/index.html not found. The website sources live in site/ (gitignored); restore them before packaging."
}

$setup = Join-Path $ArtifactsDir "JsonNotepad_${version}_x64_fd_setup.exe"
$publishDir = Join-Path $ArtifactsDir 'publish\win-x64-fd'
$zip = Join-Path $ArtifactsDir "JsonNotepad_${version}_x64_portable.zip"

if (-not $SkipBuild) {
    Write-Step 'Building framework-dependent installer'
    & (Join-Path $PSScriptRoot 'package-inno.ps1') -FrameworkDependent
    if (-not $?) { throw 'package-inno.ps1 failed.' }
    Write-Step 'Creating portable zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal
}
foreach ($f in $setup, $zip) {
    if (-not (Test-Path $f)) { throw "Missing $f. Run without -SkipBuild." }
}

Write-Step 'Copying downloads'
New-Item -ItemType Directory -Force -Path $siteDownloads | Out-Null
Get-ChildItem $siteDownloads -File | Remove-Item -Force

$files = [ordered]@{
    "JsonNotepad-$version-setup.exe"    = $setup
    "JsonNotepad-$version-portable.zip" = $zip
    'JsonNotepad-setup.exe'             = $setup   # stable "latest" link used by the site
    'JsonNotepad-portable.zip'          = $zip
}
$sums = New-Object System.Collections.Generic.List[string]
foreach ($name in $files.Keys) {
    $dest = Join-Path $siteDownloads $name
    Copy-Item $files[$name] $dest
    $size = (Get-Item $dest).Length
    if ($size -gt $MaxBytes) { throw "$name is $([math]::Round($size / 1MB, 1)) MB; Cloudflare Pages allows at most 25 MiB per file." }
    $hash = (Get-FileHash $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    $sums.Add("$hash  $name")
    Write-Host ("  {0,-36} {1,8:N2} MB" -f $name, ($size / 1MB))
}
Set-Content -Path (Join-Path $siteDownloads 'SHA256SUMS.txt') -Value $sums -Encoding ascii

Write-Step 'Done'
Write-Host "Deploy with: npx wrangler pages deploy site --project-name jsonnotepad" -ForegroundColor Green
Write-Host "Remember to update the version, sizes and hashes shown on site/index.html if they changed." -ForegroundColor Yellow
