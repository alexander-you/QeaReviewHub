# Data Model

All custom tables use the `alex_` publisher prefix. Standard Dataverse system fields (`createdby`, `createdon`, `modifiedby`, `modifiedon`, `ownerid`, `statecode`, `statuscode`) are omitted from the field tables below.

---

## Table: alex_managereview — Manager Review

The central record for a single coaching session. Created as a draft at the start of Phase 1 and finalised (status = Completed) when the manager submits at Phase 5.

**Relationships**
- `alex_agent` → `systemuser` (the agent being reviewed)
- `alex_reviewer` → `systemuser` (the reviewing manager)
- `alex_evaluation` → `msdyn_conversationevaluation` (the primary evaluation that opened this session)
- `alex_evaluationextension` → `msdyn_evaluationextension`
- Child: `alex_managereviewline` (one per questionnaire/category)
- Child: `alex_reviewactionitemid` (action items committed during the session)
- Child: `alex_mcsaiinsight` (AI insight records — consolidated summary and coaching plan)
- Child: `alex_mcsimprovementplan` (improvement plan generated at session end)

| Field | Type | Description |
|---|---|---|
| `alex_managereviewid` | PK | Record identifier |
| `alex_name` | String | Auto-generated name label |
| `alex_agent` | Lookup (systemuser) | The agent being reviewed |
| `alex_reviewer` | Lookup (systemuser) | The manager conducting the review |
| `alex_evaluation` | Lookup (msdyn_conversationevaluation) | Primary evaluation for this session |
| `alex_evaluationextension` | Lookup (msdyn_evaluationextension) | Extension record of the primary evaluation |
| `alex_evaluationids` | Text | Newline-separated list of evaluation GUIDs in scope for this review period |
| `alex_criteriaid` | String | Questionnaire/criteria ID (GUID as string) used to scope the session |
| `alex_status` | Option Set | Draft (100000000) · In Progress (100000001) · Completed (100000002) · Expired (100000003) |
| `alex_reviewtype` | Option Set | Review type classification |
| `alex_sessiondate` | DateTime | Date the review session took place |
| `alex_periodstart` | DateTime | Start of the evaluation period |
| `alex_periodend` | String | End of the evaluation period ⚠️ *see note below* |
| `alex_expiration_date` | DateTime | Date after which a draft review is auto-expired |
| `alex_overallscore` | Decimal | Weighted composite score calculated at submission |
| `alex_categoryreviewjson` | Text (Long) | JSON snapshot of all category scores and notes |
| `alex_weightoverrides` | Text (Long) | JSON of per-category weight adjustments made during the session |
| `alex_prepstrengths` | Text (Long) | Pre-session coaching notes: strengths |
| `alex_prepimprovements` | Text (Long) | Pre-session coaching notes: improvement areas |
| `alex_preptalkingpoints` | Text (Long) | Pre-session coaching notes: talking points |
| `alex_openingresponse` | String | Manager's opening response text |
| `alex_agentcomments` | String | Agent's comments captured during the session |
| `alex_managersummary` | String | Manager's overall session summary |
| `alex_userlanguage` | String | Display name of the user's UI language (e.g. "Hebrew") — passed to AI to control output language |

> ⚠️ **Field inconsistency — `alex_periodend`**: This field is typed as `nvarchar` (string) while `alex_periodstart` is `datetime`. Consider aligning both to `datetime` for consistency with `alex_mcsimprovementplan` where both fields are `datetime`.

> ℹ️ **`alex_reviewtype`**: Exists in the schema and is included in the session payload, but is not actively rendered or filtered in the current UI. Review whether this field is still needed.

---

## Table: alex_managereviewline — Review Line

One record per questionnaire/category in a review. Written by `SaveManagerReview` and read back by `GetManagerReview` for the read-only viewer.

**Relationships**
- `alex_managereview` → `alex_managereview`

| Field | Type | Description |
|---|---|---|
| `alex_managereviewlineid` | PK | Record identifier |
| `alex_name` | String | Category name label |
| `alex_managereview` | Lookup (alex_managereview) | Parent review |
| `alex_categoryid` | String | Category identifier (GUID as string) |
| `alex_categoryname` | String | Display name of the questionnaire/category |
| `alex_categoryweight` | Integer | Weight assigned to this category (%) |
| `alex_categoryscore` | Decimal | Score for this category |
| `alex_qearesponsejson` | Text (Long) | Raw QEA evaluator response JSON |
| `alex_evaluatorresponsejson` | Text (Long) | Evaluator-specific response detail JSON |
| `alex_agentresponse` | Text (Long) | Agent's response to this category |
| `alex_managerassessment` | Text (Long) | Manager's assessment notes for this category |
| `alex_sortorder` | Integer | Display order |

---

## Table: alex_mcsaiinsight — MCS AI Insight

Stores a single AI generation request and its result. Two types are used:
- **Type 1** (Consolidated Summary) — created automatically at the start of Phase 1 to summarise all evaluations in scope.
- **Type 2** (Coaching Plan) — created on demand when the manager requests AI coaching suggestions.

A Power Automate flow triggers on creation (or when `alex_try_again` is toggled) and calls the appropriate Copilot Studio agent.

**Relationships**
- `alex_agent` → `systemuser`
- `alex_managereviewid` → `alex_managereview`
- `alex_evaluationcriteria` → `msdyn_evaluationcriteria`

