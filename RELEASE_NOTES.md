# Release Notes

## Unreleased

Daybook is a single-server task manager built with ASP.NET Core, SQLite, and a
browser-based interface.

### Added

- Create, view, edit, complete, reopen, and delete tasks with notes, priority,
  and optional due dates.
- Search task titles and notes, filter by completion status, and sort the list.
- Set tasks to repeat daily, weekly, or monthly. Completing a recurring task
  retains the completed occurrence and creates one successor. Successor due
  dates are based on the completion date; monthly dates clamp to the last valid
  day of the month.
- A JSON API for listing, retrieving, creating, replacing, and deleting tasks,
  with development OpenAPI documentation.
- API validation with Problem Details responses, plus content security policy
  and `X-Content-Type-Options: nosniff` response headers.
- .NET code coverage collection for integration tests using Coverlet.

### Data and compatibility

- Task data is persisted in SQLite and retained across application restarts.
- Database schema creation and upgrades run automatically at startup. Existing
  databases are migrated to support recurrence and successor tracking, with
  existing tasks defaulting to no recurrence.
- The task schema is versioned with SQLite `PRAGMA user_version`; ordered
  upgrades run once and are not reapplied on restart. A database with a newer
  schema version than the app supports is rejected at startup and not modified.
- Task creation timestamps are stored as UTC. Due dates are calendar dates,
  not timezone-dependent timestamps.

### Operational notes

- The default listener is localhost-only. The application does not include
  authentication; do not expose it to the public internet.
- The application is intended for one server. It does not provide background
  task scheduling or push updates between browsers; refresh to load changes
  made elsewhere.
- Concurrent edits replace the full task, so the last save wins.
- .NET 10 is required. Automated coverage includes API integration and browser
  interaction tests.
