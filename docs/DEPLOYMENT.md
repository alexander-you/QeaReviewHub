# Deployment Guide

This guide covers deploying QEA Review Hub to a Dynamics 365 Customer Service environment from scratch.

---

## Prerequisites

Before you begin, confirm the following are in place:

- [ ] Dynamics 365 Customer Service with Omnichannel and Quality Evaluation (QEA) module enabled
- [ ] At least one `msdyn_evaluationcriteria` (questionnaire) configured with completed evaluations
- [ ] Power Platform System Administrator or System Customizer role
- [ ] Plugin Registration Tool installed
- [ ] .NET 4.6.2 SDK (to rebuild the plugin from source if needed)

---

## Step 1 — Import the solution

1. Navigate to **Power Apps** → your target environment → **Solutions**.
2. Click **Import solution** and select the appropriate ZIP:
   - `QEAReviewHub_1_0_0_0_managed.zip` for production
   - `QEAReviewHub_1_0_0_0.zip` for development/test
3. On the **Connection References** screen, create or assign connections for each reference:

   | Reference | Service |
   |---|---|
   | `alex_qea_review_hub_dv` | Dataverse |
   | `alex_qea_review_hub_mcs` | Microsoft Copilot Studio |
   | `alex_teams_qea_review_hub` | Microsoft Teams |
   | `alex_sharedoffice365_0518f` | Office 365 Outlook |

4. Complete the import. Resolve any warnings.

---

## Step 2 — Configure Questionnaire Weights

Populate `alex_questionnairweight` with one record per `msdyn_evaluationcriteria` (questionnaire):

| Field | Value |
|---|---|
| `alex_evaluationcriteria` | Lookup to the questionnaire |
| `alex_weight` | Numeric weight (e.g. 0.4 for 40%) |
| `alex_prioritymultiplier` | Multiplier (1.0 = no adjustment) |
| `alex_isactive` | `true` |

Weights are used by `GetConsolidatedEvaluations` to compute the weighted composite score. Questionnaires without a weight record are excluded from the weighted calculation.

---

## Step 3 — Register the plugin assembly

1. Build the assembly (if rebuilding from source):
   ```
   cd src
   dotnet build -c Release
   ```
2. Open **Plugin Registration Tool** and connect to your environment.
3. Register the assembly `Alex.ReviewSession.Plugins.dll`:
   - **Isolation Mode**: Sandbox
   - **Location**: Database
4. For each Custom API, open its record in Dataverse and set the **Plugin Type** lookup:

   | Custom API | Plugin class |
   |---|---|
   | `alex_GetMyAgents` | `Alex.ReviewSession.Plugins.GetMyAgents` |
   | `alex_GetAgentEvaluations` | `Alex.ReviewSession.Plugins.GetAgentEvaluations` |
   | `alex_GetConsolidatedEvaluations` | `Alex.ReviewSession.Plugins.GetConsolidatedEvaluations` |
   | `alex_SaveManagerReview` | `Alex.ReviewSession.Plugins.SaveManagerReview` |
   | `alex_GetManagerReview` | `Alex.ReviewSession.Plugins.GetManagerReview` |

> The Custom API records are imported with the solution. You only need to link the plugin type — no step registration is required.

---

## Step 4 — Activate flows

After import, flows are off by default. Turn on only the flows relevant to your environment:

| Flow | Activate? |
|---|---|
| `RunMCSImprovementPlan` | ✅ Required |
| `RunCoachingCopilot` | ✅ Required |
| `RunActionPlanExtractor` | ✅ Required |
| `MarkManagerReviewExpired` | ✅ Recommended |
| `DeleteExpiredManageReview` | ✅ Recommended |
| `DeleteAllRecords` | ❌ Development only — do not activate in production |

---

## Step 5 — Publish Copilot Studio agents

1. Open **Copilot Studio** in your environment.
2. For each of the three agents (`alex_coaching_copilot`, `alex_action_plan_extractor`, `alex_elevate_qa`):
   - Verify the agent imported correctly.
   - Click **Publish**.
   - Confirm the Dataverse connection is configured and active.

---

## Step 6 — Add the web resource to a form

1. In D365, open the form where the review tool should appear (e.g. a custom app page or queue item form).
2. Insert the web resource `alex_agent_review_session_V2`.
3. Set an appropriate height (800–1000 px recommended).
4. Save and publish the form.

---

## Step 7 — Verify end-to-end

1. Open the web resource as a manager user.
2. Confirm agents load in Phase 0.
3. Select an agent with existing evaluations and a date range — confirm scores load.
4. Proceed through phases 1–5 and submit a review.
5. On Phase 5, click **Ask Copilot to generate an Improvement Plan** and wait for the result.

---

## Environment-specific adjustments

### Agent–manager relationship
`GetMyAgents` determines the manager's agents by matching queue membership. If your org uses a different model (team hierarchy, custom manager field, business unit), update `GetMyAgents.cs` and rebuild the assembly.

### Evaluation filter
`GetConsolidatedEvaluations` filters evaluations by `statuscode = 700610001` (QEA completed status). Verify this matches the status codes used in your org's QEA configuration.
