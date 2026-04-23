# Power Automate Flows

The solution includes six flows. Three are AI orchestration flows triggered by Dataverse row changes; three are maintenance flows.

---

## AI Orchestration Flows

### RunMCSImprovementPlan

**Trigger**: When `alex_mcsimprovementplan` row is **created** (scope: organisation)

**Purpose**: Calls the Dynamics QA Performance Assistant agent to generate the improvement plan, then writes the result back to the record and optionally notifies the manager.

**Connections required**
| Connection | API |
|---|---|
| `alex_qea_review_hub_dv` | Dataverse |
| `alex_qea_review_hub_mcs` | Microsoft Copilot Studio |
| `alex_teams_qea_review_hub` | Microsoft Teams |
| `alex_sharedoffice365_0518f` | Office 365 Outlook |

**Steps**

1. **Get full record** — Retrieve the newly created `alex_mcsimprovementplan` record.
2. **Execute Agent and wait** — Call the `alex_elevate_qa` (Dynamics QA Performance Assistant) agent with:
   - Output language: `alex_userlanguage`
   - Evaluation GUIDs: `alex_evaluationids`
3. **Update record** — Write `alex_reportcontent` = agent response, `alex_status` = 2 (Completed).
4. **Re-read record** — Fetch the record again to confirm status = 2.
5. **Check status** — If status is 2 (Completed):
   - **Teams branch** — If `alex_notify_by_teams = true`: look up manager email → post Teams message.
   - **Email branch** — If `alex_notify_by_email = true`: look up manager email → run AI prompt to format report → send Outlook email.

---

### RunCoachingCopilot

**Trigger**: When `alex_mcsaiinsight` row is **modified**, filtered on `alex_try_again` attribute and `statuscode = 416780001`

**Purpose**: Calls the Coaching Copilot agent to produce AI coaching suggestions for a single questionnaire/category.

**Connections required**
| Connection | API |
|---|---|
| `alex_qea_review_hub_mcs` | Microsoft Copilot Studio |
| `alex_qea_review_hub_dv` | Dataverse |

**Steps**

1. **Execute Agent and wait** — Call the `alex_coaching_copilot` agent with the insight's input text.
2. **Get record** — Retrieve the `alex_mcsaiinsight` record.
3. **Update record (Processing)** — Write the agent response to `alex_outputtext`.
4. **Check for response** — Condition verifying a non-empty response was returned; updates record status accordingly.

---

### RunActionPlanExtractor

**Trigger**: When `alex_managereview` row is **created** (scope: organisation)

**Purpose**: Calls the Action Plan Extractor agent to generate a consolidated AI summary at the start of a new review session (used to populate Phase 1 of the UI).

**Connections required**
| Connection | API |
|---|---|
| `alex_qea_review_hub_dv` | Dataverse |
| `alex_qea_review_hub_mcs` | Microsoft Copilot Studio |

**Steps**

1. **Get record** — Retrieve `alex_evaluationids` from the new `alex_managereview` record.
2. **Compose message** — Build the structured input: parse evaluation GUIDs into a JSON array, then format as a bullet list.
3. **Execute Agent and wait** — Call the `alex_action_plan_extractor` agent with the formatted evaluation IDs.

---

## Maintenance Flows

### MarkManagerReviewExpired

**Trigger**: Recurrence (scheduled)

**Purpose**: Finds draft reviews past their `alex_expiration_date` and sets their status to Expired.

**Steps**

1. **List rows** — Query `alex_managereview` where status = Draft and `alex_expiration_date` < now.
2. **Apply to each** — Update each record: `alex_status` = Expired.

---

### DeleteExpiredManageReview

**Trigger**: Recurrence (scheduled)

**Purpose**: Permanently deletes expired review records and their associated child records.

**Steps**

1. **List rows** — Query expired `alex_managereview` records.
2. **For each** — Delete each record (Dataverse cascades to child records if configured).

---

### DeleteAllRecords *(development/testing utility)*

**Trigger**: Manual

**Purpose**: Bulk-deletes all records from `alex_managereview`, `alex_mcsaiinsight`, and `alex_reviewactionitemid`. **Do not activate in production.**

**Steps**

1. List all Manager Review rows → delete each.
2. List all MCS AI Insight rows → delete each.
3. List all Review Action Item rows → delete each.

---

## Connection References

The following connection references must be configured after solution import:

| Logical name | Service | Used by |
|---|---|---|
| `alex_qea_review_hub_dv` | Dataverse | All flows |
| `alex_qea_review_hub_mcs` | Microsoft Copilot Studio | RunMCSImprovementPlan, RunCoachingCopilot, RunActionPlanExtractor |
| `alex_teams_qea_review_hub` | Microsoft Teams | RunMCSImprovementPlan |
| `alex_sharedoffice365_0518f` | Office 365 Outlook | RunMCSImprovementPlan |
