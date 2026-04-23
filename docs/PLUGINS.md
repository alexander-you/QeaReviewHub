# Plugins

Five C# Custom API plugins are included in the assembly `Alex.ReviewSession.Plugins`. All are registered in Sandbox isolation mode, stored in the database.

**Build**
```
cd src
dotnet build -c Release
```
Output: `src/bin/Release/net462/Alex.ReviewSession.Plugins.dll`

---

## GetMyAgents

**Custom API**: `alex_GetMyAgents`
**Class**: `Alex.ReviewSession.Plugins.GetMyAgents`

Returns the list of agents the calling manager is responsible for.

**Request parameters**: None

**Response properties**

| Name | Type | Description |
|---|---|---|
| `AgentsJson` | String | JSON array of `{ id, name, jobTitle, queueId, queueName }` |

**Notes**
- Identifies the manager's agents by matching queue membership between the current user and systemusers with agent roles.
- If your organisation uses a different manager–agent relationship model (e.g. team hierarchy, business unit, custom field), update the query in `GetMyAgents.cs` accordingly.

---

## GetAgentEvaluations

**Custom API**: `alex_GetAgentEvaluations`
**Class**: `Alex.ReviewSession.Plugins.GetAgentEvaluations`

Returns recent evaluations for a specific agent.

**Request parameters**

| Name | Type | Required | Description |
|---|---|---|---|
| `AgentId` | String (GUID) | Yes | `systemuserid` of the agent |
| `Top` | Integer | No | Maximum results to return (default: 10) |

**Response properties**

| Name | Type | Description |
|---|---|---|
| `EvaluationsJson` | String | JSON array of evaluation records including scores, criteria, and extension data |

---

## GetConsolidatedEvaluations

**Custom API**: `alex_GetConsolidatedEvaluations`
**Class**: `Alex.ReviewSession.Plugins.GetConsolidatedEvaluations`

Aggregates evaluations for an agent over a date range, applies questionnaire weights from `alex_questionnairweight`, and returns a weighted composite score.

**Request parameters**

| Name | Type | Required | Description |
|---|---|---|---|
| `AgentId` | String (GUID) | Yes | `systemuserid` of the agent |
| `DateFrom` | String (date) | Yes | Start of evaluation period (ISO 8601) |
| `DateTo` | String (date) | Yes | End of evaluation period (ISO 8601) |

**Response properties**

| Name | Type | Description |
|---|---|---|
| `ConsolidatedJson` | String | JSON object: `{ totalEvaluations, questionnaires[], weightedOverallScore, dateFrom, dateTo }` |

**Notes**
- Queries `msdyn_evaluation` filtered by `msdyn_regardingobjectowner = AgentId` and `statuscode = 700610001` (completed).
- Joins to `msdyn_evaluationextension` for agent/evaluator response JSON.
- Joins to `alex_questionnairweight` for per-criteria weights. Criteria without a configured weight receive no weighting.

---

## SaveManagerReview

**Custom API**: `alex_SaveManagerReview`
**Class**: `Alex.ReviewSession.Plugins.SaveManagerReview`

Creates or updates the complete review record in a single atomic transaction.

**Request parameters**

| Name | Type | Required | Description |
|---|---|---|---|
| `ReviewPayloadJson` | String (JSON) | Yes | Full session payload (see below) |

**Payload schema**

```json
{
  "AgentId": "guid",
  "ReviewId": "guid (optional — if supplied, updates existing draft; if omitted, creates new)",
  "EvaluationId": "guid (required on create)",
  "EvaluationExtensionId": "guid (optional)",
  "OverallScore": 0.0,
  "Status": 100000002,
  "SessionDate": "2025-01-01T00:00:00Z",
  "PeriodStart": "2025-01-01",
  "PeriodEnd": "2025-03-31",
  "OpeningResponse": "...",
  "AgentComments": "...",
  "ManagerSummary": "...",
  "PrepNotes": {
    "Strengths": "...",
    "Improvements": "...",
    "TalkingPoints": "..."
  },
  "CategoryLines": [
    {
      "CategoryId": "guid",
      "CategoryName": "...",
      "CategoryWeight": 25,
      "CategoryScore": 4.5,
      "QeaResponseJson": "...",
      "EvaluatorResponseJson": "...",
      "AgentResponse": "...",
      "ManagerAssessment": "...",
      "SortOrder": 1
    }
  ],
  "ActionItems": [
    {
      "Description": "...",
      "CategoryId": "guid (optional)",
      "DueDate": "2025-06-01 (optional)",
      "SortOrder": 1
    }
  ]
}
```

**Response properties**

| Name | Type | Description |
|---|---|---|
| `ReviewId` | String (GUID) | ID of the created or updated review |
| `Success` | Boolean | `true` on success |
| `ErrorMessage` | String | Error detail on failure |

**Behaviour**
- If `ReviewId` is supplied: updates header fields and replaces all action items. Does not recreate category lines on update.
- If `ReviewId` is omitted: creates header, category lines, and action items in one `ExecuteTransactionRequest`.
- Prevents duplicate completed reviews for the same evaluation.

---

## GetManagerReview

**Custom API**: `alex_GetManagerReview`
**Class**: `Alex.ReviewSession.Plugins.GetManagerReview`

Retrieves a complete saved review for display in the read-only viewer (Phase 6).

**Request parameters**

| Name | Type | Required | Description |
|---|---|---|---|
| `ReviewId` | String (GUID) | Yes | `alex_managereviewid` |

**Response properties**

| Name | Type | Description |
|---|---|---|
| `ReviewJson` | String | JSON object containing header fields, `categoryFeedback[]`, and `actionItems[]` |

---

## Registration reference

After building and uploading the assembly via Plugin Registration Tool, link each plugin class to its Custom API record:

| Custom API | Plugin class |
|---|---|
| `alex_GetMyAgents` | `Alex.ReviewSession.Plugins.GetMyAgents` |
| `alex_GetAgentEvaluations` | `Alex.ReviewSession.Plugins.GetAgentEvaluations` |
| `alex_GetConsolidatedEvaluations` | `Alex.ReviewSession.Plugins.GetConsolidatedEvaluations` |
| `alex_SaveManagerReview` | `Alex.ReviewSession.Plugins.SaveManagerReview` |
| `alex_GetManagerReview` | `Alex.ReviewSession.Plugins.GetManagerReview` |

All Custom APIs are synchronous. There are no step registrations — the plugin runs as the Custom API's main operation.
