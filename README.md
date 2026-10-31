# PI Reports

Scheduled captures of AVEVA PI Vision displays, delivered as PDF or PNG by email and to shared folders, with a small web portal to set them up.

This is the first slice of the report system described in [docs/research/AVEVA-PI-Reporting-Research-Brief.md](docs/research/AVEVA-PI-Reporting-Research-Brief.md). Data-driven reports from PI Web API come next and will share the same scheduler, run history, and delivery.

## What it does

- **Captures:** each capture names a PI Vision display URL, a time range in PI time syntax (`*-24h` to `*`), an optional AF asset, a browser size, and PDF or PNG output.
- **Schedules:** cron per capture (`0 6 * * 1-5` = weekdays 06:00) in a chosen time zone, or on demand with **Run now**.
- **Delivery:** email through the SMTP server set on **Email settings**. Files above a per-capture size are linked from the portal instead of attached. Files are also copied to a UNC folder, written under a temp name and then renamed, so nobody opens half a file.
- **Run history:** every run is archived with its status, warnings, errors, and a download link. Old runs are pruned after `RetentionDays`.
- **Jobs dashboard:** Hangfire at `/jobs` for retries and queue state.
- **Test and Production:** one package, configured per server. A Test install redirects all email to one address, tags subjects `[TEST]`, writes shared-folder files to a local test folder, and shows a banner. **Export / import** moves captures between installations as JSON.

The service opens displays in headless Edge as its own Windows account, so PI Vision sees the service account through Kerberos or NTLM and no password is stored. A capture waits until two screenshots in a row match before saving, so half-drawn symbols aren't captured. It fails outright if it lands on a sign-in page or gets a 401. Captures run one at a time to keep load off PI Vision.

A failed capture is retried twice (after 1 and 5 minutes). A failed delivery is recorded on the run but not retried, so people who already got the email don't get it again.

## Layout

```
src/PiReports.Core    model, EF Core context and migrations, capture, delivery, jobs
src/PiReports.Web     ASP.NET Core portal (Razor Pages), Hangfire host, Windows auth
tests/PiReports.Tests unit tests, plus real-browser capture tests
deploy/               Build-Package.ps1 and Deploy-PiReports.ps1 (IIS install/upgrade)
docs/                 research brief and deployment guide
docs/dba/             request sheet and SQL scripts for the DBA
```

## Develop

Requires the .NET 10 SDK, and SQL Server LocalDB or any SQL Server (see `appsettings.Development.json`).

```powershell
dotnet tool restore
dotnet test
dotnet run --project src/PiReports.Web
```

To run the browser capture tests, point them at Edge or Chrome:

```powershell
$env:PIREPORTS_TEST_BROWSER = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
dotnet test
```

To add a database migration after changing the model:

```powershell
dotnet ef migrations add <Name> -p src/PiReports.Core -s src/PiReports.Web -o Data/Migrations
```

Deployment to Test and Production is in [docs/deployment.md](docs/deployment.md). What to send the DBA is in [docs/dba/README.md](docs/dba/README.md).
