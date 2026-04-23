# Copilot Studio Agents

The solution uses three Copilot Studio agents. All are included in the solution package and are invoked exclusively from Power Automate flows — they are not directly accessible to end users.

---

## Coaching Copilot

**Schema name**: `alex_coaching_copilot`

**Purpose**: Generates AI coaching suggestions for a specific questionnaire category, based on the manager's evaluation notes and agent responses.

**Invoked by**: `RunCoachingCopilot` flow — triggered when an `alex_mcsaiinsight` record (type 2) is created or re-triggered.

**Input**: Structured text containing the agent's evaluation responses for a single category, the category context, and any existing manager notes.

**Output**: Coaching suggestions written back to `alex_mcsaiinsight.alex_outputtext`.

---

## Action Plan Extractor

**Schema name**: `alex_action_plan_extractor`

**Purpose**: Produces a consolidated AI summary of all evaluations in scope for the review period. Used to pre-populate the consolidated summary section in Phase 1 of the review session.

**Invoked by**: `RunActionPlanExtractor` flow — triggered when a new `alex_managereview` record is created.

**Input**: Bullet list of evaluation GUIDs (pulled from `alex_managereview.alex_evaluationids`).

**Output**: Summary text written back to the associated `alex_mcsaiinsight` record (type 1).

---

## Dynamics QA Performance Assistant

**Schema name**: `alex_elevate_qa`

**Purpose**: Generates a comprehensive, document-quality agent improvement plan covering performance analysis, coaching recommendations, and development goals — based on the full set of evaluations for the review period.

**Invoked by**: `RunMCSImprovementPlan` flow — triggered when a new `alex_mcsimprovementplan` record is created.

**Input**
| Parameter | Source field |
|---|---|
| Output language | `alex_mcsimprovementplan.alex_userlanguage` |
| Evaluation GUIDs | `alex_mcsimprovementplan.alex_evaluationids` |

**Output**: Markdown-formatted improvement plan written to `alex_mcsimprovementplan.alex_reportcontent`. The plan is rendered in the UI using an inline Markdown-to-HTML renderer.
