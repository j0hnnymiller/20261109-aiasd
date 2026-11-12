---
ai_generated: true
model: "unknown/unknown@unknown"
operator: "jMM-at-CODETraining"
chat_id: "add-review-solid-20261110"
prompt: |
  /skill add
  skill=review the codebase for SOLID violations, scope=This repository
started: "2026-11-10T11:52:09.5997879-08:00"
ended: "2026-11-10T11:52:25.4054404-08:00"
task_durations:
  - task: "Design repository-local SOLID review workflow"
    duration: "00:00:15"
total_duration: "00:00:15"
ai_log: "ai-logs/2026/11/10/add-review-solid-20261110/conversation.md"
source: "user"
revisions:
  - chat_id: "testability-recommendations-20261110"
    model: "unknown/unknown@unknown"
    date: "2026-11-10"
    change: "Link the testability recommendations."
    ai_log: "ai-logs/2026/11/10/testability-recommendations-20261110/conversation.md"
  - chat_id: "update-readme-current-state-20261110"
    model: "unknown/unknown@unknown"
    date: "2026-11-10"
    change: "Document current app capabilities, security headers, limits, migrations, API behavior, and test coverage."
    ai_log: "ai-logs/2026/11/10/update-readme-current-state-20261110/conversation.md"
  - chat_id: "add-review-solid-20261110"
    model: "unknown/unknown@unknown"
    date: "2026-11-10"
    change: "Document repository-local SOLID review skill."
    ai_log: "ai-logs/2026/11/10/add-review-solid-20261110/conversation.md"
  - chat_id: "timestamp-review-solid-20261110"
    model: "unknown/unknown@unknown"
    date: "2026-11-10"
    change: "Document timestamped SOLID review reports."
    ai_log: "ai-logs/2026/11/10/timestamp-review-solid-20261110/conversation.md"
  - chat_id: "test-workflow-20261111"
    model: "unknown/unknown@unknown"
    date: "2026-11-11"
    change: "Document automated test checks."
    ai_log: "ai-logs/2026/11/11/test-workflow-20261111/conversation.md"
  - chat_id: "tdd-instructions-20261111"
    model: "unknown/unknown@unknown"
    date: "2026-11-11"
    change: "Add test-driven development instructions."
    ai_log: "ai-logs/2026/11/11/tdd-instructions-20261111/conversation.md"
  - chat_id: "fix-pending-save-race-20261111"
    model: "OpenAI/gpt-6.1-sol@unknown"
    date: "2026-11-11"
    change: "Document pending-save form locking and deterministic browser regression coverage."
    ai_log: "ai-logs/2026/11/11/fix-pending-save-race-20261111/conversation.md"
  - chat_id: "identity-context-20261111"
    model: "OpenAI/gpt-6.1-sol@unknown"
    date: "2026-11-11"
    change: "Document the Identity user model, database context, and isolated SQLite tests."
    ai_log: "ai-logs/2026/11/11/identity-context-20261111/conversation.md"
  - chat_id: "identity-cookie-configuration-20261111"
    model: "OpenAI/gpt-6.1-sol@unknown"
    date: "2026-11-11"
    change: "Document registered Identity services, cookie policy, test coverage, and deferred access control."
    ai_log: "ai-logs/2026/11/11/identity-cookie-configuration-20261111/conversation.md"
  - chat_id: "implement-feature-flags-appsettings-20261111"
    model: "OpenAI/gpt-6.1-sol@unknown"
    date: "2026-11-11"
    change: "Document appsettings-backed feature flags, defaults, reload behavior, and server-side checks."
    ai_log: "ai-logs/2026/11/11/implement-feature-flags-appsettings-20261111/conversation.md"
  - chat_id: "flagged-login-flow-20261111"
    model: "OpenAI/gpt-6.1-sol@unknown"
    date: "2026-11-11"
    change: "Document default-on shared login, CSRF, migrations, bootstrap, flag opt-out, and verified tests."
    ai_log: "ai-logs/2026/11/11/flagged-login-flow-20261111/conversation.md"
  - chat_id: "refactoring-finding-issues-20261112"
    model: "OpenAI/unknown@unknown"
    date: "2026-11-12"
    change: "Link the local issue index for refactoring and instruction-audit findings."
    ai_log: "ai-logs/2026/11/12/refactoring-finding-issues-20261112/conversation.md"
  - chat_id: "version-task-schema-upgrades-20261112"
    model: "Anthropic/claude@unknown"
    date: "2026-11-12"
    change: "Document versioned task-schema upgrades and their test coverage (GitHub issue #10)."
    ai_log: "ai-logs/2026/11/12/version-task-schema-upgrades-20261112/conversation.md"
