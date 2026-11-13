---
ai_generated: true
model: "OpenAI/gpt-6.1-sol@unknown"
operator: "jMM-at-CODETraining"
chat_id: "identity-cookie-configuration-20261111"
prompt: |
  go ahead with step 3 using tdd
started: "2026-11-11T11:25:11.1718321-08:00"
ended: "2026-11-11T11:31:01.7002632-08:00"
task_durations:
  - task: "Test and configure Identity services and cookie middleware; document and verify"
    duration: "00:05:50"
total_duration: "00:05:50"
ai_log: "ai-logs/2026/11/11/identity-cookie-configuration-20261111/conversation.md"
source: "user"
revisions:
  - chat_id: "flagged-login-flow-20261111"
    model: "OpenAI/gpt-6.1-sol@unknown"
    date: "2026-11-11"
    change: "Document real login endpoints, CSRF, default-on flag, migrations, and initial provisioning."
    ai_log: "ai-logs/2026/11/11/flagged-login-flow-20261111/conversation.md"
  - chat_id: "implement-feature-flags-appsettings-20261111"
    model: "OpenAI/gpt-6.1-sol@unknown"
    date: "2026-11-11"
    change: "Document flag configuration, dependency injection, runtime JSON reload, and testing."
    ai_log: "ai-logs/2026/11/11/implement-feature-flags-appsettings-20261111/conversation.md"
---

# Daybook Developer Guide

This guide covers running and testing Daybook, its implementation layout and
configuration, and the task API.

## Prerequisites

- .NET 10 SDK
- PowerShell or another terminal

## Run and test

From the repository root, start the app with:

```powershell
dotnet run --project .\src\TodoManager
```

