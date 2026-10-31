# Deploying PI Reports to Test and Production

PI Reports runs on each PI Vision server as its own IIS application, with its own database on that environment's AF SQL Server instance. Test and Production run the **same package**. What differs is one environment variable on the app pool and one settings file per server.

| | Test | Production |
|---|---|---|
| App pool `ASPNETCORE_ENVIRONMENT` | `Test` | `Production` |
| Settings file the app reads | `appsettings.Test.json` | `appsettings.Production.json` |
| Database | `PiReports_Test` | `PiReports` |
| Service account (gMSA) | `CORP\gmsa-pirpt-tst$` | `CORP\gmsa-pirpt-prd$` |
| Email | all redirected to one tester, subject tagged `[TEST]` | real recipients |
| Shared-folder files | written under `D:\PiReports\TestShares` | real shares |
| Portal | orange TEST banner | no banner |

**Never point both servers at one database.** Each server's scheduler runs every job it finds in its database, so sharing one means duplicate emails.

## The release flow

1. Build a package on your workstation: `.\deploy\Build-Package.ps1`. It runs the tests, regenerates the DBA's schema script, and writes `artifacts\PiReports-<date>.zip`.
2. Send the DBA `dba\02-pireports-schema.sql` and `dba\03-hangfire-schema.sql` from the package, for both databases, and wait for confirmation (step 2). This matters whenever the release changes the database. The scripts are safe to re-run, so sending them every time is simplest.
3. Deploy to Test: copy the zip to the Test server, unzip, and run `Deploy-PiReports.ps1 -Environment Test ...`.
4. Set up or change captures on Test and check the output. Then use **Export / import** to download them as JSON.
5. Deploy the same zip to Production, then import the JSON there. Captures import disabled by default, so you can check recipients and shared folders before enabling each one.

## 1. Service accounts

Create one **gMSA** per environment. Passwords rotate automatically and none is stored anywhere. Separate accounts mean the Test account simply has no rights on production shares.

The names in this guide (`CORP\gmsa-pirpt-tst$`, `CORP\gmsa-pirpt-prd$`) are examples. `CORP` stands for your domain's short (NetBIOS) name. The `$` is part of every gMSA's logon name and is always written that way. Pick names that fit your AD naming standard, but keep the part before the `$` to **15 characters or fewer**, because AD rejects or truncates longer gMSA logon names.

```powershell
# On a domain controller or with RSAT
New-ADServiceAccount gmsa-pirpt-tst -DNSHostName gmsa-pirpt-tst.corp.local `
  -PrincipalsAllowedToRetrieveManagedPassword "PIVISION-TEST$"
New-ADServiceAccount gmsa-pirpt-prd -DNSHostName gmsa-pirpt-prd.corp.local `
  -PrincipalsAllowedToRetrieveManagedPassword "PIVISION01$"

# On each PI Vision server, for its own account
Install-ADServiceAccount gmsa-pirpt-tst
Test-ADServiceAccount gmsa-pirpt-tst
```

### If your domain doesn't have gMSAs set up yet

You have two choices, and everything else in this guide stays the same either way.

**Turn gMSAs on (recommended, one-time, about 10 minutes of AD admin work).** A domain admin runs this once per forest, on a domain controller:

```powershell
Add-KdsRootKey -EffectiveImmediately
```

The key takes up to 10 hours to replicate before the first gMSA can be created, so do it the day before. It needs domain controllers running Windows Server 2012 or later, and it changes nothing for existing accounts.

**Or use a regular domain service account.** Ask AD for a normal user account per environment, for example `CORP\svc-pirpt-tst` and `CORP\svc-pirpt-prd` (no `$`). The account:

- doesn't need to be interactive, so it can be denied local and remote desktop logon
- needs a long random password, which you'll type once into the deploy script. IIS stores it encrypted.
- gets exactly the same PI Vision, SQL and share rights described below. The DBA uses that name in script 01, without a `$`.

The catch is the password. When it expires or someone resets it, the app pool stops and captures quietly stop running. Either exempt the account from expiry under your service-account policy, or diary the rotation. To rotate, re-run the deploy script with the new password, or update the app pool identity in IIS Manager.