---

# Daybook

A responsive todo manager built with ASP.NET Core (.NET 10), SQLite, and plain
HTML/CSS/JavaScript. Create, edit, complete, reopen, and delete tasks; add notes,
priorities, and due dates; search, filter, and sort your list.

## Current project state

Daybook is a working, single-server task manager. The ASP.NET Core minimal API
serves both the JSON task API and the browser UI; task data is persisted in one
SQLite database. The UI provides task counts, search, status filters, sorting,
editing, and confirmation before deletion. API input is validated and invalid
requests return Problem Details. Responses include a Content Security Policy and
`X-Content-Type-Options: nosniff`; priority and recurrence values use JSON strings.

While a request is pending, the editor fields and task action controls are
disabled. A successful save clears the form and returns focus to the title;
a failed save preserves the entered values and re-enables the form for retry.

Login is enabled by default. All signed-in users share the same task list; there
is no per-user ownership. The app does not provide multi-instance storage,
real-time updates, or background scheduling. Changes from another browser appear
after Refresh. Concurrent edits replace the whole task, so the last save wins.

### Identity storage foundation

[ApplicationUser](src/TodoManager/ApplicationUser.cs) inherits Identity's standard
user fields. [ApplicationDbContext](src/TodoManager/ApplicationDbContext.cs)
inherits `IdentityDbContext<ApplicationUser>` and accepts externally configured
EF Core options without hardcoding a database path or modifying Identity's model.
The intended deployment uses a separate Identity SQLite database so EF Core
migrations do not manage the existing task schema.

Identity services and cookie authentication middleware are registered in
[Program.cs](src/TodoManager/Program.cs), including user, role, and sign-in managers.
The Identity SQLite path defaults to `App_Data/identity.db` under the application's
content root. Override it with `IdentityDatabase__Path` (or `IdentityDatabase:Path`
in configuration), using an absolute path outside the public web root, separate
from task storage. When login is enabled, startup creates the containing directory
and applies the [Identity migrations](src/TodoManager/Migrations). It never
migrates task data into Identity or changes task ownership.

The `Daybook.Identity` cookie is HTTP-only, uses `SameSite=Lax` and path `/`,
and has a 30-minute ticket lifetime with sliding renewal. It is Secure-only
outside Development; Development permits HTTP for the existing localhost launch
profile. Requests to `/api` endpoints receive 401/403 rather than authentication
redirects. The browser UI at `/` shows the login form when signed out; it checks
session state before showing tasks and clears visible task data after sign-out
or an expired-session response. Passwords are never stored in browser storage.
New users are locked out after five failed password attempts for 15 minutes.
Login is additionally limited to ten requests per minute per remote IP.

[AuthEndpoints](src/TodoManager/AuthEndpoints.cs) exposes:

| Method | Route | Behavior |
|---|---|---|
| GET | `/api/auth/session` | Flag state, current username, and CSRF request token; never cached |
| POST | `/api/auth/login` | Username/password JSON; nonpersistent cookie, or generic 401 |
| POST | `/api/auth/logout` | Authenticated sign-out; clears the browser cookie |

With login enabled, every task API operation requires authentication. Login,
logout, and task writes require the session's `X-CSRF-TOKEN` header plus its
antiforgery cookie. Fetch a fresh session token after login because tokens are
bound to the authenticated identity. Invalid verification returns Problem Details
with status 400. Public static assets contain no task data and remain anonymous.
Responses from the enabled API use `Cache-Control: no-store`.

There is no self-registration, password recovery, account-management UI, or MFA
flow. Sign-out clears this browser's cookie, not all sessions or copied cookies.
Production hosting must provide HTTPS and persistent, protected ASP.NET Core
Data Protection keys; configure trusted proxy forwarding before relying on
client-IP rate limits behind a proxy.
The [context tests](tests/TodoManager.Tests/ApplicationDbContextTests.cs) use a
separate open in-memory SQLite connection per test to verify provider selection,
Identity table creation, user persistence, unique normalized usernames,
user-role membership, and missing-user lookup. They use `EnsureCreatedAsync()`
only for isolated test schemas, not production migrations. The
[verification record](ai-logs/2026/11/11/identity-context-20261111/summary.md)
records the test-first results and scope.
The [configuration tests](tests/TodoManager.Tests/IdentityConfigurationTests.cs)
verify services, default schemes, storage paths, lockout settings, and startup
without Identity schema creation. The
[cookie tests](tests/TodoManager.Tests/IdentityCookieTests.cs) use test-only
endpoints, isolated databases, and a controllable clock for flags, authentication,
API challenges, expiry, renewal, and sign-out. See the
[cookie configuration verification record](ai-logs/2026/11/11/identity-cookie-configuration-20261111/summary.md).
The [login API tests](tests/TodoManager.Tests/LoginFlowTests.cs) and
[login browser tests](tests/browser/login.spec.ts) verify the real flow, both flag
states, CSRF defenses, shared access, lockout, rate limiting, migrations, initial
provisioning, expired sessions, and error recovery.

