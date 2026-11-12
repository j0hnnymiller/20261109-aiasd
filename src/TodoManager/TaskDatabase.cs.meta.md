---
ai_generated: true
model: "Anthropic/claude@unknown"
operator: "jMM-at-CODETraining"
chat_id: "version-task-schema-upgrades-20261112"
prompt: |
  implement github issue 10
started: "2026-11-12T12:57:30.233-08:00"
ended: "2026-11-12T13:01:33.3860566-08:00"
task_durations:
  - task: "Implement and test versioned task-schema upgrades; update docs"
    duration: "00:04:00"
total_duration: "00:04:00"
ai_log: "ai-logs/2026/11/12/version-task-schema-upgrades-20261112/conversation.md"
source: "user; GitHub issue #10"
---
# Task database change provenance

[TaskDatabase.cs](./TaskDatabase.cs) versions the task schema with
`PRAGMA user_version` and applies ordered upgrades once, in one transaction.
