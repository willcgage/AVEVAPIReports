# PI Reports: database request for the DBA

PI Reports schedules screen captures of PI Vision displays and emails them or saves them to shared folders. It needs one small SQL Server database per environment, on the same instance that hosts PI AF. It never reads or writes the AF database (PIFD).

## What we're asking for

| | Test | Production |
|---|---|---|
| SQL instance | *AF test instance, e.g. AFSQL-TEST* | *AF production instance, e.g. AFSQL-PROD* |
| Database name | `PiReports_Test` | `PiReports` |
| Login (Windows, gMSA) | `CORP\gmsa-pirpt-tst$` | `CORP\gmsa-pirpt-prd$` |
| Database roles for that login | `db_datareader`, `db_datawriter` | `db_datareader`, `db_datawriter` |
| Connects from | *PI Vision test server* | *PI Vision production server* |

Fill in the italic values before sending this. The accounts must already exist in AD, because script 01 creates a Windows login for them. If the domain doesn't use gMSAs yet, regular domain service accounts work just as well. Use their names without the `$`, for example `CORP\svc-pirpt-tst`.

- **Authentication:** Windows only, using the account the IIS app pool runs as (a gMSA, or a regular domain service account). No SQL logins or passwords.
- **Schemas:** the database holds two schemas, `reports` (the app's tables) and `HangFire` (its job scheduler). Nothing goes in `dbo`.
- **No DDL rights for the app.** The DBA creates and upgrades the tables with the scripts in this folder, and the app is configured not to change the schema (`ApplyMigrationsOnStartup: false`).

## Size and load

- **Size:** under 100 MB in normal use. The database holds configuration and run history only, and report files are stored on the app server's disk. Run history older than 90 days is deleted automatically. Suggested start: 256 MB data, 128 MB log, 64 MB autogrowth.
- **Load:** light but constant. The scheduler polls a few small tables every 15 seconds or so, and captures run one at a time.
- **Recovery model:** SIMPLE is fine. Everything in it can be recreated: captures can be exported to a JSON file from the portal. Use FULL if your standard requires it.
- **Backups:** a nightly full backup under your normal policy is enough.
- **Collation:** the instance default.

## Scripts, run in order

| Script | What it does | When to run it |
|---|---|---|
| `01-create-database.sql` | Creates the database, the login and user, and grants read/write. Uses SQLCMD variables at the top: turn on **Query > SQLCMD Mode** in SSMS and set the database name and account. | Once per environment |
| `02-pireports-schema.sql` | Creates the `reports` tables. Idempotent. | At install, and again with each new release |
| `03-hangfire-schema.sql` | Creates the `HangFire` scheduler tables. Idempotent. | At install, and again when a release says Hangfire was upgraded |
| `04-verify.sql` | Lists the tables, schema versions and role memberships, to confirm it worked. | After any of the above |

Each release ships updated copies of scripts 02 and 03. They are safe to re-run, because they skip anything already applied. For a release that changes the database, we'll ask you to run them on Test, then Production, **before** the new version is installed on that server.

## What we need back from you

1. Confirmation that both databases exist, and the output of `04-verify.sql` for each. Expected: tables in `reports` and `HangFire`, migration `20261027182055_InitialCreate`, Hangfire schema version 9, and the gMSA in `db_datareader` and `db_datawriter`.
2. The exact server and instance names for each connection string, including a port or named instance if it's not the default.
3. Whether connections must be encrypted with a trusted certificate. Our default connection string uses `TrustServerCertificate=true`.