## Feature flags

The app registers `IFeatureFlags` for checking named boolean flags from the
`FeatureFlags` section of [appsettings.json](src/TodoManager/appsettings.json).
For example, add `"NewTaskFlow": true` inside that section, then inject
`IFeatureFlags` and call `IsEnabled("NewTaskFlow")` where the feature behavior
is implemented. Missing flags are disabled. Configuration changes are read
through the active .NET configuration providers; appsettings changes reload
while the app is running, and environment variables can override settings with
names such as `FeatureFlags__NewTaskFlow`.

`Login` defaults to `true`. Explicitly set `FeatureFlags__Login=false` to restore
the anonymous shared app: task operations no longer require login or CSRF tokens,
login/logout endpoints are unavailable, and Identity migrations/provisioning are
skipped. **Disabling this flag removes task access controls.** Use it only on a
trusted local system. Unlike ordinary dynamically read flags, Login is read once
at startup; restart after changing it so UI state and server authorization agree.

## Recurring tasks

Choose **Daily**, **Weekly**, or **Monthly** in the Repeat field when adding or
editing a task. On completion, the task stays in history and a new incomplete task
is created with the same title, notes, priority, and recurrence. Its due date is
one day, seven days, or one calendar month after the completion date, using the
server's local timezone, regardless of the previous due date. An initial due date
is optional. Monthly dates clamp to the last valid day of the next month.

Each occurrence generates at most one successor, even if completion is retried
or the task is reopened and completed again. Deleting a task does not delete its
successor. To stop repeating, set the active occurrence's Repeat field to
**Does not repeat**. No tasks are generated in the background.
Existing databases are automatically upgraded on startup to add recurrence and
successor-tracking fields; existing task data is retained and old tasks default
to no recurrence.

The task schema version is stored in SQLite's `PRAGMA user_version`. Startup
applies only the ordered upgrades newer than the stored version, in one
transaction, then records the current version. Databases created before
versioning report version 0 and are upgraded without duplicating existing
columns. The app refuses to start against a database with a newer schema version
than it supports, leaving that database unchanged.

## Run

Install the .NET 10 SDK. On the first run, supply an initial username and strong
password through a secret provider or environment variables. For local PowerShell
development, prompt for the credentials instead of putting a password in history:

```powershell
$credential = Get-Credential -Message "Create the first Daybook account"
$env:IdentityBootstrap__UserName = $credential.UserName
$env:IdentityBootstrap__Password = $credential.GetNetworkCredential().Password
try {
    dotnet run --project .\src\TodoManager
} finally {
    Remove-Item Env:\IdentityBootstrap__UserName
    Remove-Item Env:\IdentityBootstrap__Password
}
```

The [initializer](src/TodoManager/IdentityInitialization.cs) provisions one
ordinary account only when the Identity database has no users. Missing credentials
or an invalid password cause explicit startup failure. Once provisioned, remove
the bootstrap settings from your deployment; restarting never resets an existing
password or creates additional accounts from changed bootstrap settings. Later
runs use the normal `dotnet run --project .\src\TodoManager` command.

Open http://localhost:5050. Tasks persist across restarts in
`src\TodoManager\App_Data\tasks.db`; the database starts empty.
All browsers connecting to the same server share the same list. Use Refresh to
load changes made on another device; updates are not pushed automatically.
Editing saves the entire task, so the last save wins when people edit concurrently.

The default listener is localhost-only. To run without login for a trusted local
demo, explicitly opt out before starting:

```powershell
$env:FeatureFlags__Login = "false"
dotnet run --project .\src\TodoManager
```

Remove that environment override to restore login on the next start. Do not
expose anonymous mode to the internet. Development HTTP is for local use only;
credentials require HTTPS for any network deployment. No Azure resources are
required or provisioned.

## Test

For behavior changes and bug fixes, follow the
[Test-Driven Development instructions](.github/instructions/test-driven-development.instructions.md).

