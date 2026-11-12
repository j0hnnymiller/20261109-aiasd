---
ai_generated: true
model: "OpenAI/unknown@unknown"
operator: "jMM-at-CODETraining"
chat_id: "refactoring-finding-issues-20261112"
prompt: |
  create issues for the findings and put them in the issues folder as markdown files
started: "2026-11-12T11:14:00.289-08:00"
ended: "2026-11-12T11:17:21.225-08:00"
task_durations:
  - task: "Document unresolved refactoring findings"
    duration: "00:03:21"
total_duration: "00:03:21"
ai_log: "../ai-logs/2026/11/12/refactoring-finding-issues-20261112/conversation.md"
source: "user; src/TodoManager/docs/refactoring-plan.md"
revisions:
  - chat_id: "prioritize-local-issues-20261112"
    model: "OpenAI/unknown@unknown"
    date: "2026-11-12"
    change: "Add impact, effort, risk, and recommended priority assessment."
    ai_log: "../ai-logs/2026/11/12/prioritize-local-issues-20261112/conversation.md"
  - chat_id: "issues-conformance-20261112"
    model: "OpenAI/unknown@unknown"
    date: "2026-11-12"
    change: "Restructure the local issue draft to follow GitHub issue authoring guidance and add verified evidence."
    ai_log: "../ai-logs/2026/11/12/issues-conformance-20261112/conversation.md"
  - chat_id: "version-task-schema-upgrades-20261112"
    model: "Anthropic/claude@unknown"
    date: "2026-11-12"
    change: "Mark acceptance criteria complete and link the implementation."
    ai_log: "../ai-logs/2026/11/12/version-task-schema-upgrades-20261112/conversation.md"
---

# Ensure task schema upgrades are versioned and preserve existing data

**Finding:** Schema evolution

## Summary

Introduce an explicit version and ordered upgrade path for the task database
schema.

## Context

`TaskDatabase.InitializeAsync` creates the task table, inspects its columns, and
applies conditional `ALTER TABLE` statements at startup. As schema changes
accumulate, manually discovering and sequencing upgrades becomes harder to
verify.

## Assessment

- **Impact:** High - safe upgrades protect existing task data as the schema
  evolves.
- **Effort:** High - add ordered upgrades and test fresh and legacy database
  states, repeated initialization, and data retention.
- **Risk:** High - incorrect upgrade ordering or SQL can prevent startup or lose
  existing data.
- **Recommended priority:** P1 - highest impact and risk; schedule early and
  coordinate the shared database-initialization changes.

## Expected outcome

Fresh databases reach the current schema, and supported existing databases
upgrade safely without losing task data or reapplying completed migrations.

## Acceptance criteria

- [x] The task schema has an explicit version and ordered upgrade path.
- [x] Fresh databases initialize to the current schema.
- [x] Supported existing databases, including legacy schemas, upgrade without
  data loss.
- [x] Re-running initialization is safe and does not reapply completed upgrades.
- [x] Tests cover each supported starting schema, resulting schema, and retained
  data.

## Resolution

Implemented in [TaskDatabase.cs](../src/TodoManager/TaskDatabase.cs) using
`PRAGMA user_version` and ordered upgrades, verified by
[TaskDatabaseTests.cs](../tests/TodoManager.Tests/TaskDatabaseTests.cs).

## Evidence

- [TaskDatabase.InitializeAsync](../src/TodoManager/TaskDatabase.cs#L16-L59)
  creates the task table, inspects columns with `PRAGMA table_info`, and applies
  conditional `ALTER TABLE` statements.
- [LegacyDatabaseIsUpgradedWithoutLosingRecords](../tests/TodoManager.Tests/TaskApiTests.cs#L324-L354)
  verifies an existing legacy database is upgraded while retaining its record.

## Dependencies and scope

- No hard prerequisite. Coordinate changes to `TaskDatabase.cs` with
  [006](006-null-forgiving-operators.md) and
  [007](007-database-initialization-cancellation.md), which touch the same
  initialization path.
- [009](009-split-task-api-tests.md) is useful test preparation, not a blocker.
- Preserve task records and existing API behavior; introduce only the
  versioning and upgrade behavior needed for the task schema.