Moving to a gMSA later is straightforward. Create the gMSA, have the DBA add a login for it using script 01, grant it the same PI Vision and share rights, and re-run the deploy script with `-ServiceAccount 'CORP\gmsa-pirpt-tst$'`.

Then give each account:

- **PI Vision:** read access to every display it should capture. Sharing the displays, or their folders, with an AD group that contains the account works.
- **PI Data Archive and AF:** read access, through the same PI mapping your PI Vision users get.
- **File shares (Production only):** modify rights on each delivery folder. Grant them to an **AD group containing the gMSA**, because some NAS permission pickers can't find gMSAs.

## 2. Database (handed off to the DBA)

The DBA creates and owns the databases. The app only reads and writes data, and never creates or changes tables. Do this before the first install, because the app won't start without its tables.

### What you send the DBA

1. The [dba](dba/) folder from the release package (`dba\` inside the zip, or `docs\dba\` in the repository). It holds:
   - [README.md](dba/README.md): the one-page request
   - `01-create-database.sql`: the database, login and permissions
   - `02-pireports-schema.sql`: the app's tables
   - `03-hangfire-schema.sql`: the scheduler's tables
   - `04-verify.sql`: a check they run at the end
2. The values to fill into the README's table, for each environment:

| You provide | Test | Production |
|---|---|---|
| SQL instance (the AF one) | e.g. `AFSQL-TEST` | e.g. `AFSQL-PROD` |
| Database name | `PiReports_Test` | `PiReports` |
| gMSA login (step 1) | `CORP\gmsa-pirpt-tst$` | `CORP\gmsa-pirpt-prd$` |
| App server that connects | PI Vision test server | PI Vision production server |

The gMSA must exist before the DBA runs script 01, because `CREATE LOGIN ... FROM WINDOWS` fails on an account AD doesn't know.

### What the DBA does

For each environment, in SSMS with **Query > SQLCMD Mode** on:

1. Set `DatabaseName` and `ServiceAccount` at the top of `01-create-database.sql`, and optionally `DataFolder`, then run it. This creates the database (SIMPLE recovery, read-committed snapshot), the Windows login and user, and grants `db_datareader` and `db_datawriter`. No DDL rights.
2. Run `02-pireports-schema.sql` against the new database. It creates schema `reports` and its tables.
3. Run `03-hangfire-schema.sql`. It creates schema `HangFire` and the scheduler tables.
4. Run `04-verify.sql` and send you the output.

All four scripts are safe to re-run.

### What you get back

- the `04-verify.sql` output: tables in `reports` and `HangFire`, migration `..._InitialCreate`, Hangfire schema version 9, and the gMSA in `db_datareader` and `db_datawriter`
- the exact server name, plus port or named instance, for the connection string
- whether connections must use a trusted certificate (`Encrypt=true` without `TrustServerCertificate=true`)

Put the connection string in that server's `appsettings.<Environment>.json` (step 6). Leave `PiReports:ApplyMigrationsOnStartup` set to `false`, which is the default in both templates.

### Each later release

Every package contains fresh copies of scripts 02 and 03, generated from that build. When a release changes the database, its notes say so. Send the DBA both scripts for each environment **before** you run `Deploy-PiReports.ps1`, because new code against an old schema fails at startup. Do Test first, then Production. Re-running a script on a database that's already current changes nothing, so when in doubt, send them anyway.

### If you'd rather the app manage its own schema

Some teams let the app create and upgrade its tables itself. To do that, the DBA grants `db_ddladmin` as well (the commented line at the end of script 01) and skips scripts 02 and 03, and you set `ApplyMigrationsOnStartup` to `true`. The DBA-run approach above is the default, because most AF SQL instances are under change control.

## 3. Server prerequisites

- **.NET 10 Hosting Bundle**, which installs the ASP.NET Core Module for IIS.
- **Microsoft Edge**, which is usually already installed. The app runs it headless and never downloads a browser.
- **IIS Windows Authentication** role service enabled.

## 4. Ports and bindings on the PI Vision server

PI Vision already owns HTTPS on this server, normally port 443 on the site it runs under. PI Reports has to share that or take its own port. Pick one of these before installing.

| | **A. Application under PI Vision's site** (default) | **B. Separate site on its own port** |
|---|---|---|
| Address | `https://pivision01.corp.local/PIReports` | `https://pivision01.corp.local:8443/` |
| Ports | none new; shares 443 | one new inbound port, e.g. 8443 |
| Certificate | reuses PI Vision's | a certificate bound to the new port (PI Vision's own is fine if its name matches) |
| Firewall | no change | inbound rule for the port, on the server and any network firewall between users and it |
| Isolation | own app pool; a PI Reports problem can't stop PI Vision, but both share the site's bindings | fully separate site and bindings |
| Script | `-HostingMode Application -SiteName '<PI Vision site>'` | `-HostingMode Site -Port 8443 -CertificateThumbprint <thumbprint> -OpenFirewall` |

