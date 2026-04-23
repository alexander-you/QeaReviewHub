# Changelog

All notable releases are documented here.

---

## [v1.0.0-pre] — 2026-04-23 · Pre-Release

> ⚠️ This is a pre-release version. It has been tested in a development environment
> and is not yet recommended for production use. Feedback and issue reports are welcome.

### What's included

**Web Resource**
- Single-page coaching session UI with 6 phases (agent selection through session summary) and a read-only review viewer
- Full RTL / Hebrew localisation (RESX — en-US + he-IL)
- Draft auto-save with expiry and resume support

**AI Features**
- Consolidated summary — automatically generated at session start via the Action Plan Extractor Copilot Studio agent
- Per-category coaching suggestions — on demand, via the Coaching Copilot agent
- Improvement Plan — generated on Phase 5 by the Dynamics QA Performance Assistant agent; rendered as a formatted document inside the UI
- Improvement plan notifications: Microsoft Teams and/or Outlook email when the plan is ready
- All AI output language follows the manager's UI language setting

**Power Automate Flows**
- `RunMCSImprovementPlan` — orchestrates improvement plan generation, Teams notification, and email delivery
- `RunCoachingCopilot` — handles per-category coaching suggestion generation
- `RunActionPlanExtractor` — generates the consolidated session summary
- `MarkManagerReviewExpired` — scheduled expiry of stale drafts
- `DeleteExpiredManageReview` — scheduled cleanup of expired records

**C# Custom API Plugins**
- `GetMyAgents` · `GetAgentEvaluations` · `GetConsolidatedEvaluations`
- `SaveManagerReview` · `GetManagerReview`

**Copilot Studio Agents**
- Coaching Copilot · Action Plan Extractor · Dynamics QA Performance Assistant

**Custom Tables**
- `alex_managereview` · `alex_managereviewline` · `alex_mcsaiinsight`
- `alex_mcsimprovementplan` · `alex_reviewactionitemid` · `alex_questionnairweight`

### Known limitations

- `GetMyAgents` resolves the manager–agent relationship via shared queue membership. Organisations using a different model (team hierarchy, custom field, BU) need to update the plugin before deploying.
- `DeleteAllRecords` flow is included for development/testing only — do not activate in production.
- Notification emails use an AI-formatted version of the report. Email template and sender identity should be reviewed before production use.

### Assets

| File | Description |
|---|---|
| `QEAReviewHub_1_0_0_0.zip` | Unmanaged solution — use in development environments |
| `QEAReviewHub_1_0_0_0_managed.zip` | Managed solution — use in production |
