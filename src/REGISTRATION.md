# Plugin Registration Guide
## Alex.ReviewSession.Plugins

### Prerequisites
1. Generate a strong name key: `sn -k key.snk` (place in project root)
2. Build: `dotnet build -c Release`
3. Register assembly via Plugin Registration Tool (PRT)

---

### Assembly Registration
- **Assembly**: `Alex.ReviewSession.Plugins.dll`
- **Isolation Mode**: Sandbox
- **Location**: Database

---

### Step Registration

Since these are Custom API plugins (not traditional event-based plugins),
registration is done by linking each plugin class to its Custom API record.

#### 1. alex_GetMyAgents

| Field | Value |
|-------|-------|
| Plugin Type | `Alex.ReviewSession.Plugins.GetMyAgents` |
| Custom API | `alex_GetMyAgents` |
| Stage | N/A (Custom API — runs as the main operation) |
| Execution Mode | Synchronous |

**In the Custom API record (Dataverse):**
- Set the **Plugin Type** lookup to `Alex.ReviewSession.Plugins.GetMyAgents`

**Response Property to register:**
| Name | Type | Is Optional |
|------|------|-------------|
| AgentsJson | String | No |

---

#### 2. alex_GetAgentEvaluations

| Field | Value |
|-------|-------|
| Plugin Type | `Alex.ReviewSession.Plugins.GetAgentEvaluations` |
| Custom API | `alex_GetAgentEvaluations` |
| Stage | N/A (Custom API) |
| Execution Mode | Synchronous |

**Request Parameters to register:**
| Name | Type | Is Optional | Description |
|------|------|-------------|-------------|
| AgentId | String | No | systemuserid GUID |
| Top | Integer | Yes | Max results (default 10) |

**Response Property to register:**
| Name | Type | Is Optional |
|------|------|-------------|
| EvaluationsJson | String | No |

---

#### 3. alex_SaveManagerReview

| Field | Value |
|-------|-------|
| Plugin Type | `Alex.ReviewSession.Plugins.SaveManagerReview` |
| Custom API | `alex_SaveManagerReview` |
| Stage | N/A (Custom API) |
| Execution Mode | Synchronous |

**Request Parameters to register:**
| Name | Type | Is Optional | Description |
|------|------|-------------|-------------|
| ReviewPayloadJson | String | No | Full session JSON |

**Response Properties to register:**
| Name | Type | Is Optional |
|------|------|-------------|
| ReviewId | String | No |
| Success | Boolean | No |
| ErrorMessage | String | Yes |

---

### Things to Verify in Your Environment

1. **Agent lookup field on msdyn_conversationevaluation**
   - The `GetAgentEvaluations` plugin filters by `msdyn_agent`
   - Check your entity metadata — the field might be named differently
     (e.g., `msdyn_agentid`, `ownerid`, or a custom field)
   - Update the QueryExpression condition accordingly

2. **msdyn_evaluationextension relationship**
   - The plugin queries by `msdyn_evaluation` lookup
   - Verify this is the correct relationship field name in your org

3. **Queue membership pattern**
   - `GetMyAgents` assumes the manager is a member of the same queues as agents
   - If managers are NOT in queues, you'll need an alternative:
     - Query by team membership
     - Use a custom "manager team" configuration table
     - Filter by business unit hierarchy

4. **System.Text.Json compatibility**
   - The plugins use System.Text.Json 6.0.x (compatible with .NET Framework 4.6.2)
   - If you encounter sandbox issues with this package, fall back to
     Newtonsoft.Json (included in the sandbox by default) or manual JSON building

---

### Testing Sequence

1. Deploy assembly → verify it shows in PRT
2. Link `GetMyAgents` → test via browser:
   ```
   GET [org]/api/data/v9.2/alex_GetMyAgents
   ```
3. Link `GetAgentEvaluations` → test:
   ```
   POST [org]/api/data/v9.2/alex_GetAgentEvaluations
   { "AgentId": "guid-here" }
   ```
4. Link `SaveManagerReview` → test with the payload from the technical design
5. Wire the web resource to call these APIs instead of mock data