**Recommendation: A.** It needs no firewall change or new certificate, and users already trust that address. Choose B if your change control won't allow a new application under PI Vision's site, or you want to stop and start the two independently in IIS.

Either way, set `PiReports:PortalBaseUrl` to the resulting address so links in emails work.

### Network flows to clear with IT

| From | To | Port | Why | New? |
|---|---|---|---|---|
| Users' browsers | PI Vision server | 443 (A) or the chosen port (B) | portal | B only |
| PI Vision server | AF SQL instance | 1433, or the instance's port (plus UDP 1434 for a named instance resolved through SQL Browser) | database | usually already open for PI Vision and AF |
| PI Vision server | SMTP relay | 25, 587 or 465 | email | probably new |
| PI Vision server | file servers | 445 (SMB) | shared-folder delivery | probably new |
| PI Vision server | itself | 443 | headless Edge opening PI Vision displays | loopback, no firewall change |
| PI Vision server | domain controllers | 88 (Kerberos), 389 (LDAP) | sign-in for the gMSA and users | already open on a domain member |
| PI Vision server | PI Web API | 443 | later, data-driven reports | not yet |

### Capturing PI Vision from the same server

Headless Edge opens PI Vision through its normal address, for example `https://pivision01.corp.local/PIVision/...`, even though that's the same machine. Use the same fully qualified name users use, so the certificate matches. Put that name in `PiReports:IntegratedAuthAllowlist`.

Windows blocks NTLM sign-in to a local site under a name that isn't the computer's own (the "loopback check"), and the capture then fails with a 401. Kerberos avoids this, so first check that PI Vision's SPN `HTTP/pivision01.corp.local` is registered on PI Vision's app pool account. If PI Vision uses NTLM, add the name to the registry value `HKLM\SYSTEM\CurrentControlSet\Control\Lsa\MSV1_0\BackConnectionHostNames` (Multi-String), then reboot. Don't use `DisableLoopbackCheck`, which turns the protection off for every name.

## 5. Install or upgrade with the script

From an elevated PowerShell, inside the unzipped package:

```powershell
# Option A: /PIReports under PI Vision's site
.\Deploy-PiReports.ps1 -Environment Test -ServiceAccount 'CORP\gmsa-pirpt-tst$' -SiteName 'Default Web Site'

# Option B: its own site on port 8443
.\Deploy-PiReports.ps1 -Environment Production -ServiceAccount 'CORP\gmsa-pirpt-prd$' `
    -HostingMode Site -Port 8443 -CertificateThumbprint 'AB12CD...' -OpenFirewall

