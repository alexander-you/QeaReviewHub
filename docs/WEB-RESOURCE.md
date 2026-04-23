# Web Resource

The entire UI is a single HTML file (`webresources/html/Agent review session V2.html`) embedded as a Dynamics 365 web resource. It is a self-contained single-page application with no external JavaScript dependencies.

**Logical name in D365**: `alex_agent_review_session_V2`

---

## Session phases

| Phase | ID | Description |
|---|---|---|
| 0 | `ph0` | Agent and period selection. Loads agents via `GetMyAgents` and consolidated scores via `GetConsolidatedEvaluations`. |
| 1 | `ph1` | Category review. Displays per-questionnaire scores, agent/evaluator responses. Triggers consolidated summary and coaching plan AI insights. |
| 2 | `ph2` | Prep notes. Manager enters coaching strengths, improvement areas, and talking points. |
| 3 | `ph3` | Session conversation. Opening response, agent comments. |
| 4 | `ph4` | Action items. Manager adds/removes/edits commitments. |
| 5 | `ph5` | Session summary and AI improvement plan generation. Review is finalised here. |
| 6 | `ph6` | Read-only viewer. Displays a saved review retrieved via `GetManagerReview`. |

---

## Key behaviours

**Draft auto-save**: A draft `alex_managereview` record is created via direct OData at the start of Phase 1. The draft has an expiration date; expired drafts are cleaned up by the `MarkManagerReviewExpired` and `DeleteExpiredManageReview` flows. When a user returns to Phase 5 of an existing session, the draft is detected and resumed.

**AI insights**: Two types of `alex_mcsaiinsight` records are created by the UI:
- Type 1 (consolidated summary) — created automatically when a new review begins.
- Type 2 (coaching plan) — created on demand per category.

Both trigger asynchronous Power Automate flows. The UI polls the record's `statuscode` every 3 seconds.

**Improvement plan**: An `alex_mcsimprovementplan` record is created when the manager clicks the generate button. The UI polls `alex_status` every 3 seconds for up to 5 minutes. If the record already exists and is still pending, polling resumes on the same record rather than creating a new one. A new record is only created if the previous attempt failed (status = 3).

**Markdown rendering**: The improvement plan report is stored as Markdown and rendered to HTML by an inline renderer (`renderMarkdown()`). No external library is used.

---

## Localisation (RESX)

String resources are stored in two RESX files:

| File | Language | LCID | D365 logical name |
|---|---|---|---|
| `ReviewStrings.1033.resx` | English (en-US) | 1033 | `alex_ReviewStrings.1033` |
| `ReviewStrings.1037.resx` | Hebrew (he-IL) | 1037 | `alex_ReviewStrings.1037` |

Strings are loaded at runtime via `Xrm.Utility.getResourceString`. The UI detects the user's D365 language setting and falls back gracefully to English if a key is missing.

To add a new localised string:
1. Add the key/value pair to both RESX files.
2. Use `L('KeyName', 'fallback text')` in the HTML/JS.

Key naming convention: `ReviewSession_[Phase]_[Section]_[Purpose]`

---

## Adding the web resource to a form

1. In D365, open the target form in the form editor.
2. Insert a **Web Resource** control.
3. Select `alex_agent_review_session_V2`.
4. Set the height to fill the available space (recommended: 800–1000 px minimum).
5. Pass the required parameters if needed (e.g. `recordId`, `recordType`).
