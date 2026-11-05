<#
.SYNOPSIS
    Regenerates the JsonNotepad logo assets with tools/IconGen.

.DESCRIPTION
    Writes src/JsonNotepad.App/Resources/AppIcon.ico, installer/msix/Assets/*.png and docs/images/logo.png.
    Pass -OutputRoot to write somewhere else (e.g. to preview changes without touching the repo).
#>
[CmdletBinding()]
param([string] $OutputRoot)

. (Join-Path $PSScriptRoot 'common.ps1')
Assert-DotNet

$project = Join-Path $RepoRoot 'tools\IconGen\IconGen.csproj'
Write-Step 'Building IconGen'
Invoke-Native dotnet @('build', $project, '-c', 'Release', '-nologo', '-v', 'quiet')

Write-Step 'Generating assets'
$exe = Join-Path $RepoRoot 'tools\IconGen\bin\Release\net9.0-windows\IconGen.exe'
$iconArgs = @()
if ($OutputRoot) { $iconArgs += $OutputRoot } else { $iconArgs += $RepoRoot }
Invoke-Native $exe $iconArgs