| Field | Type | Description |
|---|---|---|
| `alex_mcsaiinsightid` | PK | Record identifier |
| `alex_name` | String | Auto-generated name label |
| `alex_agent` | Lookup (systemuser) | Agent the insight relates to |
| `alex_managereviewid` | Lookup (alex_managereview) | Parent review |
| `alex_evaluationcriteria` | Lookup (msdyn_evaluationcriteria) | Questionnaire scope (type 2 only) |
| `alex_insighttype` | Option Set | 1 = Consolidated Summary · 2 = Coaching Plan |
| `alex_inputtext` | Text (Long) | Structured input passed to the Copilot Studio agent |
| `alex_outputtext` | Text (Long) | AI-generated output returned by the agent |
| `alex_try_again` | Boolean | Set to `true` by the UI to trigger a re-run; the flow sets it back to `false` after processing |
| `alex_userlanguage` | String | UI language display name — controls AI output language |
| `alex_periodstart` | DateTime | Start of the evaluation period for this insight |
| `alex_periodend` | DateTime | End of the evaluation period for this insight |
| `alex_mcs_conversation_id` | String | Copilot Studio conversation ID (set by flow; informational only) |

---

## Table: alex_mcsimprovementplan — MCS Improvement Plan

One record per improvement plan generation request. Created by the UI when the manager clicks **Ask Copilot to generate an Improvement Plan**. A Power Automate flow triggers on creation and calls the Dynamics QA Performance Assistant agent.

**Relationships**
- `alex_agent` → `systemuser`
- `alex_managereview` → `alex_managereview`

| Field | Type | Description |
|---|---|---|
| `alex_mcsimprovementplanid` | PK | Record identifier |
| `alex_name` | String | Auto-generated name label |
| `alex_agent` | Lookup (systemuser) | Agent the plan is for |
| `alex_managereview` | Lookup (alex_managereview) | Parent review |
| `alex_status` | Option Set | 1 = Pending · 2 = Completed · 3 = Failed |
| `alex_reportcontent` | Text (Long) | Markdown-formatted improvement plan produced by the agent |
| `alex_evaluationids` | Text (Long) | Newline-separated list of evaluation GUIDs passed to the agent |
| `alex_evaluationcontext` | Text (Long) | Pre-compiled plain-text summary of all evaluation data, passed as context to the agent |
| `alex_userlanguage` | String | UI language — controls the language of the generated plan |
| `alex_periodstart` | DateTime | Start of the evaluation period |
| `alex_periodend` | DateTime | End of the evaluation period |
| `alex_notify_by_teams` | Boolean | If `true`, the flow sends a Teams message to the manager when the plan is ready |
| `alex_notify_by_email` | Boolean | If `true`, the flow emails the formatted plan to the manager when ready |
| `alex_errormessage` | Text (Long) | Error details if `alex_status = 3` (Failed) |
| `alex_conversation_id` | String | Copilot Studio conversation ID (set by flow; informational only) |

---

## Table: alex_questionnairweight — Questionnaire Weight Configuration

Configuration table that maps questionnaires (evaluation criteria) to scoring weights and priority multipliers. Read by `GetConsolidatedEvaluations` to compute the weighted composite score.

| Field | Type | Description |
|---|---|---|
| `alex_questionnairweightid` | PK | Record identifier |
| `alex_name` | String | Configuration name |
| `alex_evaluationcriteria` | Lookup (msdyn_evaluationcriteria) | The questionnaire this weight applies to |
| `alex_weight` | Decimal | Base weight for score aggregation |
| `alex_prioritymultiplier` | Decimal | Multiplier applied on top of the base weight |
| `alex_isactive` | Boolean | Only active records (`true`) are used in score calculation |

---

## Table: alex_reviewactionitemid — Review Action Item

Action items committed during a review session. Created and deleted as part of the `SaveManagerReview` transaction. Read back by `GetManagerReview` for the viewer.

**Relationships**
- `alex_agent` → `systemuser`
- `alex_managereview` → `alex_managereview`

| Field | Type | Description |
|---|---|---|
| `alex_reviewactionitemidid` | PK | Record identifier |
| `alex_name` | String | Action item description |
| `alex_managereview` | Lookup (alex_managereview) | Parent review |
| `alex_agent` | Lookup (systemuser) | Agent the action item is assigned to |
| `alex_status` | Option Set | Open (100000000) · In Progress (100000001) · Completed (100000002) |
| `alex_is_ignored` | Boolean | Marks items the manager chose to exclude |
| `alex_sortorder` | Integer | Display order |

> ⚠️ **Schema gap**: The `SaveManagerReview` plugin writes `alex_categoryid` (string) and `alex_duedate` (datetime) to action item records, and `GetManagerReview` reads them back. Neither field appears in the current solution schema. Verify that these fields exist in your environment or remove the references from the plugin code if they are no longer needed.

---

## Relationships summary

```
msdyn_conversationevaluation ──► alex_managereview ──┬──► alex_managereviewline
msdyn_evaluationcriteria                             ├──► alex_reviewactionitemid
alex_questionnairweight                              ├──► alex_mcsaiinsight
                                                     └──► alex_mcsimprovementplan
systemuser (agent) ─────────────────────────────────┘
```