# Regular service account instead of a gMSA: no $, and it asks for the password
.\Deploy-PiReports.ps1 -Environment Test -ServiceAccount 'CORP\svc-pirpt-tst' -SiteName 'Default Web Site'
```

Add `-WhatIf` to see what it would do first. Defaults are install root `D:\PiReports`, application `/PIReports` and app pool `PIReports`, and each has a parameter. Run the script only after the DBA has confirmed the database (step 2). In Site mode it refuses a port another IIS site already uses.

The script:

- creates `App`, `Output`, `Keys` and `Logs` (plus `TestShares` on Test) under the install root, and grants the service account rights on them
- creates or updates the app pool: No Managed Code, the gMSA identity, AlwaysRunning, idle time-out 0, user profile loaded, a daily recycle at 03:15, and `ASPNETCORE_ENVIRONMENT`
- stops the pool, copies the files, and **keeps the server's existing `appsettings.Test.json` and `appsettings.Production.json`**, so upgrades never overwrite that server's settings
- creates the `/PIReports` application (option A), or the site with its HTTPS binding, certificate and optional firewall rule (option B), with preload, Windows Authentication on and Anonymous off, then starts the pool

On the first install it reminds you to edit the settings file for that environment.

## 6. Settings per server

Edit `D:\PiReports\App\appsettings.<Environment>.json` on each server, then recycle the app pool.

| Setting | Test example | Production example |
|---|---|---|
| `ConnectionStrings:PiReports` | `Server=AFSQL-TEST;Database=PiReports_Test;Integrated Security=true;TrustServerCertificate=true` | `Server=AFSQL-PROD;Database=PiReports;...` |
| `PiReports:EnvironmentLabel` | `TEST` | blank |
| `PiReports:EmailRedirectTo` | `will@yourcompany.com` | blank |
| `PiReports:ShareRedirectRoot` | `D:\PiReports\TestShares` | blank |
| `PiReports:PortalBaseUrl` | `https://pivision-test.corp.local/PIReports` (A) or `...:8443/` (B) | `https://pivision01.corp.local/PIReports` |
| `PiReports:IntegratedAuthAllowlist` | `pivision-test.corp.local` | `pivision01.corp.local` |
| `PiReports:AdminGroup` | blank or a test group | `CORP\PI Reports Admins` |
| `PiReports:ApplyMigrationsOnStartup` | `false` | `false` |
| `PiReports:RetentionDays` | `30` | `90` |

Shared settings (the Edge path, logging) are in `appsettings.json`, which each release replaces.

SMTP is not in these files. Set it on each server's **Email settings** page, and use **Send test** to check it. On Test, the test email also goes to the redirect address.

## 7. First capture

1. **Email settings:** enter the SMTP relay and send a test email.
2. **New capture:** paste a PI Vision display URL, set the time range and browser size, then click **Save and run now**.
3. Check **Run history**. A failed run says what happened, for example a sign-in page or no access to the display.

## Moving captures from Test to Production

PI Vision display URLs name the server, so a capture exported from Test still points at the Test PI Vision server. On the import form, fill in **Change PI Vision server from / to** (for example `pivision-test.corp.local` to `pivision01.corp.local`) and every display URL is switched as it imports. Display IDs can also differ between servers if the displays were recreated rather than copied, so open each imported capture's link once before enabling it.

## Troubleshooting

- **"landed on a sign-in page" or 401:** PI Vision didn't accept the service account's Windows credentials. Check that `IntegratedAuthAllowlist` matches the host in the display URL, that PI Vision has an HTTP SPN for that host (Kerberos), and that an ordinary account in the same AD groups can open the display. On the PI Vision server itself, see "Capturing PI Vision from the same server" in step 4.
- **Startup error mentioning `Invalid object name 'reports...'` or `HangFire`:** the DBA scripts haven't been run on this database, or this release needs a newer `02-pireports-schema.sql`.
- **Startup error "Login failed for user":** the gMSA has no login on that SQL instance, or the connection string points at the wrong database.
- **The site won't start:** look in `D:\PiReports\Logs` after setting `stdoutLogEnabled="true"` in `web.config`. The most common cause is a connection string or database permission problem.
- **Display cut off:** raise the capture's browser width and height to the display's full size.
- **"kept changing until the timeout":** the display has live values or animation, so the last frame was used. Use an end time other than `*` for a fixed range, or ignore the warning.
- **Jobs not running overnight:** confirm the app pool is AlwaysRunning with idle time-out 0, and check `/jobs`.
