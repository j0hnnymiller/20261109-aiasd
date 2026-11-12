---
ai_generated: true
model: "OpenAI/unknown@unknown"
operator: "jMM-at-CODETraining"
chat_id: "split-task-api-tests-by-concern-20261112"
prompt: |
  Split task API integration tests into focused test groups while preserving
  their current behavioral coverage and shared test setup.

  TaskApiTests.cs covers API contracts, persistence across host restarts,
  validation, recurrence, concurrency, rollback, and legacy database upgrades.
  The broad scenarios make failures harder to localize.

  Expected outcome: Test files and classes have focused responsibilities and
  retain existing integration coverage.

  Acceptance criteria:
  - Test classes/files have clear responsibility boundaries.
  - Existing assertions and scenarios retain equivalent behavior coverage.
  - Restart persistence, recurring-task atomicity, concurrency, and legacy
    database compatibility remain covered.
  - The complete .NET test suite passes.

  Keep the existing isolated database and host setup. Do not add a general
  testing framework.
started: "2026-11-12T20:02:42Z"
ended: "2026-11-12T20:05:43Z"
task_durations:
  - task: "Split API tests and centralize shared setup"
    duration: "00:03:01"
total_duration: "00:03:01"
ai_log: "ai-logs/2026/11/12/split-task-api-tests-by-concern-20261112/conversation.md"
source: "GitHub issue #9; tests/TodoManager.Tests/TaskRecurrenceApiTests.cs"
---

# Recurrence test provenance

[TaskRecurrenceApiTests.cs](./TaskRecurrenceApiTests.cs) - recurrence dates,
successors, concurrency, and rollback coverage.