For the first run, follow [initial-account provisioning](../../../README.md#run)
to supply bootstrap credentials securely. Login is on by default and startup
fails explicitly if the empty Identity database cannot be provisioned.

The development launch profile listens at `http://localhost:5050`. Open that
address to use the browser UI. The app serves both the UI and the JSON API.

Run the integration tests with:

```powershell
dotnet test .\tests\TodoManager.Tests
```

The tests use temporary SQLite databases. They cover API CRUD and validation,
persistence, browser assets, OpenAPI, recurring tasks, and database upgrades.

## Project layout

- `Program.cs` configures dependency injection, JSON enum serialization,
  validation, OpenAPI, static files, task database initialization, and Identity
  cookie authentication/authorization middleware.
- [ApplicationUser.cs](../ApplicationUser.cs) uses Identity's standard user fields.
- [ApplicationDbContext.cs](../ApplicationDbContext.cs) contains Identity's EF Core model.
- `TaskEndpoints.cs` maps the HTTP routes under `/api/tasks`.
- `TaskContracts.cs` defines request and response models, priority and
  recurrence enums, and request validation attributes.
- `TaskService.cs` implements task CRUD and recurring-task generation.
- `TaskDatabase.cs` opens SQLite connections and creates or upgrades the schema.
- `FeatureFlags.cs` exposes named configuration-backed feature flags through DI.
- `wwwroot/` contains the browser UI assets.
- `TodoManager.http` contains example API requests.

## Class diagram

```mermaid
classDiagram
    class Program {
        <<entry point>>
    }
    class TaskEndpoints {
        <<static>>
        +MapTasks(app)
    }
    class ITaskService {
        <<interface>>
        +GetAllAsync(ct)
        +GetAsync(id, ct)
        +CreateAsync(request, ct)
        +UpdateAsync(id, request, ct)
        +DeleteAsync(id, ct)
    }
    class TaskService
    class TimeProvider
    class TaskDatabase {
        +OpenConnection() SqliteConnection
        +InitializeAsync() Task
    }
    class SaveTaskRequest {
        +string Title
        +string? Notes
        +TaskPriority Priority
        +DateOnly? DueDate
        +bool IsCompleted
        +TaskRecurrence Recurrence
    }
    class TaskResponse {
        +long Id
        +string Title
        +string Notes
        +TaskPriority Priority
        +DateOnly? DueDate
        +bool IsCompleted
        +DateTimeOffset CreatedAt
        +TaskRecurrence Recurrence
    }
    class TaskPriority {
        <<enumeration>>
        Low
        Medium
        High
    }
    class TaskRecurrence {
        <<enumeration>>
        None
        Daily
        Weekly
        Monthly
    }

    Program ..> TaskEndpoints : maps routes
    Program ..> ITaskService : registers
    Program ..> TaskDatabase : initializes
    TaskEndpoints ..> ITaskService : uses
    TaskEndpoints ..> SaveTaskRequest : accepts
    TaskEndpoints ..> TaskResponse : returns
    TaskService ..|> ITaskService
    TaskService --> TaskDatabase : persists tasks
    TaskService --> TimeProvider : reads clock
    TaskService ..> SaveTaskRequest : accepts
    TaskService ..> TaskResponse : returns
    SaveTaskRequest ..> TaskPriority
    SaveTaskRequest ..> TaskRecurrence
    TaskResponse ..> TaskPriority
    TaskResponse ..> TaskRecurrence

    classDef bootstrap fill:#EDE9FE,stroke:#6D28D9,color:#2E1065
    classDef api fill:#DBEAFE,stroke:#2563EB,color:#172554
    classDef service fill:#DCFCE7,stroke:#16A34A,color:#14532D
    classDef persistence fill:#FFEDD5,stroke:#EA580C,color:#7C2D12
    classDef contract fill:#CCFBF1,stroke:#0F766E,color:#134E4A
    classDef enum fill:#FEF3C7,stroke:#D97706,color:#78350F
    classDef external fill:#F3F4F6,stroke:#6B7280,color:#1F2937
    class Program bootstrap
    class TaskEndpoints api
    class ITaskService api
    class TaskService service
    class TaskDatabase persistence
    class SaveTaskRequest contract
    class TaskResponse contract
    class TaskPriority enum
    class TaskRecurrence enum
    class TimeProvider external
```

Colors distinguish application startup, API, service, persistence, contracts,
enums, and external dependencies, respectively.

## Configuration and persistence

By default, the SQLite database is stored at
`src/TodoManager/App_Data/tasks.db`. Override the location with the
`TaskDatabase__Path` environment variable or the `TaskDatabase:Path` setting.
Use an absolute path outside the public web root.

The schema is initialized automatically when the app starts. Existing databases
are upgraded in place in version order, retaining task data. The current schema
version is stored in SQLite's `user_version` pragma. The application uses SQLite
and is designed for a single server; it does not provide multi-instance storage
or real-time browser synchronization. Authenticated users share all tasks.

### Feature flags

`IFeatureFlags` is registered as a singleton and reads named booleans from the
`FeatureFlags` section in `appsettings.json`. Add a key such as
`"NewTaskFlow": true`, then inject `IFeatureFlags` and call
`IsEnabled("NewTaskFlow")` at the code path that implements that feature.
Missing flags are disabled. JSON appsettings changes are reloaded at runtime;
environment variables use double underscores, for example
`FeatureFlags__NewTaskFlow=true`. The Login flag defaults to true and is
snapshotted at startup. Set `FeatureFlags__Login=false` and restart to explicitly
restore the anonymous shared app; this disables login/logout, task authorization,
CSRF requirements, and Identity initialization. Do not disable it for public
hosting. Other callers of IFeatureFlags continue to read live configuration.

### Identity and cookie configuration

Identity uses a separate SQLite database, defaulting to `App_Data/identity.db`
under the application content root. Override it with `IdentityDatabase__Path` or
`IdentityDatabase:Path`. Use an absolute path outside the public web root, keep
it separate from the task database. With login enabled,
[IdentityInitialization](../IdentityInitialization.cs) creates the directory and
applies EF Core migrations, then provisions the first user if no users exist.
Set IdentityBootstrap:UserName and IdentityBootstrap:Password only through secure
configuration for the initial run. Missing credentials and password-policy
failures stop startup. Existing users are never reset by bootstrap settings.

User, role, and sign-in managers are available through scoped dependency
injection. Identity's application cookie is the default authentication/challenge
scheme. The `Daybook.Identity` cookie is HTTP-only, `SameSite=Lax`, path `/`,
with a 30-minute ticket lifetime and sliding renewal. Production and other
non-Development environments require Secure cookies; Development uses Secure
when the request is HTTPS and permits HTTP for localhost development.
Nonpersistent sign-ins produce browser-session cookies; ticket expiry still
applies independently.

Authentication precedes authorization after routing. There is no fallback
authorization policy. Task routes require authorization when Login is enabled;
static assets remain public.
Protected `/api` requests receive 401 or 403 without redirects. Browser challenges
redirect to `/login` or `/access-denied`, which are reserved paths without pages
yet; the app's sign-in form is served at `/`. Password login counts failures,
locking an account after five failures for 15 minutes. A per-remote-IP limiter
allows ten login attempts per minute. Configure trusted forwarding if deploying
behind a reverse proxy.

[AuthEndpoints](../AuthEndpoints.cs) exposes GET /api/auth/session and POST
/api/auth/login and /api/auth/logout. Credentials use username/password JSON;
invalid, unknown, and locked-out accounts get the same generic 401. The session
response includes loginEnabled, isAuthenticated, userName, and csrfToken. Fetch
again after login to get a token bound to the signed-in user. Send X-CSRF-TOKEN
and retain cookies for login, logout, and all task writes. Verification failures
return a 400 Problem Details response. API responses are marked no-store. There
is no registration, password-recovery, or MFA flow.

[Configuration tests](../../../tests/TodoManager.Tests/IdentityConfigurationTests.cs)
exercise the registrations, paths, schemes, and lockout settings.
[Cookie tests](../../../tests/TodoManager.Tests/IdentityCookieTests.cs) seed users
only in isolated test databases and add endpoints only to the test host. They
use a controlled clock to exercise expiry and renewal without sleeps. These
tests isolate cookie infrastructure from password login.
[LoginFlowTests](../../../tests/TodoManager.Tests/LoginFlowTests.cs) cover the real
endpoints, flag modes, CSRF route variants, migrations, bootstrap, lockout, rate
limits, and shared access. Existing regression fixtures explicitly disable Login.

## Recurring tasks

Recurrence is applied when a task changes from incomplete to complete. The
completed task remains in history, and one incomplete successor is created in
the same database transaction. Its due date is calculated from the completion
date in the server's local timezone: one day, seven days, or one calendar month
later for daily, weekly, or monthly recurrence. A monthly date is clamped to the
last valid day of the target month. The previous due date does not affect the
successor's due date, and an initial due date is optional.

Each task occurrence can generate at most one successor, including if it is
reopened and completed again. Successors are not generated in the background;
deleting a task does not delete its successor. Set recurrence to `None` to stop
repeating from the active occurrence.

## API reference

The API is rooted at `/api/tasks`. Requests and responses use JSON with
camel-cased property names. Priority and recurrence values are strings, not
numbers. All successful task responses have this shape:

```json
{
  "id": 1,
  "title": "Plan the week",
  "notes": "Choose three priorities",
  "priority": "Medium",
  "dueDate": "2026-11-16",
  "isCompleted": false,
  "createdAt": "2026-11-10T20:00:00.0000000+00:00",
  "recurrence": "None"
}
```

`dueDate` is either a `YYYY-MM-DD` calendar date or `null`. `createdAt` is the
creation timestamp in UTC. The response's `notes` value is a string; omitted
notes in a request are stored as an empty string.

### Endpoints

| Method | Route | Success response | Description |
|---|---|---|---|
| `GET` | `/api/tasks` | `200 OK`, array of tasks | Lists tasks newest ID first. |
| `GET` | `/api/tasks/{id}` | `200 OK`, task | Gets a task by ID. |
| `POST` | `/api/tasks` | `201 Created`, task and `Location` header | Creates a task. |
| `PUT` | `/api/tasks/{id}` | `200 OK`, updated task | Replaces the task's editable fields. |
| `DELETE` | `/api/tasks/{id}` | `204 No Content` | Permanently deletes a task. |

Unknown IDs return `404 Not Found` for `GET`, `PUT`, and `DELETE`.

### Create and replace request

`POST /api/tasks` and `PUT /api/tasks/{id}` accept the same request shape:

```json
{
  "title": "Plan the week",
  "notes": "Choose three priorities",
  "priority": "High",
  "dueDate": "2026-11-16",
  "isCompleted": false,
  "recurrence": "Weekly"
}
```

| Property | Type | Required | Default | Rules |
|---|---|---:|---|---|
| `title` | string | Yes | — | 1–200 characters; whitespace-only titles are invalid. Surrounding whitespace is trimmed when stored. |
| `notes` | string or `null` | No | `""` | Maximum 2,000 characters; surrounding whitespace is trimmed. |
| `priority` | string | No | `"Medium"` | One of `"Low"`, `"Medium"`, or `"High"`. |
| `dueDate` | string or `null` | No | `null` | Valid `YYYY-MM-DD` calendar date. |
| `isCompleted` | boolean | No | `false` | Whether the task is complete. |
| `recurrence` | string | No | `"None"` | One of `"None"`, `"Daily"`, `"Weekly"`, or `"Monthly"`. |

`PUT` replaces these fields rather than partially updating them. Send the full
desired state; omitted optional fields take their defaults. For example, an
omitted `isCompleted` resets completion to `false`, and an omitted `dueDate`
clears the due date.

### Status and validation errors

- Invalid JSON values or request fields return `400 Bad Request` with a Problem
  Details response (`application/problem+json`).
- Invalid enum names, numeric enum values, invalid dates, missing titles, and
  over-limit title or notes values are rejected.
- A request to a task ID that does not exist returns `404 Not Found`.

### Examples

List tasks:

```http
GET /api/tasks
```

Create a task:

```http
POST /api/tasks
Content-Type: application/json

{
  "title": "Weekly review",
  "priority": "Medium",
  "recurrence": "Weekly"
}
```

Complete a recurring task (replace `1` with the task ID). The response contains
the completed task; the successor is a separate task returned by a later list
or get request:

```http
PUT /api/tasks/1
Content-Type: application/json

{
  "title": "Weekly review",
  "priority": "Medium",
  "isCompleted": true,
  "recurrence": "Weekly"
}
```

### OpenAPI and request samples

In the Development environment, the OpenAPI document is available at
`http://localhost:5050/openapi/v1.json`. The project's ready-to-run HTTP
examples are in [`TodoManager.http`](../TodoManager.http).

## Deployment and security notes

The default development listener is localhost-only. Login is enabled by default;
disabling it allows anyone who can reach the app to change every task. For
network deployment, require HTTPS and protect/persist Data Protection keys.
Sign-out clears a browser cookie rather than revoking every session. Review
backup and secret-management practices before hosting publicly. The automatic
migration/provisioning path targets this app's single-server deployment.
