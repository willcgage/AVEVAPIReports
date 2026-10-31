<#
.SYNOPSIS
    Runs the tests and publishes a deployable package of PI Reports.

.EXAMPLE
    .\deploy\Build-Package.ps1
    Produces artifacts\PiReports-20261027-1530\ and a matching .zip.
#>
[CmdletBinding()]
param(
    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\artifacts'),
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$name = "PiReports-$(Get-Date -Format 'yyyyMMdd-HHmm')"
$out = Join-Path $OutputRoot $name

Push-Location $repo
try {
    if (-not $SkipTests) {
        dotnet test --configuration Release
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed; no package was built.' }
    }

    # Regenerate the DBA's schema script so it always matches the migrations in this build.
    dotnet tool restore | Out-Null
    dotnet ef migrations script --idempotent -p src/PiReports.Core -s src/PiReports.Web -o docs/dba/02-pireports-schema.sql
    if ($LASTEXITCODE -ne 0) { throw 'Generating the schema script failed.' }

    dotnet publish src/PiReports.Web --configuration Release --output $out
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    # The deploy script and DBA scripts travel with the package.
    Copy-Item (Join-Path $repo 'deploy\Deploy-PiReports.ps1') $out
    Copy-Item (Join-Path $repo 'docs\dba') (Join-Path $out 'dba') -Recurse

    Compress-Archive -Path (Join-Path $out '*') -DestinationPath "$out.zip" -Force
    Write-Host "Package: $out.zip"
}
finally {
    Pop-Location
}
