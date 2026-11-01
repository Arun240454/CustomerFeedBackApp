# CustomerFeedbackApp

Customers self-register, sign in and submit feedback about a store. Each submission is categorised and stored. Store
managers get a landing page that summarises customer-satisfaction trends for the stores they are assigned to.

## Architecture

```
React (Vite, :5173) ──REST/JWT──┬─> Identity.Api   :5101  register, login, issue JWT
                                ├─> Feedback.Api   :5102  stores, categories, submit + list own feedback
                                └─> Analytics.Api  :5103  manager summary, recent feedback, async categoriser
                                         ▲
   Feedback.Api ──FeedbackSubmitted──> OutboxMessages (SQL Server) ──> Analytics.Api consumer
```

| Project | Responsibility |
|---|---|
| `src/CustomerFeedback.Identity.Api` | Registration, login, lockout, password hashing (PBKDF2-SHA256 + salt), JWT issuing |
| `src/CustomerFeedback.Feedback.Api` | Write side: validates/sanitises/stores submissions, publishes `FeedbackSubmitted` |
| `src/CustomerFeedback.Analytics.Api` | Read side: satisfaction trend summary (indexed queries); background categorisation consumer |
| `src/CustomerFeedback.Shared` | EF Core model + migrations, JWT/encryption/sanitising, exception handlers, outbox, config layers |
| `web` | React + TypeScript SPA |
| `tests/CustomerFeedback.Tests` | xUnit + Moq + FluentAssertions |
| `tests/CustomerFeedback.LoadTest` | Console load test for submission and summary endpoints |

### Decisions where the brief left a choice

- **Frontend:** React 19 + TypeScript + Vite. Hand-written SVG chart (no chart dependency).
- **Database / ORM:** SQL Server, EF Core 10 code-first migrations (target framework `net10.0`).
- **Messaging:** a **transactional outbox in SQL Server** (`OutboxMessages` table) polled by a hosted service, instead of
  RabbitMQ/Kafka. The event is written in the same transaction as the submission, so it cannot be lost, and there is no
  extra infrastructure to run. The consumer claims rows with `UPDLOCK, READPAST`, so several instances can run safely.
  Swapping to a broker only means replacing `IEventPublisher` and `OutboxProcessor`.
- **Where messaging is used (and where it is not):** only for categorisation. Everything else is a direct REST call.
  Customers get an immediate response; the categoriser then maps topics found in the free-text comment
  (e.g. "queue", "dirty", "rude") to categories not already rated by the customer, scoring them with the overall
  rating, and files uncategorisable feedback under "Other". Trend aggregation is *not* asynchronous: it is a set of
  index-covered queries that are fast enough to run on demand.
- **Shared database:** the three services deploy independently but share one SQL Server schema (as in the provided
  script), each touching only the tables it needs. Splitting databases per service is the natural next step.