```powershell
dotnet test .\tests\TodoManager.Tests
```

Integration tests use isolated temporary SQLite databases. Coverage includes
CRUD, validation, persistence across host restarts, serving browser assets,
OpenAPI operations, recurring task dates and month-end clamping, duplicate and
concurrent completion, transaction rollback when successor creation fails, and
versioned schema upgrades from fresh, legacy, partially upgraded, and versioned
databases with data retained.

Collect .NET code coverage in Cobertura format with:

```powershell
dotnet test .\tests\TodoManager.Tests --collect:"XPlat Code Coverage"
```

The report is written under `tests\TodoManager.Tests\TestResults`.

Browser tests exercise the rendered UI and its interactions against the real app:

```powershell
cd .\tests\browser
npm install
npx playwright install chromium
npm test
```

Playwright starts two servers with separate temporary SQLite databases: anonymous
mode on port 5200 and default-on login on port 5201. It generates an ephemeral
bootstrap password at runtime. Browser tests run serially because each mode shares
its server. Existing task API regression tests explicitly opt out of login;
the login suite verifies authenticated operations separately.

The [browser regression tests](tests/browser/tasks.spec.ts) hold save responses
open to verify form locking and recovery after success or failure. Multi-task
setup waits for each new card before entering the next task. The
[TDD verification record](ai-logs/2026/11/11/fix-pending-save-race-20261111/summary.md)
records the failing tests and five consecutive passing suite runs.

The [Tests workflow](.github/workflows/tests.yml) runs both suites on every
push and pull request. Push a branch and check the results before opening a PR;
the checks also run when the PR is updated. The workflow does not prevent
someone from opening a PR, and blocking merges requires configuring both jobs
as required status checks in GitHub branch protection or rulesets.

See [Testability Recommendations](src/TodoManager/docs/testability-recommendations.md)
for prioritized improvements to test seams, deterministic time, assertions, and
test-resource isolation.

See the [Refactoring Plan](src/TodoManager/docs/refactoring-plan.md) for
evidence-backed hotspots and incremental refactoring proposals.

## API

| Method | Route | Behavior |
|---|---|---|
| GET | `/api/tasks` | List tasks, newest first |
| GET | `/api/tasks/{id}` | Get a task |
| POST | `/api/tasks` | Create; returns 201 and Location |
| PUT | `/api/tasks/{id}` | Replace fields, including completion |
| DELETE | `/api/tasks/{id}` | Delete; returns 204 |

Titles are required (maximum 200 characters); notes allow 2,000 characters.
Priority is `Low`, `Medium` (default), or `High`.
`recurrence` is `None` (default), `Daily`, `Weekly`, or `Monthly`.
`dueDate` is an optional
`YYYY-MM-DD` calendar date, intentionally not a timezone-dependent timestamp.
`createdAt` is a UTC timestamp. Overdue means an incomplete task due before the
browser's current calendar date.

Invalid request bodies return `400` Problem Details responses. Unknown task IDs
return `404`; successful deletion returns `204`.

Development OpenAPI: http://localhost:5050/openapi/v1.json.
Example requests: [TodoManager.http](src/TodoManager/TodoManager.http).
Override the database location with the `TaskDatabase__Path` environment variable
(use an absolute path outside the public web root). Keep the database on local
disk and use SQLite-aware backups while the app runs, or stop the app before
copying its database files. Schema creation and versioned schema upgrades are
automatic. This is a single-server app, not a multi-instance service.

For setup, project structure, configuration, recurrence behavior, and a detailed
endpoint and payload reference, see the [Developer Guide](src/TodoManager/docs/developers-guide.md).

## Project requirements skill

[Create Project Requirements](.github/skills/create-project-requirements/SKILL.md)
creates or updates a Markdown requirements document covering project purpose,
intended users, key workflows, business rules, tech stack, assumptions, and
unanswered questions. Ask to "create project requirements" or "write a project
requirements document" to use it.

## Local issues

The [local issue descriptions](issues/README.md) track actionable findings from
the instruction-compliance audit and the refactoring plan.

## Review skill

[Review SOLID](.github/skills/review-solid/SKILL.md) provides a read-only,
evidence-backed review of Single Responsibility, Open/Closed, Liskov Substitution,
Interface Segregation, and Dependency Inversion. Ask to "review the codebase for
SOLID violations" to use it. It reports concrete impacts, focused recommendations,
and validation suggestions without changing application code. Each report includes
the run's start date and time with its timezone.
