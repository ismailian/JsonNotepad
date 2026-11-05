<#
.SYNOPSIS
    Restores and builds the whole solution, then runs the core test suite.

.DESCRIPTION
    dotnet restore + dotnet build JsonNotepad.sln (warnings are errors via Directory.Build.props), then
    runs tests/JsonNotepad.Core.Tests with `dotnet run` (it is a self-hosted console test runner) if it exists.

.EXAMPLE
    ./build/build.ps1
.EXAMPLE
    ./build/build.ps1 -Configuration Debug -SkipTests
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $SkipTests
)

. (Join-Path $PSScriptRoot 'common.ps1')
Assert-DotNet

$solution = Join-Path $RepoRoot 'JsonNotepad.sln'
$version = Get-ProductVersion
Write-Host "JsonNotepad $version - $Configuration build"

Write-Step 'Restoring'
Invoke-Native dotnet @('restore', $solution, '-nologo')

Write-Step "Building ($Configuration)"
Invoke-Native dotnet @('build', $solution, '-c', $Configuration, '--no-restore', '-nologo')

$testsDir = Join-Path $RepoRoot 'tests\JsonNotepad.Core.Tests'
$testProject = Get-ChildItem -Path $testsDir -Filter '*.csproj' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($SkipTests) {
    Write-Host 'Tests skipped (-SkipTests).' -ForegroundColor Yellow
}
elseif ($testProject) {
    Write-Step "Running tests ($($testProject.Name))"
    Invoke-Native dotnet @('run', '--project', $testProject.FullName, '-c', $Configuration)
}
else {
    Write-Host "No test project found in $testsDir - skipping tests." -ForegroundColor Yellow
}

Write-Step 'Build succeeded'
