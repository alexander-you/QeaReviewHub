# Roadmap

Feature requests, enhancements, and known improvements planned for future releases. Community votes and comments are welcome — use GitHub Discussions or add a 👍 reaction to the relevant issue.

---

## Priority: fix before next release

| # | Item | Detail |
|---|---|---|
| 1 | **Fix `alex_periodend` field type** | Currently `nvarchar` while `alex_periodstart` is `datetime`. Inconsistency affects flows and reporting. |
| 2 | **Add missing action item fields to schema** | `SaveManagerReview` writes `alex_categoryid` and `alex_duedate` to action item records; neither field exists in the current solution schema. |
| 3 | **Flexible manager–agent relationship** | Replace queue-based agent lookup with a configurable option: team hierarchy, custom lookup field, or business unit. Biggest adoption blocker for non-standard org structures. |

---

## Analytics and visibility

| # | Item | Detail |
|---|---|---|
| 4 | **Agent history panel** | Show a summary of previous reviews for the selected agent (score trend, last session date, open action items) at the start of each session. |
| 5 | **Score trending chart** | Per-agent chart of weighted scores across review periods — canvas chart in the web resource or embedded Power BI visual. |
| 6 | **Manager dashboard** | Aggregate view across all agents: pending reviews, declining scores, overdue action items. |
| 7 | **Power BI report pack** | Standard reports shipped with the solution: agent performance over time, questionnaire score distribution, improvement plan completion rates. |

---

## AI enhancements

| # | Item | Detail |
|---|---|---|
| 8 | **Richer improvement plan context** | Pass structured JSON (per-category scores, trend direction, prior action items) to the agent instead of plain text, for more targeted plans. |
| 9 | **Improvement plan follow-up** | Scheduled check-in (e.g. 30 days later) where Copilot assesses progress against the plan based on newer evaluations. |
| 10 | **AI-suggested action items** | After coaching notes are entered, offer Copilot-suggested action items the manager can accept or dismiss with one click. |
| 11 | **Sentiment analysis on agent comments** | Flag sessions where agent tone suggests disengagement or frustration; surface as a subtle indicator for the manager. |

---

## Agent experience

| # | Item | Detail |
|---|---|---|
| 12 | **Agent self-view** | Read-only view (separate web resource or Teams tab) where agents see their own improvement plan and action items — without access to the manager's private coaching notes. |
| 13 | **Action item acknowledgement** | Agents confirm they've seen and accepted action items, creating a simple accountability loop. |

---

## Workflow and lifecycle

| # | Item | Detail |
|---|---|---|
| 14 | **Manager-of-manager approval** | Optional step where a senior manager reviews and approves the coaching summary before finalisation. |
| 15 | **Session scheduling** | Integrate with Outlook Calendar to propose and book the coaching session directly from Phase 5. |
| 16 | **Improvement plan versioning** | Keep a history of regenerated plans rather than replacing; allow managers to compare versions. |
| 17 | **Action item carry-forward** | When starting a new review for the same agent, show open action items from the previous session and allow carry-forward or closure. |

---

## Platform and distribution

| # | Item | Detail |
|---|---|---|
| 18 | **Microsoft Teams app** | Surface the web resource as a personal Teams tab so managers can conduct reviews without opening D365. |
| 19 | **PDF / SharePoint export** | Export the improvement plan as a PDF or push it to a SharePoint document library for record-keeping. |
| 20 | **Weight management UI** | Admin page to manage `alex_questionnairweight` records without needing direct Dataverse table access. |
| 21 | **Additional languages** | The RESX architecture already supports it; Arabic, French, and Spanish are natural next candidates. |

---

## Top picks for next release

Based on impact and effort, the three highest-priority items for the next release are:

1. **#3** — Flexible manager–agent relationship (broadest adoption impact)
2. **#4** — Agent history panel (biggest in-session UX improvement)
3. **#17** — Action item carry-forward (closes the feedback loop that makes coaching programs work over time)
