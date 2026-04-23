# Architecture

## Overview

QEA Review Hub extends Dynamics 365 Customer Service with a structured, AI-assisted coaching workflow. A manager selects an agent, picks a review period, reviews consolidated evaluation data, writes coaching notes, and — at the end of the session — triggers a Copilot Studio agent to produce a personalised improvement plan.

All user interaction happens inside a single HTML web resource embedded in D365. Backend logic is split between C# Custom API plugins (synchronous data access) and Power Automate flows (asynchronous AI orchestration).

---

## Component map

```
┌─────────────────────────────────────────────────────────────────┐
│  Dynamics 365 Customer Service                                  │
│                                                                 │
│  ┌─────────────────────┐   Evaluated by QEA    ┌────────────┐  │
│  │ msdyn_evaluation    │ ◄───────────────────► │  Agent     │  │
│  │ msdyn_evaluationext │                        │ (systemuser│  │
│  └─────────────────────┘                        └────────────┘  │
│             │                                        ▲          │
│             ▼                                        │          │
│  ┌────────────────────────────────────────────────┐ │          │
│  │         HTML Web Resource (SPA)                │ │          │
│  │  Phase 0  →  Phase 1  →  ...  →  Phase 5       │─┘          │
│  │  Pick agent   Review     Coaching   AI Plan     │            │
│  └────────────────┬───────────────────────────────┘            │
│                   │ Custom API calls                            │
│                   ▼                                             │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  C# Custom API Plugins                                   │  │
│  │  GetMyAgents · GetAgentEvaluations                       │  │
│  │  GetConsolidatedEvaluations · SaveManagerReview          │  │
│  │  GetManagerReview                                        │  │
│  └──────────────┬───────────────────────────────────────────┘  │
│                 │ Dataverse OData / Custom API                  │
│                 ▼                                               │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  Dataverse Tables                                        │  │
│  │  alex_managereview · alex_managereviewline               │  │
│  │  alex_mcsaiinsight · alex_mcsimprovementplan             │  │
│  │  alex_reviewactionitemid · alex_questionnairweight       │  │
│  └──────────────┬───────────────────────────────────────────┘  │
│                 │ Row-change triggers                           │
│                 ▼                                               │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  Power Automate Flows                                    │  │
│  │  RunCoachingCopilot · RunActionPlanExtractor             │  │
│  │  RunMCSImprovementPlan                                   │  │
│  │  MarkManagerReviewExpired · DeleteExpiredManageReview    │  │
│  └──────────────┬───────────────────────────────────────────┘  │
│                 │ Copilot Studio SDK                            │
│                 ▼                                               │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  Copilot Studio Agents                                   │  │
│  │  Coaching Copilot · Action Plan Extractor                │  │
│  │  Dynamics QA Performance Assistant (Improvement Plan)    │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

---

## End-to-end data flow

### Review session (synchronous)

1. Manager opens the web resource inside D365.
2. **Phase 0** — `GetMyAgents` plugin returns agents the manager is responsible for.
3. **Phase 0** — Manager picks an agent and a date range. `GetConsolidatedEvaluations` plugin returns aggregated scores and questionnaire data for that period.
4. **Phase 1–4** — Manager works through coaching categories. A draft `alex_managereview` record is created/updated via direct OData calls. At the start of Phase 1, the `alex_mcsaiinsight` record (type 1 = consolidated summary) is created, which triggers **RunActionPlanExtractor**.
5. **Phase 1** — Manager can request AI coaching suggestions. The `alex_mcsaiinsight` record (type 2 = coaching plan) is created, which triggers **RunCoachingCopilot**.
6. **Phase 5** — Manager finalises and submits the review via `SaveManagerReview`.

### AI improvement plan (asynchronous)

1. Manager clicks **Ask Copilot to generate an Improvement Plan** on Phase 5.
2. The UI creates an `alex_mcsimprovementplan` record with status = Pending (1).
3. **RunMCSImprovementPlan** flow triggers on record creation.
4. Flow calls the **Dynamics QA Performance Assistant** Copilot Studio agent with the evaluation GUIDs and language.
5. On completion, the flow updates the record: `alex_status = 2` (Completed), `alex_reportcontent` = markdown report.
6. If `alex_notify_by_teams = true`, flow posts a Teams message to the manager. If `alex_notify_by_email = true`, flow runs an AI prompt to format the report and sends it by email.
7. The UI polls the record every 3 seconds (up to 5 minutes) and renders the report when status = 2.

### Draft expiry (scheduled)

- **MarkManagerReviewExpired** runs on a schedule and marks overdue draft reviews as Expired.
- **DeleteExpiredManageReview** runs on a schedule and permanently deletes expired review records and their child records.

---

## External dependencies

| System | Used for |
|---|---|
| `msdyn_conversationevaluation` | Source evaluations scored by QEA |
| `msdyn_evaluationextension` | Extended agent/evaluator response JSON per evaluation |
| `msdyn_evaluationcriteria` | Questionnaire definitions (names, IDs) |
| Microsoft Teams | Improvement plan ready notification (optional) |
| Office 365 / Outlook | Email delivery of improvement plan (optional) |
