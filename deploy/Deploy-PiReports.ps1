<#
.SYNOPSIS
    Installs or upgrades PI Reports on a Test or Production server under IIS.

.DESCRIPTION
    Run from an elevated PowerShell on the target server, from inside an unzipped package
    built by Build-Package.ps1. Safe to re-run: it creates what is missing and upgrades the rest.

    The server's own appsettings.<Environment>.json is never overwritten once it exists, so
    connection strings and server names edited on that server survive upgrades.

.EXAMPLE
    .\Deploy-PiReports.ps1 -Environment Test -ServiceAccount 'CORP\gmsa-pirpt-tst$'

    Two ways to sit next to PI Vision (see docs\deployment.md, "Ports and bindings"):
      -HostingMode Application (default)  /PIReports under PI Vision's site. Same port and certificate, nothing new to open.
      -HostingMode Site                   a separate IIS site on its own HTTPS port, e.g. 8443.

.EXAMPLE
    .\Deploy-PiReports.ps1 -Environment Production -ServiceAccount 'CORP\gmsa-pirpt-prd$' -WhatIf

.EXAMPLE
    .\Deploy-PiReports.ps1 -Environment Test -ServiceAccount 'CORP\svc-pirpt-tst'
    Regular domain service account (no gMSA): prompts for its password.

