# AVEVA PI Reporting System — Options Research Brief

**Prepared:** 11 August 2026
**Scope:** On-premises PI Data Archive + PI AF + PI Vision. Deliverables: scheduled PDFs from PI Vision displays, data-driven reports, interactive web dashboards, and spreadsheet exports — distributed via web, email, and shared directories.
**Bias of this brief:** weighted toward a custom build, per direction, but grounded against commercial options so the build case is honest.

---

## 1. Executive summary

Three findings drive everything else.

**1. AVEVA has no native scheduled-report capability, and shows no sign of adding one.** As of **PI Vision 2025** (the current release — there is no 2026 version), PI Vision cannot export a PDF, cannot schedule anything, and cannot deliver anything. Browser Ctrl+P works but clips to the viewport, dropping scrollable content — which is precisely the gap the third-party market fills. The feedback-portal request "Print a display as a report" ([PIVISION-I-1420](https://pisystem.feedback.aveva.com/ideas/PIVISION-I-1420)) has sat at **597 votes since August 2022 with no AVEVA status assigned**. The PI Vision 2025 release added a Recycle Bin, regression lines, and folder permissions — nothing reporting-related. This is a real, durable gap, which is why a healthy third-party market exists around it. Building here is not reinventing a wheel AVEVA is about to ship.

**2. There are two fundamentally different architectures, and the choice is a licensing decision as much as a technical one.**

- **Screenshot architecture** — drive PI Vision in a headless browser and capture what operators already see. Pixel-faithful, zero re-authoring of visuals. But it consumes a **PI Vision named-user license** for the service account, and puts you in an unresolved position on redistributing that output to unlicensed recipients.
- **Data architecture** — pull from PI Web API (or AF SDK) and render your own reports. Deterministic, testable, format-flexible, and lands under **PI System Access (PSA)** licensing, which is the model built for programmatic access. Cost: you re-author the visuals, and reports won't be pixel-identical to the displays.

Notably, a commercial integrator (IOTA Software) chose to *import PI Vision display definitions via API and re-render them in their own engine* rather than screenshot — a meaningful vote for the data architecture.

**3. The hard part of this project is not getting data out of PI.** Data retrieval is a well-trodden REST/SDK problem, solvable in a week. What kills custom reporting builds is the second half: paginated PDF rendering quality, a scheduler with real retry/catch-up/state semantics, delivery tracking, and — if you are in a regulated plant — approval workflow and audit trail. Software Athlete, a dedicated team on a single product, only shipped approval workflow and missed-run recovery in **v1.13, July 2026**. That is your realistic timeline signal for the tail end.

**Recommendation:** build a **data-first system** with **selective screenshot capability** as a secondary renderer. Phase it so the licensing questions get answered before they can strand you.

---

## 2. Before writing any code: three questions for AVEVA

These are commercial questions with technical consequences. Get answers in writing.

| # | Question | Why it matters |
|---|---|---|
| **Q1** | Does our current entitlement include **PI System Access (PSA)**? | PSA is required for intermediary non-AVEVA programs that programmatically access PI Server data ([PI System Software Schedule v1.5, June 2026](https://www.aveva.com/content/dam/aveva/documents/legal/policies/AVEVA-Software-Schedule-PI-System-v1-5-June-2026.pdf), §3.3(b)(iv) — note the Schedule defines PSA generically and does *not* enumerate interfaces). Your custom service is exactly that. PSA is priced **per PI Data Archive server, scaled by server size**. **This is the most likely hidden cost in the build column.** Good news: per AVEVA's developer-technologies material, PSA covers PI Web API, AF SDK, PI SQL Client, and PI OLEDB Enterprise equally — so the technology choice among them is pure engineering, not licensing. |
| **Q2** | If a service account drives PI Vision headlessly, and the resulting PDFs go to 200 people who don't hold PI Vision licenses, is that permitted? | PI Vision is **named-user** licensed ("one person… and their associated login account"), reassignable no sooner than every **90 days**, with **no concurrent/pooled option**. The Software Schedule contains no explicit multiplexing clause either way. This is the single highest-risk unknown in the project. |
| **Q3** | What is the current version and support lifecycle of **PI Web API**, and is a 2024/2025/2026 release available? | Latest publicly evidenced release is **2023 SP1 Patch 1 (1.19.1.28)**, tied to security bulletin AVEVA-2025-003 / [CISA ICSA-25-162-08](https://www.cisa.gov/news-events/ics-advisories/icsa-25-162-08). PI Web API ships on its own cadence, separate from PI Server. Confirm on the Customer Portal before pinning a version. |

If Q2 comes back restrictive, the screenshot path is dead for broad distribution and the data architecture becomes mandatory — which is another reason to build data-first and treat screenshots as an add-on.

---

## 3. Data access layer — four options, one clear default

All four are covered by the **same PSA license**, so choose on merit.

| Option | Platform | Throughput | Best for |
|---|---|---|---|
| **AF SDK (.NET)** | Windows only, needs PI AF Client | **~200k values/sec** uncached | Highest performance; richest API surface (bulk `AFAttributeList` ops, data pipes, full AFSearch). AVEVA: "best performance of all PI Development Technologies." |
| **PI Web API (REST/JSON)** | Anything | **~50k values/sec** — roughly **4× slower** than AF SDK | Cross-platform, containers, one HTTPS endpoint through network segmentation, and it can serve both your service *and* a browser front end. It is a wrapper over AF SDK — it can never exceed AF SDK's capability. |
| **PI SQL Client (RTQP Engine)** | Windows + Linux (JDBC) | — | The **underrated option**. Server component on the AF Server, thin ODBC/OLEDB/JDBC clients. AVEVA calls it "the recommended choice when reading from the PI System using SQL technologies." Wins hard when you need to join PI data against LIMS/ERP/SQL, or let analysts write SQL. Supersedes the deprecated PI OLEDB Enterprise. |
| **PI Integrator for Business Analytics** | Windows services | — | Configure-not-code ETL into a warehouse for BI tools. Wrong tool for bespoke report logic. Release cadence looks slow (2020 R2 SP2 current per [CISA ICSA-25-224-04](https://www.cisa.gov/news-events/ics-advisories/icsa-25-224-04)); confirm roadmap before committing. |

**Default: PI Web API**, because you want a web portal and a service reading the same layer, and because it keeps the door open to non-Windows hosting. Reach for AF SDK if profiling shows the 4× tax actually hurts, and for PI SQL Client the moment a report needs non-PI data joined in.

### Endpoints that matter for reporting

```
GET /streamsets/summary?webId=…&webId=…&summaryType=Average&summaryType=Maximum
      &summaryDuration=1h&calculationBasis=TimeWeighted
GET /streamsets/plot?webId=…&intervals=640          ← for every chart, always
GET /streamsets/interpolated?webId=…&interval=1h
GET /streamsets/{elementWebId}/recorded?searchFullHierarchy=true
GET /assetdatabases/{webId}/eventframes?searchMode=Inclusive&templateName=Batch
POST /batch                                          ← chained multi-step requests
```

Six practices that separate a fast system from a slow one:

1. **`selectedFields` on every call** (`?selectedFields=Items.Timestamp;Items.Value;Items.Good`). JSON serialization dominates cost; this is the highest-leverage single change.
2. **Aggregate server-side.** `/streamsets/summary` with `summaryDuration` moves rollups into AF SDK and returns orders of magnitude less data than pulling raw and aggregating in pandas.
3. **`/plot` with `intervals` ≈ chart pixel width** for charts. It returns per-interval min/max/first/last, preserving spikes at bounded point count. Never pull `recorded` and downsample yourself.
4. **Watch the silent truncation.** `maxCount` defaults to **1000** and returns no error when it clips. Server ceiling `MaxReturnedItemsPerCall` defaults to **150,000** — and **for StreamSet calls it is divided by the number of streams** (150 streams → ~1000 values each). Always set `maxCount` explicitly and check whether you got exactly your limit back.
5. **Concurrency ≤ 4.** AVEVA staff guidance: `MaxDegreeOfParallelism` 3–4, because parallel requests contend for a limited pool of Data Archive archive threads (default 8).
6. **Prefer AF Analyses writing to PI points over `/calculation/*`** for recurring KPIs — computed once, archived, cheap to read forever. Reserve the Calculation controller for genuinely ad-hoc user formulas.

Two more things worth knowing: **Event Frames are the right primitive** for batch/downtime/excursion reporting — don't reconstruct episodes from raw series. And **every value carries `Good`/`Questionable`/`Substituted` flags, with digital states arriving as objects rather than numbers** — this is the #1 source of type errors in naive PI code.

### Authentication

Kerberos, running your service as a domain account (ideally a **gMSA**). Critically: have PI Web API's own service identity hold the PI/AF read rights rather than impersonating callers — that makes it a **single hop** and you sidestep constrained-delegation entirely. Register SPNs for both short and FQDN names, use **DNS A records not CNAMEs**, and check `setspn -X` for duplicates (the classic silent NTLM-fallback cause). Send `X-Requested-With` on every POST/PUT/PATCH/DELETE or PI Web API rejects it — this bites `/batch` even in read-only systems.

If you land on PI Server 2023+, OIDC client-credentials is available and is the better answer for off-domain or containerized hosting.

**Python client note:** the generated Swagger clients are effectively abandoned — `osisoft/PI-Web-API-Client-Python` now 404s. AVEVA's own current sample uses plain HTTP. Use `httpx`/`requests` with **`pyspnego`** (0.12.1, actively maintained) — *not* `requests-negotiate-sspi`, which last shipped in **2018**. On Windows, `PIconnect` (AF SDK via pythonnet, ~104 stars, actively maintained) is a serious alternative that gets you AF SDK performance from Python.

---

## 4. PI Vision capture — feasible, but know what you're signing up for

### What works

Kiosk-mode URLs give you clean, chrome-free displays with parameterized time and asset context:

```
https://pivision.corp/PIVision/#/Displays/412/UnitReport
    ?Asset=\\AFSRV01\NuGreen\NuGreen\Houston\Boiler1
    &StartTime=01-Aug-2026&EndTime=02-Aug-2026
    &mode=kiosk&HideToolbar&HideTimebar&HideSidebar
```

`mode=kiosk` is documented; `HideToolbar`/`HideTimebar`/`HideSidebar` are **valueless flags**; `Asset=` for asset swapping is in AVEVA's own training material. Parameter names are case-insensitive. There is **no `fullscreen` parameter** — kiosk plus the Hide flags is as close as it gets.

Symbols render as **HTML/CSS/AngularJS, not canvas**, so `page.pdf()` output is vector-crisp.

### What will cost you time

- **Kerberos from headless Chromium is the documented pain point.** `httpCredentials` is Basic-auth machinery and does not reliably drive NTLM/Negotiate ([Playwright #32360](https://github.com/microsoft/playwright/issues/32360), [Puppeteer #11626](https://github.com/puppeteer/puppeteer/issues/11626)). The `--auth-server-allowlist` flags only *permit* Chromium to answer a Negotiate challenge — they don't obtain a ticket. A documented real-world attempt in a Linux container with correct flags **still hung on a blank page** ([robotframework-browser #3944](https://github.com/MarketSquare/robotframework-browser/issues/3944)). **Mitigation: run the capture on Windows as the service account**, where SSPI handles it transparently. Do not attempt containers first.
- **I found zero published end-to-end write-ups of Playwright driving PI Vision.** You are on an untrodden path. Budget accordingly.
- **Never use `networkidle`.** PI Vision polls PI Web API continuously; the network never idles. Wait on `window.PIVisualization`, then on symbols leaving their loading/error state, then **screenshot twice ~2s apart and compare buffers** — accept only when identical. This defeats the "trend drew axes but not the series" failure that produces silently-wrong reports.
- **Use absolute `StartTime`/`EndTime`, not `*`-relative**, so the display converges to a fixed state.
- **`printBackground: true` and `emulateMedia({media:'screen'})`** are mandatory or PI Vision's dark theme renders white-on-white.
- **Parameters live after the `#`.** Changing only the query fragment will **not** reload the SPA — force `page.reload()` or use a fresh context per capture when looping over assets.
- **Timeouts in minutes, not seconds.** A community report documents **5–10 minute** PI Vision load times caused by on-demand AF Analyses, at only 15% CPU — latency, not saturation. Your automated capture is indistinguishable from a user with a long time range, which is exactly the pathological case. Serialize captures, schedule off-peak.
- **Detect the login page.** Expired sessions silently screenshot a login form. This is the classic scheduled-capture failure mode.

### What does not exist

PI Vision's REST API is a **display-definition CRUD surface** — import/export display JSON, added in PI Vision 2021. There is **no screenshot, render, or PDF endpoint anywhere** in the API. The PI Vision Display Utility manages display lifecycle and **renders nothing**. Export-to-Excel exists but is click-driven, was called "early phase" by OSIsoft staff, and exports CSV into a single column. **Nothing in PI Vision produces a rendered image without a browser.**

---

## 5. Implementation stack

Two coherent stacks. The .NET one has a real structural advantage; the Python one has better data-tooling ergonomics.

### Python-centric

```
Definition   YAML/JSON spec → Jinja2 → HTML + CSS Paged Media
Data         httpx + pyspnego (Kerberos) → PI Web API /batch, /streamsets
Charts       matplotlib → SVG (svg.fonttype='none'), inlined as data URLs
PDF          WeasyPrint 69          ← running headers, TOC page numbers, bookmarks, PDF/UA
XLSX         XlsxWriter 3.2.9
Scheduling   APScheduler 3.11 + your own report_run state table
Host         WinSW/NSSM wrapping the venv, running as a gMSA
Portal       FastAPI behind IIS via HttpPlatformHandler, forwardWindowsAuthToken
```

### .NET-centric

```
Definition   Typed C# model → Razor → HTML
Data         HttpClient { UseDefaultCredentials = true } → PI Web API  (Kerberos "just works")
Charts       ScottPlot 5 (MIT) → high-DPI PNG
PDF          PuppeteerSharp (MIT) headless Chromium, pdf(outline:true, tagged:true)
XLSX         ClosedXML 0.105.1 (MIT)
Scheduling   Hangfire Core + SQL Server storage  ← retries, persisted state machine, dashboard
Host         .NET Worker Service running as a gMSA
Portal       ASP.NET Core on IIS in-process, Negotiate auth, Hangfire dashboard at /jobs
```

**The .NET stack's structural win:** one process serves the portal, hosts the scheduler with its dashboard, and runs the jobs. One deployment unit, one gMSA, no IPC, no auth shim. On the Python side, Windows Integrated Auth on the portal means a hand-rolled `X-IIS-WindowsAuthToken` → pywin32 SID-resolution shim, with per-request native calls and a handle you must remember to close. That shim is the strongest single argument for putting *at least the portal* in ASP.NET Core even on a Python team.

### Licensing landmines in the stack — verified, and worth reading twice

| Package | Status |
|---|---|
| **QuestPDF** | **License changed 2026-07-06 to v3.0** — source-available commercial, *not* OSI open-source. Community tier **excludes public-sector entities and publicly traded companies at any revenue**. Paid: Professional $1,999+, Enterprise $4,999+. If your employer is publicly traded, Community is unavailable. **Avoid; PuppeteerSharp is MIT and equivalent.** |
| **iText** | AGPLv3 or commercial. An internal web portal **is** a network deployment — AGPL would require disclosing your source. Assume you need a commercial license. **Avoid.** |
| **EPPlus** | Polyform Noncommercial or commercial. An operating company's internal reporting system **is commercial use**. $329–$569/dev/yr. **Use ClosedXML (MIT) instead** unless you need EPPlus's chart/pivot depth. |
| **Hangfire Core** | **LGPLv3; commercial use is free.** Caveat: LGPLv3 permits proprietary *use and linking*, not proprietary *forks* — distributing a modified/private fork requires a paid commercial subscription. Fine as long as you consume it as a NuGet package. |
| **wkhtmltopdf** | **Repository archived January 2023.** Last release 2020, ~2015-era WebKit CSS, unpatched. **Do not start here.** |
| Playwright, PuppeteerSharp, WeasyPrint, matplotlib, XlsxWriter, ClosedXML, ScottPlot, Quartz.NET, Prefect, Dagster | Apache-2.0 / MIT / BSD. Clean. |

### PDF engine: the one nuanced call

Chromium 131+ handles `@page` margin boxes and page counters natively, but **no browser implements `string-set()`/`string()` or `target-counter()`** — meaning **content-derived running headers** ("Unit 3 — Boiler Feedwater" repeated at the top of each page) and **a TOC with real page numbers** are not achievable in headless Chromium alone. WeasyPrint supports both, plus automatic PDF bookmarks from `<h1>`–`<h6>` and PDF/A + PDF/UA output.

- **Need running headers and a paginated TOC** → **WeasyPrint**, and script the MSYS2/Pango install into deployment once. (This install is a real change-control conversation on a hardened OT server — plan for it.)
- **Simple "Title / Page N of M" headers** → **Playwright or PuppeteerSharp**, with [Paged.js](https://pagedjs.org/) as the escape hatch if you later need `target-counter`.

Two headless-Chromium gotchas worth pre-empting: header/footer templates render in an isolated context with a ~5pt default font and no inherited stylesheet (the most-reported "my footer is invisible" bug), and headless Chrome **silently refuses to fetch `url()` resources from `@page` rules** — logos in margin boxes must be base64 data URLs.

**Charts:** render to **SVG and inline them**. Both WeasyPrint and Chromium preserve SVG as vector. matplotlib is the winner for print (true vector PDF/SVG, trivially headless, no browser). Avoid Plotly+kaleido here — kaleido v1 no longer bundles Chromium and WebGL traces export as embedded rasters.

### Delivery

**Email.** Do not build on SMTP + username/password. Microsoft's SMTP AUTH basic-auth retirement was **delayed, not cancelled**: usable until **end of December 2026**, after which it is blocked by default — **new tenants created from January 2027 cannot use it at all**, while existing tenants keep an admin-reversible block. Final removal date to be announced H2 2027. Options in preference order:

1. **Internal on-prem SMTP relay** with IP-based anonymous relay — lowest friction, sidesteps the clock entirely. If your plant already relays HMI/alarm email, use it. **Start here.**
2. **Microsoft Graph `sendMail`** with client-credentials OAuth. **Essential hardening:** the `Mail.Send` application permission is tenant-wide (send as *any* mailbox) and security review will reject it — scope it with **RBAC for Applications** (`New-ManagementScope` with a recipient filter). Application Access Policies are now labelled legacy.
3. **High Volume Email (HVE)** — now GA, purpose-built for line-of-business messaging, $0.000042/recipient. But: **internal recipients only**, **10 MB message cap**, 50 recipients/message.

**Attach vs. link — the clearest call in this brief.** Graph attachments over 3 MB require a three-step `createUploadSession` protocol in ≤4 MB chunks with resumption logic you own. HVE caps at 10 MB. And a 40 MB daily PDF to 60 people is 2.4 GB/day of duplicated mailbox storage after journaling. **Rule: attach under ~5 MB; above that, write to the share/portal and email a link plus an inline HTML summary** (KPIs, one small PNG chart, exceptions table). Make the threshold per-report configurable, with an "always attach" override for regulatory reports that must be self-contained in the mail record.

**File shares.** Run the service under a **gMSA** — automatic 30-day password rotation, no password in config, no annual "the service account expired" outage. This is the highest-value ops decision in the whole design. Grant share/NTFS rights to a **domain group containing the gMSA**, not the gMSA directly — many NAS and storage-appliance object pickers won't enumerate `msDS-GroupManagedServiceAccount` objects. Write to a temp name and atomically rename so consumers never see a half-written PDF. Use UNC paths; mapped drives are per-logon-session and don't exist for a service.

**Portal.** ASP.NET Core on IIS in-process with Negotiate gives transparent SSO and AD-group authorization. A genuinely simpler alternative if the portal is only "browse and download": serve the artifact directory from **IIS with Windows Auth + NTFS ACLs** and no app code at all. Worth considering for Phase 1.

---

## 6. Build vs. buy — the honest comparison

### What buying gets you, ranked by what it would cost you to build

1. **Paginated rendering fidelity.** Page breaks that don't split tables, repeating headers, TOC, vector trends, print-safe color. Headless Chrome gets you 70%, then you spend months on the last 30%. **The most underestimated line item.**
2. **A report designer non-developers can use.** Dream Report and AVEVA RfO sell almost entirely on no-code drag-and-drop. Custom means every new report is a ticket to your team, forever. If the site wants 40 report variants and 3 changes a month, the designer *is* the product. (Vision Report+ sidesteps this cleverly — PI Vision itself is the designer.)
3. **Approval workflow, e-signature, audit trail.** If you're in a GxP plant, building this yourself means owning the **validation burden** too — IQ/OQ/PQ, change control, defending your own code in an audit. **If 21 CFR Part 11 is in scope, this alone decides it.**
4. **Execution reliability semantics.** Queued/Processing/Failed state machine, retry, **missed-run detection and regeneration after an outage**, HA clustering, telemetry.
5. **Delivery tracking.** Per-recipient status. "Did the 6am shift report actually go out?" is the question that gets a homegrown build shut down.
6. **Upgrade compatibility.** PI Vision and PI Server change between releases; a vendor absorbs that. Your build inherits it as unplanned work every cycle.

### The market

| Product | What it is | Fit |
|---|---|---|
| **Vision Report+** (Software Athlete) | PI Vision extension → scheduled PDFs. Vector rendering, not screenshots. Email/printer/network folder/Teams delivery. **Event Frame triggering.** Inherits PI Vision/AF/AD security. v1.12 added HA clustering; **v1.13 (Jul 2026) added approval workflow, queued/processing states, and missed-run recovery.** Quote-based subscription, 30-day trial. | **The closest thing to a drop-in** if your target is "our existing PI Vision displays, as scheduled PDFs." That v1.13 list is a checklist of the tail-end work you'd otherwise spend a year on. **Limitation: the display *is* the report** — no free-form banded designer, no arbitrary tabular layout, no joining PI to LIMS/ERP, no Excel/Word output. |
| **Dream Report** (Ocean Data Systems) | **Verified: ODS is independent — AVEVA did not acquire them.** Ships "Dream Report 2026." No-code designer, runtime engine, 140+ drivers, web portal, built-in data logger. **Version management, audit trail, e-signature blocks, gated approval-before-release** — purpose-built for GxP. | **Buy this if you're regulated.** Limitation: PI is one of 140 drivers, not first-class — expect tag-level access with limited AF/Event Frame awareness. Security note: [CISA ICSA-24-226-08](https://www.cisa.gov/news-events/ics-advisories/icsa-24-226-08) (two CVSS 8.5 issues, patched in 2023 R2). |
| **AVEVA Reports for Operations** | **It is Dream Report, AVEVA-branded** — AVEVA's own announcement says it "uses 100% of the functionality of Dream Report." | **Roadmap caution:** most recent public "what's new" is **2023 R2**; no 2024/2025/2026 release found, while ODS independently shipped "Dream Report 2026." The AVEVA build may be trailing. Also telling: the current product page's connectivity list **doesn't mention PI System at all**. Confirm version and lifecycle with AVEVA before betting on it. |
| **PI DataLink + scheduled Excel** | The incumbent. | **The thing your build must beat, and beating it is easy.** Licensed per *individual user*, max 2 PCs, and *"cannot be used by multiple people at the same time"* — a licensing problem for server-side execution, not just a technical one. Running Excel unattended in a non-interactive session is **not supported by Microsoft**; AVEVA community guidance is blunt: "do not expect anything to run smoothly every time for years." Zero retry, zero delivery tracking, zero audit. ("PI DataLink Server" — **I could not verify this exists as a current product.** Don't budget for it unsighted.) |
| **Power BI** | Three routes: PI Integrator for BA (sanctioned, but **licensed by "actively published data streams"** — scales with data breadth, not report count, which is punitive for wide-but-shallow needs); PI Web API → Power Query (fragile beyond a PoC); or your own pipeline → SQL → Power BI (which is just your custom build with Power BI as the front end). | **Dashboards, not documents.** If the deliverable is a signed 8-page PDF, this is the wrong end of the tool. Reasonable as a *complement* to a custom build for the interactive-dashboard requirement. |
| **SSRS on PI OLEDB Enterprise** | The classic "supported and stable" answer. Free with SQL Server: designer, parameters, scheduled subscriptions, email/file-share delivery, execution logging, RBAC. | **The most interesting hybrid** — hands you items 1, 2, 4, and 5 from the "what buying gets you" list for free, and you write only the queries. **But it's a dead-end lineage:** the how-to ecosystem is 2010–2014 vintage, and **Power BI Report Builder does not support the OLE DB connector for PI**, so there's no migration path forward. |
| **Seeq, dataPARC, XLReporter, Grafana** | Adjacent. Seeq Organizer does scheduled emailed PDFs on a native PI/AF connector but is enterprise-priced and overkill for documents alone. XLReporter is mature but its AVEVA connector is **tag-level only — no AF, no PI Web API**, disqualifying if your reporting is model-driven. Grafana + the free GridProtectionAlliance PI Web API datasource is a genuinely useful *component* of a custom build for the dashboard requirement. | Grafana is worth a look for the interactive-dashboard deliverable specifically. |

### Where building genuinely wins

Joining PI with non-PI data (LIMS, ERP, SQL, CSV drops) in the same report; bespoke calculations beyond AF Analyses; arbitrary output formats; git-versioned report definitions with CI; and **no per-seat, per-stream, or per-Data-Archive metering as report count grows**.

### The decision, stated sharply

- **Regulated (GxP / Part 11) → buy Dream Report.** Validation and e-signature burden dominates everything else.
- **"Make our existing PI Vision displays into scheduled PDFs," full stop → trial Vision Report+.** Test HA and missed-run recovery specifically. It is a narrow, well-fit tool.
- **Free-form reports, PI joined with other systems, no compliance mandate, and you have engineering capacity → build.** Which is where this project sits.

---

## 7. Recommended architecture

**Data-first, with screenshots as a secondary renderer.** One service, four output paths, one scheduler.

```
                    ┌──────────────────────────────────────┐
                    │   Report Definitions (YAML in git)   │
                    │   source · schedule · layout ·       │
                    │   recipients · format · thresholds   │
                    └───────────────────┬──────────────────┘
                                        │
        ┌───────────────────────────────▼───────────────────────────────┐
        │            Scheduler + run-state machine                      │
        │   queued → running → succeeded | failed(+attempt)             │
        │   scheduled_for vs started_at → real catch-up backfill        │
        └───────────────────────────────┬───────────────────────────────┘
                                        │
              ┌─────────────────────────┴─────────────────────────┐
              │                                                   │
    ┌─────────▼──────────┐                          ┌─────────────▼──────────┐
    │  PI data pipeline  │                          │  PI Vision capture     │
    │  PI Web API        │                          │  headless Chromium     │
    │  streamsets/summary│                          │  kiosk URLs            │
    │  /plot · /batch    │                          │  (Phase 3 — gated on   │
    │  Event Frames      │                          │   the license answer)  │
    └─────────┬──────────┘                          └─────────────┬──────────┘
              │                                                   │
    ┌─────────▼───────────────────────────────────────────────────▼──────────┐
    │                          Render layer                                   │
    │   HTML+CSS → PDF   ·   XLSX   ·   HTML dashboard   ·   captured PNG/PDF │
    └─────────┬───────────────────────────────────────────────────────────────┘
              │
    ┌─────────▼────────────┬──────────────────────┬────────────────────────┐
    │  Email               │  SMB share           │  Web portal            │
    │  relay or Graph      │  gMSA, atomic rename │  IIS + Windows Auth    │
    │  link above ~5 MB    │                      │  browse · download ·   │
    │                      │                      │  on-demand re-run      │
    └──────────────────────┴──────────────────────┴────────────────────────┘
```

### Why this shape

- **Report definitions as version-controlled YAML** gives you git history, code review, and CI on report changes — one of the few places a custom build beats every commercial tool outright.
- **One scheduler, one state table** means retry, catch-up, and delivery tracking are built once and inherited by every output path. Build this before the third report exists, not after the thirtieth.
- **Screenshots are a renderer, not the architecture.** If Q2 comes back restrictive, you lose one renderer, not the system.
- **The portal is also the large-attachment answer** — it's what the email links point to.

### Phasing

| Phase | Scope | Gate |
|---|---|---|
| **0 — Licensing** | Answers to Q1–Q3 in writing. Confirm installed PI Web API version. | Blocks everything downstream. Start today. |
| **1 — Thin vertical slice** | One real report, end to end: PI Web API → HTML → PDF → email + share. Hard-coded schedule, no portal. | Proves auth, data access, and PDF quality. **Do not skip to breadth.** |
| **2 — Platform** | YAML definitions, scheduler + state machine with retry and catch-up, run history, XLSX output, the portal with Windows Auth. | This is where the system becomes maintainable rather than a script. |
| **3 — PI Vision capture** | Headless capture on **Windows as the service account** (not a container). Screenshot-stability check and login-page detection built in from day one. | **Gated on Q2.** Also: price Vision Report+ against remaining build cost at this point — if capture is most of what you need, buying may win here even if building won Phase 1–2. |
| **4 — Dashboards** | Interactive web dashboards. Evaluate Grafana + the free PI Web API datasource against building your own before writing code. | Grafana may be the cheapest correct answer for this specific deliverable. |

### Language call

**If your team is comfortable in .NET, build in .NET.** Three concrete reasons, not preference: `HttpClient { UseDefaultCredentials = true }` makes Kerberos against PI Web API a non-event where Python needs `pyspnego` care; Hangfire gives you retry, a persisted state machine, and a dashboard as a NuGet package plus SQL Server tables; and one ASP.NET Core process can host the portal, the scheduler dashboard, and the jobs as a single deployment unit under one gMSA. **If the team is Python-first**, the stack in §5 works — accept the `HttpPlatformHandler` auth shim on the portal, or host just the portal in ASP.NET Core.

---

## 8. Risk register

| Risk | Severity | Mitigation |
|---|---|---|
| PSA licensing turns out to be an unbudgeted cost | **High** | Q1, before any code |
| PI Vision named-user licensing blocks broad distribution of captured output | **High** | Q2, before Phase 3. Data-first architecture means this doesn't strand the project |
| Kerberos from headless Chromium consumes a week+ | Medium | Run on Windows as the service account first. Containers only after that works |
| Silently-wrong reports (partial render, stale data, login page captured) | **High** — worse than a failed run | Screenshot-stability comparison; login-page detection; console-error capture; `Good`-flag assertions on data |
| Automated capture degrades PI Vision for interactive users | Medium | Serialize captures, cap concurrency, schedule off-peak, prefer AF Analyses writing to PI points over on-demand evaluation |
| PDF pagination quality falls short of expectations | Medium | Pick the engine against a *real* report in Phase 1, not a hello-world. Test repeating table headers against actual markup |
| SMTP basic auth disabled end of 2026 | Medium | Use an internal relay or Graph from day one |
| Custom build becomes a bottleneck (every report is a ticket) | Medium | YAML definitions + a template library. Revisit buy if variant count exceeds ~20 |
| Report definitions accumulate faster than the scheduler matures | Medium | Build the state machine in Phase 2, before report count grows |

---

## 9. Verification gaps

Flagging these explicitly rather than presenting them as settled. `docs.aveva.com` renders its body via JavaScript and could not be read directly; AVEVA doc *titles and URLs* were confirmed via search index, but parameter text and technical detail came from AVEVA/OSIsoft training PDFs, the AVEVA legal Software Schedule, CISA advisories, GitHub, and PI Square community threads.

- **PI Web API system requirements** (OS, .NET, exact AF Client version) — get from your install kit release notes.
- **Whether a PI Web API 2024/2025/2026 release exists** — latest evidenced is 2023 SP1 Patch 1 (June 2025). Confirm on the Customer Portal.
- **PI Vision session/idle timeout behavior** — no authoritative source found; assume re-auth per run.
- **`RateLimitMaxRequests` / `RateLimitDuration` defaults** and max sub-requests per `/batch` call — attribute names confirmed by AVEVA staff, defaults not stated.
- **Indexed Search / Crawler current support status** — only an ~8-year-old community thread found. Default to AFSearch query endpoints instead.
- **AVEVA Reports for Operations current version and lifecycle** — no 2024+ release found.
- **Whether ODS ships a formal IQ/OQ validation package** for Dream Report — their Part 11 page doesn't say. Ask if regulated.
- **Which QuestPDF package version first shipped under License v3.0** — release notes say only "substantially revised the legal documents." Pin this with counsel if you take the dependency.
- **Chromium's current defect status for repeating `<thead>` borders across page breaks** — issue tracker requires sign-in. Test against your real markup.

**Verification pass:** the 13 load-bearing claims in this brief (licensing terms, API limits, product status, deprecation timelines) were independently fact-checked against primary sources; all 13 verified, with four wordings tightened as a result. The remaining gaps above are the genuine unknowns.

---

## Sources

**AVEVA primary / legal**
[PI System Software Schedule v1.5, June 2026](https://www.aveva.com/content/dam/aveva/documents/legal/policies/AVEVA-Software-Schedule-PI-System-v1-5-June-2026.pdf) · [PI Vision URL parameters (1009424)](https://docs.aveva.com/bundle/pi-vision/page/1009424.html) · [Displays in kiosk mode (1009428)](https://docs.aveva.com/bundle/pi-vision/page/1009428.html) · [PI Vision Display Utility (1009356)](https://docs.aveva.com/bundle/pi-vision/page/1009356.html) · [PI Vision API Reference](https://docs.aveva.com/bundle/pi-vision-api-reference/page/overview.html) · [PI Vision 2025 release notes](https://docs.aveva.com/bundle/pi-vision/page/1564792.html) · [PI SQL Data Access Server (RTQP)](https://docs.aveva.com/bundle/pi-sql-data-access-server-rtqp-engine/page/1016129.html) · [PI Integrator for BA](https://www.aveva.com/en/products/aveva-pi-integrator-for-business-analytics/) · [PI DataLink](https://www.aveva.com/en/products/aveva-pi-datalink/) · [AVEVA Reports for Operations](https://www.aveva.com/en/products/reports-for-operations/) · [AVEVA announcement: RfO built on Dream Report](https://www.aveva.com/en/perspectives/blog/aveva-introduces-an-operations-control-reporting-software/) · [AVEVA cyber security updates](https://www.aveva.com/en/support-and-success/cyber-security-updates/)

**AVEVA/OSIsoft training material**
[PI World 2020: Using PI Web API From Beginner to Advanced](https://osicdn.blob.core.windows.net/learningcontent/PI%20World%202020%20and%20beyond/PIWorld%202020%20Using%20PI%20Web%20API%20From%20Beginner%20to%20Advanced.pdf) · [Intro to PI System Developer Technologies](https://osicdn.blob.core.windows.net/learningcontent/PI%20World%202020%20and%20beyond/Intro%20to%20PI%20System%20Dev%20Tech%20-%20workbook.pdf) · [Configuring OpenID Connect across the AVEVA PI System](https://osicdn.blob.core.windows.net/learningcontent/Online%20Course%20Workbooks/Configuring%20Modern%20Claims-based%20Authentication%20with%20OpenID%20Connect%20across%20the%20AVEVA%20PI%20System%20Workbook.pdf) · [PI World 2019: PI Vision Beyond the Basics](https://osicdn.blob.core.windows.net/learningcontent/pdfs/2019%20PI%20World%20PI%20Vision%20-%20Beyond%20the%20Basics.pdf) · [PI World 2020: PI Vision Basics](https://osicdn.blob.core.windows.net/learningcontent/PI%20World%202020%20and%20beyond/PIWorld%202020%20PI%20Vision%20Basics.pdf) · [PI Vision Extensibility Guide](https://github.com/osisoft/OSI-Samples-PI-System/blob/master/docs/PI-Vision-Extensibility-Docs/PI%20Vision%20Extensibility%20Guide.md)

**Community / feedback portal**
[PIVISION-I-1420: Print a display as a report](https://pisystem.feedback.aveva.com/ideas/PIVISION-I-1420) · [PIVISION-I-1395: Excel export](https://pisystem.feedback.aveva.com/ideas/PIVISION-I-1395) · [PI Square: high-throughput reading](https://community.aveva.com/pi-square-community/f/forum/84494/best-practice-for-high-performance-throughput-reading) · [PI Square: max allowed results](https://community.aveva.com/pi-square-community/f/forum/96416/pi-web-api-configuration-of-maximum-allowed-results) · [PI Square: rate limit for batch](https://community.aveva.com/pi-square-community/f/forum/95934/ratelimit-for-pi-web-api-batch) · [PI Square: automate PI DataLink](https://community.aveva.com/pi-square-community/f/forum/95286/automate-pi-datalink) · [PI Square: PI Vision slow 5–10 minutes](https://community.aveva.com/pi-square-community/f/forum/98621/pi-vision-are-slow-5-10-minutes) · [PI Square: Excel export](https://community.aveva.com/pi-square-community/f/forum/91592/way-to-export-pi-vision-tabular-data-to-excel-csv) · [PI Square: print entire display to PDF](https://community.aveva.com/pi-square-community/f/forum/95873/how-do-i-print-the-entire-pi-vision-display-to-pdf-browser-print-functionality-is-limiting-the-print-to-what-is-shown-on-the-screen-scrollable-content-is-not-included-in-the-pdf) · [PI Square: collection URL filter](https://community.aveva.com/pi-square-community/f/forum/98848/how-to-filter-collection-via-pi-vision-url-parameter)

**Security advisories**
[CISA ICSA-25-162-08 (PI Web API)](https://www.cisa.gov/news-events/ics-advisories/icsa-25-162-08) · [CISA ICSA-24-163-02](https://www.cisa.gov/news-events/ics-advisories/icsa-24-163-02) · [CISA ICSA-25-224-04 (PI Integrator)](https://www.cisa.gov/news-events/ics-advisories/icsa-25-224-04) · [CISA ICSA-24-226-08 (Dream Report)](https://www.cisa.gov/news-events/ics-advisories/icsa-24-226-08)

**Commercial products**
[Vision Report+](https://www.software-athlete.com/pages/vision-report-plus-automated-pdf-reporting-demo-video) · [Vision Report+ v1.13 changelog](https://www.software-athlete.com/blogs/changelog/vision-report-v1-13) · [v1.12 changelog](https://www.software-athlete.com/blogs/changelog/vision-report-v1-12) · [What's new in PI Vision 2025 and what's still missing](https://www.software-athlete.com/blogs/use-cases/what-s-new-in-pi-vision-2025-and-whats-still-missing) · [Configure Kerberos for PI Web API](https://www.software-athlete.com/blogs/resources/how-to-configure-kerberos-for-pi-web-api) · [PI Vision kiosk mode guide](https://www.software-athlete.com/blogs/resources/enable-kiosk-mode-for-displays-and-hide-pi-vision-toolbars) · [Dream Report](https://dreamreport.net/) · [Dream Report 21 CFR Part 11](https://dreamreport.net/blog/report-generation-for-21-cfr-part-11-21cfrpart11/) · [Dream Report OEM partners](https://dreamreport.net/blog/5-of-top-10-automation-companies-and-ocean-data-systems/) · [Seeq Organizer](https://www.seeq.com/product/organizer/) · [SmartSights XLReporter AVEVA connector](https://downloads.smartsights.com/files/Products/XLR/docs/product/sytech-xlreporter-connector-aveva.pdf) · [Grafana PI Web API datasource](https://grafana.com/grafana/plugins/gridprotectionalliance-osisoftpi-datasource/) · [IOTA PI Vision driver](https://docs.iotasoft.com/admin/drivers/on-prem/aveva-pivision/config.html) · [Raesemann on PSA licensing](https://raesemann.com/why-you-should-have-a-pi-system-access-server-license/)

**Implementation stack**
[QuestPDF LICENSE v3.0](https://raw.githubusercontent.com/QuestPDF/QuestPDF/main/LICENSE.md) · [QuestPDF Community License](https://www.questpdf.com/license/community.html) · [QuestPDF pricing](https://www.questpdf.com/pricing.html) · [iText AGPLv3 terms](https://itextpdf.com/how-buy/AGPLv3-license) · [EPPlus license overview](https://epplussoftware.com/en/LicenseOverview/) · [Hangfire pricing/licensing](https://www.hangfire.io/pricing/) · [wkhtmltopdf (archived)](https://github.com/wkhtmltopdf/wkhtmltopdf) · [WeasyPrint API reference](https://doc.courtbouillon.org/weasyprint/stable/api_reference.html) · [WeasyPrint first steps (Windows)](https://doc.courtbouillon.org/weasyprint/stable/first_steps.html) · [Playwright page.pdf()](https://playwright.dev/docs/api/class-page#page-pdf) · [CSS Paged Media vs Paged.js](https://doppio.sh/guide/css-paged-media-vs-pagedjs) · [Paged.js](https://pagedjs.org/) · [Chrome headless print-to-PDF gotchas](https://andre.arko.net/2025/05/25/chrome-headless-print-to-pdf/) · [Playwright #32360 (NTLM)](https://github.com/microsoft/playwright/issues/32360) · [Puppeteer #11626 (NTLM)](https://github.com/puppeteer/puppeteer/issues/11626) · [robotframework-browser #3944 (Kerberos)](https://github.com/MarketSquare/robotframework-browser/issues/3944) · [pyspnego](https://pypi.org/project/pyspnego/) · [requests-negotiate-sspi (2018)](https://pypi.org/project/requests-negotiate-sspi/) · [PIconnect](https://github.com/Hugovdberg/PIconnect) · [AVEVA PI Web API Python sample](https://github.com/AVEVA/sample-pi_web_api-common_actions-python) · [Microsoft: updated SMTP AUTH deprecation timeline](https://techcommunity.microsoft.com/blog/exchange/updated-exchange-online-smtp-auth-basic-authentication-deprecation-timeline/4489835) · [Microsoft: High Volume Email](https://learn.microsoft.com/en-us/exchange/mail-flow-best-practices/high-volume-mails-m365) · [Microsoft: Graph large attachments](https://learn.microsoft.com/en-us/graph/outlook-large-attachments) · [Microsoft: RBAC for Applications](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac) · [Microsoft: gMSA management](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/group-managed-service-accounts/group-managed-service-accounts/manage-group-managed-service-accounts) · [Microsoft: ASP.NET Core Windows Auth](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0) · [HttpPlatformHandler Windows Auth tips](https://docs.lextudio.com/blog/httpplatformhandler-windows-authentication-tips/)