- **Roles:** self-registration is open to `Customer` and `StoreManager` (per the SQL document's assumptions). A manager
  sees **no data** until a row exists in `dbo.StoreManagers`. Authorisation is checked per store, not just per role.
- **Time:** all reporting uses UTC calendar days.

## Security

- JWT bearer auth (HS256, 60 min, 30 s clock skew); role policies `CustomerOnly` / `StoreManagerOnly`; per-store check for managers.
- Passwords: PBKDF2-HMAC-SHA256, 600k iterations, random salt, constant-time compare; lockout after 5 failures for 15 min
  (tunable from the database, see Configuration); identical errors and a dummy hash check for unknown emails; rate limiting on auth endpoints.
- **Encryption at rest:** `Users.PhoneNumber`, `FeedbackSubmissions.Comments` and `FeedbackItems.Comment` are encrypted
  with AES-256-GCM in the EF layer (random nonce per value, authenticated). Their columns are therefore `varchar` ciphertext and
  wider than in the original script (`varchar(255)` / `varchar(max)`); plaintext length limits are enforced by validation.
  For whole-database protection also enable SQL Server TDE. Keys come from user-secrets/environment variables only.
- Validation: FluentValidation on the server, mirrored validators in the SPA. All free text is sanitised on the server
  (HTML stripped, control characters removed, whitespace normalised) and the SPA never renders HTML from the API.
- Secrets (`Jwt:Key`, `Encryption:Key`) are never in source control.

## Prerequisites

- .NET SDK 10 (`dotnet --version`), Node.js 20+, SQL Server reachable at `localhost` with Windows authentication.
- PowerShell. If scripts are blocked, prefix with `powershell -ExecutionPolicy Bypass -File ...`.

## Setup and run

```powershell
# 1. Generate development secrets (stored outside the repo with dotnet user-secrets; shared by all three services)
./scripts/setup-dev-secrets.ps1

# 2. Create/migrate the database (uses the connection string from the brief; override with ConnectionStrings__CustomerFeedbackDb)
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/CustomerFeedback.Shared --startup-project src/CustomerFeedback.Identity.Api

# 3. Start everything (3 APIs + web) - or run each project yourself, see below
./scripts/run-all.ps1

# 4. Open http://localhost:5173
```

Run pieces individually:

```powershell
dotnet run --project src/CustomerFeedback.Identity.Api  --urls http://localhost:5101
dotnet run --project src/CustomerFeedback.Feedback.Api  --urls http://localhost:5102
dotnet run --project src/CustomerFeedback.Analytics.Api --urls http://localhost:5103
cd web; npm install; npm run dev
```

**Demo data.** In the `Development` environment the Identity service migrates the database on start and, if it has no
users, seeds demo accounts (password `Passw0rd!Demo1`, development only):
`manager.delhi@example.com` (assigned to the Delhi store), `asha@example.com`, `rohan@example.com`, `priya@example.com`.

**Already ran `CustomerFeedbackDB.sql`?** EF owns the schema in this app, so migrations fail with
"There is already an object named ..." against a database created by the script (and the script's
`PLACEHOLDER` password hashes cannot log in). Either drop that database first, or point the app at a fresh one:

```powershell
$env:ConnectionStrings__CustomerFeedbackDb = 'Server=localhost;Database=CustomerFeedbackDB_Dev;Trusted_Connection=True;TrustServerCertificate=True;'
```

The stored procedures from the script are intentionally not used; the same logic lives in `AnalyticsRepository` / `SummaryService`.

## Configuration

Precedence, lowest to highest: `appsettings.json` → `appsettings.{Environment}.json` → user-secrets → `service.ini`
(optional property file, see `config/service.ini.example`) → **database** table `dbo.AppSettings` → environment variables → command line.
Environment variables always win, so a deployment can override the database. Seeded database settings:
`Security:MaxFailedLogins`, `Security:LockoutMinutes`, `Analytics:DefaultWindowDays` (read on startup/restart).

| Setting | Where |
|---|---|
| `ConnectionStrings:CustomerFeedbackDb` | `appsettings.json` (Windows auth, no secret). `Development` adds `TrustServerCertificate=True` |
| `Jwt:Key` (≥ 32 chars), `Encryption:Key` (base64, 32 bytes) | **secrets only**: user-secrets or `Jwt__Key` / `Encryption__Key` env vars |
| `Cors:AllowedOrigins`, `Jwt:Issuer/Audience/ExpiryMinutes`, `Outbox:*`, `Analytics:*` | JSON / ini / env |

## Tests

```powershell
dotnet test                                   # all .NET tests (142)
dotnet test tests/CustomerFeedback.Tests --settings coverlet.runsettings --collect:"XPlat Code Coverage"   # ~90% line coverage
cd web; npm test                              # SPA validation tests (13)
```

The suite covers validators, sanitiser, encryption, JWT, password hashing, auth/lockout, feedback submission, summary maths,
categoriser, outbox, exception handlers, and the HTTP pipeline (role policies, standardised errors) via TestServer, using Moq for dependencies and the EF in-memory provider for data.
The in-memory provider cannot validate SQL translation, so an opt-in test runs every dashboard query against a real database:
`$env:CF_TEST_SQLSERVER = '<connection string of a migrated database>'; dotnet test`.

## Load test

With all services running and demo data seeded:

```powershell
dotnet run --project tests/CustomerFeedback.LoadTest -c Release -- --seconds 30 --customers 20 --managers 10
```

Customers `POST /api/feedback` while managers `GET /api/analytics/stores/1/summary`. It prints throughput, error rate and
p50/p95/p99 per endpoint, and exits non-zero if the error rate exceeds 1% or p95 exceeds 1000 ms (`--max-error-rate`, `--p95-ms`).
Reference run on a dev laptop (10 customers, 5 managers, 10 s): submit ≈ 46 rps / p95 389 ms; summary ≈ 85 rps / p95 119 ms; 0 errors.

## Performance notes

Indexes (from the SQL script, created by the migration): `IX_FeedbackSubmissions_Store_Date (StoreId, SubmittedDateUtc) INCLUDE (OverallRating, CustomerUserId)`
(covers the trend queries), a filtered `IX_FeedbackSubmissions_Customer` for "my feedback", `IX_FeedbackItems_Category`, `IX_StoreManagers_User`,
plus a filtered `IX_OutboxMessages_Pending`. The summary reads both comparison windows in **one** covering index seek
(grouped by day), zero-fills missing days in memory, and uses read-only (`AsNoTracking`) projections.

## Static analysis

`Directory.Build.props` enables the latest .NET analyzers; code uses nullable reference types, primary constructors, small
single-purpose classes and interfaces for every dependency. `sonar-project.properties` is ready for SonarScanner
(coverage in OpenCover format via `coverlet.runsettings`). Run: `dotnet sonarscanner begin /k:CustomerFeedbackApp ...` → `dotnet test ...` → `end`.

## API summary

| Method & path | Role | Notes |
|---|---|---|
| `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me` | anonymous / anonymous / any | 423 when locked |
| `GET /api/stores`, `GET /api/categories` | any signed-in user | |
| `POST /api/feedback`, `GET /api/feedback/mine?page=&pageSize=` | Customer | `isAnonymous: true` stores no customer id |
| `GET /api/analytics/stores`, `GET /api/analytics/stores/{id}/summary?days=`, `GET /api/analytics/stores/{id}/feedback/recent` | StoreManager (assigned stores only) | window 1-365 days, default 30 |
| `GET /health` (each service) | anonymous | database check |

Errors are RFC 7807 `application/problem+json` with `code` and `traceId` (and `errors` for validation); the 500 response never leaks internals.
Logs are structured (Serilog): readable on the console, compact JSON in `logs/<service>-*.json`.

## Known limitations

- Managers are assigned to stores by inserting into `dbo.StoreManagers` (no admin UI yet).
- Email confirmation, password reset and token refresh are not implemented (`EmailConfirmed` is stored but unused).
- The keyword categoriser is deliberately simple; the `IFeedbackCategorizer` interface is the seam for an ML/LLM implementation.
- Encryption keys are single-version; rotation would need a key-id prefix (the `enc1:` prefix leaves room for it).