.EXAMPLE
    .\Deploy-PiReports.ps1 -Environment Test -ServiceAccount 'CORP\gmsa-pirpt-tst$' `
        -HostingMode Site -Port 8443 -CertificateThumbprint 'AB12...' -OpenFirewall
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Test', 'Production')]
    [string] $Environment,

    # Account the app pool runs as. A gMSA ends in $ and needs no password; a regular
    # domain service account (e.g. CORP\svc-pirpt-tst) is prompted for its password.
    [Parameter(Mandatory)]
    [string] $ServiceAccount,

    # Password for a regular service account, to skip the prompt. Ignored for a gMSA.
    [securestring] $ServiceAccountPassword,

    [string] $PackagePath = $PSScriptRoot,
    [string] $InstallRoot = 'D:\PiReports',
    [ValidateSet('Application', 'Site')]
    [string] $HostingMode = 'Application',

    # Application mode: the existing site to add /PIReports to (usually PI Vision's).
    # Site mode: the name of the new site.
    [string] $SiteName,
    [string] $AppName = 'PIReports',
    [string] $AppPoolName = 'PIReports',

    # Site mode only.
    [int] $Port = 8443,
    [string] $HostName = '',
    [string] $CertificateThumbprint,
    [switch] $OpenFirewall
)

$ErrorActionPreference = 'Stop'

$isGmsa = $ServiceAccount.EndsWith('$')
if (-not $isGmsa -and -not $ServiceAccountPassword -and -not $WhatIfPreference) {
    $ServiceAccountPassword = Read-Host "Password for $ServiceAccount" -AsSecureString
}

if (-not $SiteName) {
    $SiteName = if ($HostingMode -eq 'Site') { 'PI Reports' } else { 'Default Web Site' }
}
if ($HostingMode -eq 'Site' -and -not $CertificateThumbprint) {
    throw 'Site mode needs -CertificateThumbprint: the HTTPS certificate (in LocalMachine\My) for the new port.'
}

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell.'
}

Import-Module WebAdministration
$appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
$appPath = Join-Path $InstallRoot 'App'
$envFile = "appsettings.$Environment.json"

if (-not (Test-Path (Join-Path $PackagePath 'PiReports.Web.dll'))) {
    throw "No PI Reports package found in $PackagePath. Unzip the package and run the script from inside it."
}

# --- Folders ---------------------------------------------------------------
$folders = @($appPath, (Join-Path $InstallRoot 'Output'), (Join-Path $InstallRoot 'Keys'), (Join-Path $InstallRoot 'Logs'))
if ($Environment -eq 'Test') { $folders += Join-Path $InstallRoot 'TestShares' }
foreach ($folder in $folders) {
    if (-not (Test-Path $folder) -and $PSCmdlet.ShouldProcess($folder, 'Create folder')) {
        New-Item -ItemType Directory -Path $folder | Out-Null
    }
}

foreach ($folder in $folders | Where-Object { $_ -ne $appPath }) {
    if ($PSCmdlet.ShouldProcess($folder, "Grant Modify to $ServiceAccount")) {
        icacls $folder /grant "${ServiceAccount}:(OI)(CI)M" /T /Q | Out-Null
    }
}
if ($PSCmdlet.ShouldProcess($appPath, "Grant Read to $ServiceAccount")) {
    icacls $appPath /grant "${ServiceAccount}:(OI)(CI)RX" /T /Q | Out-Null
}

# --- App pool ----------------------------------------------------------------
$poolPath = "IIS:\AppPools\$AppPoolName"
if (-not (Test-Path $poolPath) -and $PSCmdlet.ShouldProcess($AppPoolName, 'Create app pool')) {
    New-WebAppPool -Name $AppPoolName | Out-Null
}

if ($PSCmdlet.ShouldProcess($AppPoolName, 'Configure app pool')) {
    Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ''
    Set-ItemProperty $poolPath -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty $poolPath -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty $poolPath -Name processModel.loadUserProfile -Value $true
    # SpecificUser identity. A gMSA has an empty password; IIS encrypts a regular account's password in applicationHost.config.
    $plainPassword = if ($isGmsa) { '' } else { [Net.NetworkCredential]::new('', $ServiceAccountPassword).Password }
    Set-ItemProperty $poolPath -Name processModel -Value @{ identityType = 3; userName = $ServiceAccount; password = $plainPassword }
    $plainPassword = $null
    # Recycle at a fixed early-morning time instead of every 29 hours, so it never lands mid-capture by surprise.
    Set-ItemProperty $poolPath -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
    Clear-ItemProperty $poolPath -Name recycling.periodicRestart.schedule
    Set-ItemProperty $poolPath -Name recycling.periodicRestart.schedule -Value @{ value = '03:15:00' }

    # The environment decides which appsettings.<Environment>.json the app reads.
    & $appcmd set config -section:system.applicationHost/applicationPools `
        "/-[name='$AppPoolName'].environmentVariables.[name='ASPNETCORE_ENVIRONMENT']" /commit:apphost 2>$null | Out-Null
    & $appcmd set config -section:system.applicationHost/applicationPools `
        "/+[name='$AppPoolName'].environmentVariables.[name='ASPNETCORE_ENVIRONMENT',value='$Environment']" /commit:apphost | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not set ASPNETCORE_ENVIRONMENT on the app pool.' }
}

# --- Files -------------------------------------------------------------------
$firstInstall = -not (Test-Path (Join-Path $appPath $envFile))
if ($PSCmdlet.ShouldProcess($appPath, 'Copy application files')) {
    if ((Get-WebAppPoolState -Name $AppPoolName).Value -eq 'Started') {
        Stop-WebAppPool -Name $AppPoolName
        while ((Get-WebAppPoolState -Name $AppPoolName).Value -ne 'Stopped') { Start-Sleep -Milliseconds 500 }
    }

    # Keep this server's settings files; copy everything else.
    $keep = @('appsettings.Test.json', 'appsettings.Production.json') | Where-Object { Test-Path (Join-Path $appPath $_) }
    $exclude = @('Deploy-PiReports.ps1') + $keep
    robocopy $PackagePath $appPath /E /NFL /NDL /NJH /NJS /NP /XD dba /XF @exclude | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying files failed (robocopy exit code $LASTEXITCODE)." }
}

# Send the app's startup errors to a log file the first time something goes wrong.
$webConfig = Join-Path $appPath 'web.config'
if ((Test-Path $webConfig) -and $PSCmdlet.ShouldProcess($webConfig, 'Point stdout log at the Logs folder')) {
    [xml] $xml = Get-Content $webConfig
    $aspNetCore = $xml.SelectSingleNode('//aspNetCore')
    $aspNetCore.SetAttribute('stdoutLogFile', (Join-Path $InstallRoot 'Logs\stdout'))
    $xml.Save($webConfig)
}

# --- Web application ---------------------------------------------------------
if ($HostingMode -eq 'Application') {
    if (-not (Test-Path "IIS:\Sites\$SiteName")) {
        throw "Site '$SiteName' does not exist. Pass -SiteName with the site PI Vision runs under."
    }

    $location = "$SiteName/$AppName"
    $iisPath = "IIS:\Sites\$SiteName\$AppName"
    if (-not (Test-Path $iisPath) -and $PSCmdlet.ShouldProcess($location, 'Create web application')) {
        New-WebApplication -Site $SiteName -Name $AppName -PhysicalPath $appPath -ApplicationPool $AppPoolName | Out-Null
    }
}
else {
    $location = $SiteName
    $iisPath = "IIS:\Sites\$SiteName"
    $inUse = Get-WebBinding | Where-Object { $_.bindingInformation -like "*:${Port}:*" -and $_.ItemXPath -notlike "*name='$SiteName'*" }
    if ($inUse) {
        throw "Port $Port is already bound by another IIS site. Choose a different -Port."
    }

    if (-not (Test-Path $iisPath) -and $PSCmdlet.ShouldProcess($SiteName, "Create site on HTTPS port $Port")) {
        New-Website -Name $SiteName -PhysicalPath $appPath -ApplicationPool $AppPoolName -Port $Port -HostHeader $HostName -Ssl | Out-Null
    }

    if ($PSCmdlet.ShouldProcess($SiteName, "Bind certificate $CertificateThumbprint to port $Port")) {
        Set-ItemProperty $iisPath -Name physicalPath -Value $appPath
        $binding = Get-WebBinding -Name $SiteName -Protocol https -Port $Port
        if (-not $binding) {
            New-WebBinding -Name $SiteName -Protocol https -Port $Port -HostHeader $HostName -SslFlags ([int][bool]$HostName)
            $binding = Get-WebBinding -Name $SiteName -Protocol https -Port $Port
        }
        $binding.AddSslCertificate($CertificateThumbprint, 'My')
    }

    if ($OpenFirewall -and $PSCmdlet.ShouldProcess("TCP $Port", 'Open inbound firewall rule')) {
        $ruleName = "PI Reports HTTPS ($Port)"
        if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
        }
    }
}

if ($PSCmdlet.ShouldProcess($location, 'Configure preload and Windows Authentication')) {
    if ($HostingMode -eq 'Application') {
        Set-ItemProperty $iisPath -Name applicationPool -Value $AppPoolName
        Set-ItemProperty $iisPath -Name preloadEnabled -Value $true
    }
    else {
        Set-ItemProperty $iisPath -Name applicationDefaults.applicationPool -Value $AppPoolName
        Set-ItemProperty $iisPath -Name applicationDefaults.preloadEnabled -Value $true
    }
    Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $location `
        -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value $true
    Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $location `
        -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $false
}

if ($PSCmdlet.ShouldProcess($AppPoolName, 'Start app pool')) {
    Start-WebAppPool -Name $AppPoolName
}

Write-Host ''
$url = if ($HostingMode -eq 'Application') { "https://<pi-vision-host>/$AppName" } else { "https://$(if ($HostName) { $HostName } else { '<this-server>' }):$Port/" }
Write-Host "PI Reports ($Environment) is installed at $appPath as $location. Browse to $url"
Write-Host "Set PiReports:PortalBaseUrl in $envFile to that address so email links work."
if ($firstInstall) {
    Write-Warning "First install: edit $(Join-Path $appPath $envFile) (connection string, server names, admin group), then recycle the '$AppPoolName' app pool."
}
