using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Alex.ReviewSession.Plugins
{
    /// <summary>
    /// Plugin for alex_GetConsolidatedEvaluations Custom API.
    /// Fetches all evaluations for an agent within a date range,
    /// groups them by questionnaire (msdyn_evaluationcriteria),
    /// applies configured weights from alex_questionnairweight,
    /// and returns aggregated per-questionnaire data + weighted composite score.
    /// </summary>
    public class GetConsolidatedEvaluations : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                // ── Parse inputs ──
                if (!context.InputParameters.Contains("AgentId") ||
                    string.IsNullOrWhiteSpace(context.InputParameters["AgentId"]?.ToString()))
                    throw new InvalidPluginExecutionException("AgentId is required.");

                if (!Guid.TryParse(context.InputParameters["AgentId"].ToString(), out Guid agentId))
                    throw new InvalidPluginExecutionException("AgentId must be a valid GUID.");

                if (!context.InputParameters.Contains("DateFrom") ||
                    string.IsNullOrWhiteSpace(context.InputParameters["DateFrom"]?.ToString()))
                    throw new InvalidPluginExecutionException("DateFrom is required.");

                if (!context.InputParameters.Contains("DateTo") ||
                    string.IsNullOrWhiteSpace(context.InputParameters["DateTo"]?.ToString()))
                    throw new InvalidPluginExecutionException("DateTo is required.");

                if (!DateTime.TryParse(context.InputParameters["DateFrom"].ToString(), out DateTime dateFrom))
                    throw new InvalidPluginExecutionException("DateFrom must be a valid date.");

                if (!DateTime.TryParse(context.InputParameters["DateTo"].ToString(), out DateTime dateTo))
                    throw new InvalidPluginExecutionException("DateTo must be a valid date.");

                tracingService.Trace("GetConsolidatedEvaluations: Agent={0}, From={1}, To={2}",
                    agentId, dateFrom.ToString("yyyy-MM-dd"), dateTo.ToString("yyyy-MM-dd"));

                // ── Step 1: Fetch all evaluations in date range ──
                var evalQuery = new QueryExpression("msdyn_evaluation")
                {
                    ColumnSet = new ColumnSet(
                        "msdyn_evaluationid",
                        "msdyn_name",
                        "msdyn_score",
                        "msdyn_responsejson",
                        "msdyn_agentresponsejson",
                        "msdyn_scorejson",
                        "msdyn_evaluationextension",
                        "msdyn_evaluationcriteria",
                        "msdyn_regardingobjectowner",
                        "msdyn_regardingobjectid",
                        "msdyn_relatedrecordtype",
                        "msdyn_evaluatorduedate",
                        "createdon",
                        "statuscode"
                    ),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
                        {
                            new ConditionExpression("msdyn_regardingobjectowner",
                                ConditionOperator.Equal, agentId),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0),
                            new ConditionExpression("statuscode", ConditionOperator.Equal, 700610001),
                            new ConditionExpression("createdon", ConditionOperator.OnOrAfter, dateFrom),
                            new ConditionExpression("createdon", ConditionOperator.OnOrBefore, dateTo)
                        }
                    },
                    Orders = { new OrderExpression("createdon", OrderType.Descending) }
                };

                var evalResults = service.RetrieveMultiple(evalQuery);
                tracingService.Trace("GetConsolidatedEvaluations: Found {0} evaluations", evalResults.Entities.Count);

                if (evalResults.Entities.Count == 0)
                {
                    context.OutputParameters["ConsolidatedJson"] = JsonSerializer.Serialize(
                        new Dictionary<string, object>
                        {
                            { "totalEvaluations", 0 },
                            { "questionnaires", new List<object>() },
                            { "weightedOverallScore", 0 },
                            { "dateFrom", dateFrom.ToString("yyyy-MM-dd") },
                            { "dateTo", dateTo.ToString("yyyy-MM-dd") }
                        });
                    return;
                }

                // ── Step 2: Fetch extension records for AI summaries ──
                var extIds = evalResults.Entities
                    .Select(e => e.GetAttributeValue<EntityReference>("msdyn_evaluationextension"))
                    .Where(r => r != null)
                    .Select(r => r.Id)
                    .Distinct()
                    .ToList();

                var extMap = new Dictionary<Guid, Entity>();
                if (extIds.Count > 0)
                {
                    var extQuery = new QueryExpression("msdyn_evaluationextension")
                    {
                        ColumnSet = new ColumnSet(
                            "msdyn_evaluationextensionid",
                            "msdyn_responsejson",
                            "msdyn_agentresponsejson"
                        ),
                        Criteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression("msdyn_evaluationextensionid",
                                    ConditionOperator.In, extIds.Cast<object>().ToArray())
                            }
                        }
                    };
                    var extResults = service.RetrieveMultiple(extQuery);
                    foreach (var ext in extResults.Entities)
                        extMap[ext.Id] = ext;
                }

                tracingService.Trace("GetConsolidatedEvaluations: Found {0} extensions", extMap.Count);

                // ── Step 3: Fetch questionnaire weights ──
                var weightQuery = new QueryExpression("alex_questionnairweight")
                {
                    ColumnSet = new ColumnSet(
                        "alex_questionnairweightid",
                        "alex_evaluationcriteria",
                        "alex_weight",
                        "alex_prioritymultiplier",
                        "alex_isactive"
                    ),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("alex_isactive", ConditionOperator.Equal, true),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    }
                };
                var weightResults = service.RetrieveMultiple(weightQuery);
                var weightMap = new Dictionary<Guid, (decimal weight, decimal multiplier)>();
                foreach (var w in weightResults.Entities)
                {
                    var criteriaRef = w.GetAttributeValue<EntityReference>("alex_evaluationcriteria");
                    if (criteriaRef != null)
                    {
                        var wt = w.GetAttributeValue<decimal?>("alex_weight") ?? 0;
                        var mult = w.GetAttributeValue<decimal?>("alex_prioritymultiplier") ?? 1;
                        weightMap[criteriaRef.Id] = (wt, mult);
                    }
                }

                tracingService.Trace("GetConsolidatedEvaluations: Found {0} weight configs", weightMap.Count);

                // ── Step 4: Fetch criteria names ──
                var criteriaIds = evalResults.Entities
                    .Select(e => e.GetAttributeValue<EntityReference>("msdyn_evaluationcriteria"))
                    .Where(r => r != null)
                    .Select(r => r.Id)
                    .Distinct()
                    .ToList();

                var criteriaNameMap = new Dictionary<Guid, string>();
                if (criteriaIds.Count > 0)
                {
                    var criteriaQuery = new QueryExpression("msdyn_evaluationcriteria")
                    {
                        ColumnSet = new ColumnSet("msdyn_evaluationcriteriaid", "msdyn_name"),
                        Criteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression("msdyn_evaluationcriteriaid",
                                    ConditionOperator.In, criteriaIds.Cast<object>().ToArray())
                            }
                        }
                    };
                    var criteriaResults = service.RetrieveMultiple(criteriaQuery);
                    foreach (var c in criteriaResults.Entities)
                        criteriaNameMap[c.Id] = c.GetAttributeValue<string>("msdyn_name") ?? "Unknown";
                }

                // ── Step 5: Group evaluations by criteria ──
                var groups = new Dictionary<Guid, List<Entity>>();
                foreach (var eval in evalResults.Entities)
                {
                    var criteriaRef = eval.GetAttributeValue<EntityReference>("msdyn_evaluationcriteria");
                    if (criteriaRef == null) continue;
                    if (!groups.ContainsKey(criteriaRef.Id))
                        groups[criteriaRef.Id] = new List<Entity>();
                    groups[criteriaRef.Id].Add(eval);
                }

                // ── Step 6: Calculate effective weights ──
                // Configured weights are fixed. Remaining weight is distributed
                // proportionally among unconfigured questionnaires by eval count.
                decimal totalConfiguredWeight = 0;
                int totalUnconfiguredEvalCount = 0;

                foreach (var kvp in groups)
                {
                    if (weightMap.ContainsKey(kvp.Key))
                        totalConfiguredWeight += weightMap[kvp.Key].weight;
                    else
                        totalUnconfiguredEvalCount += kvp.Value.Count;
                }

                decimal remainingWeight = Math.Max(0, 100 - totalConfiguredWeight);

                var effectiveWeights = new Dictionary<Guid, decimal>();
                foreach (var kvp in groups)
                {
                    if (weightMap.ContainsKey(kvp.Key))
                    {
                        effectiveWeights[kvp.Key] = weightMap[kvp.Key].weight;
                    }
                    else
                    {
                        // Proportional split of remaining weight by evaluation count
                        decimal proportion = totalUnconfiguredEvalCount > 0
                            ? (decimal)kvp.Value.Count / totalUnconfiguredEvalCount
                            : 0;
                        effectiveWeights[kvp.Key] = Math.Round(remainingWeight * proportion, 2);
                    }
                }

                // ── Step 7: Build per-questionnaire aggregates ──
                var questionnaires = new List<Dictionary<string, object>>();
                decimal weightedOverallScore = 0;

                foreach (var kvp in groups)
                {
                    var criteriaId = kvp.Key;
                    var evals = kvp.Value;
                    var criteriaName = criteriaNameMap.ContainsKey(criteriaId)
                        ? criteriaNameMap[criteriaId] : "Unknown Questionnaire";

                    // Calculate average score
                    var scores = evals
                        .Select(e => e.GetAttributeValue<int?>("msdyn_score"))
                        .Where(s => s.HasValue)
                        .Select(s => (double)s.Value)
                        .ToList();

                    double avgScore = scores.Count > 0 ? Math.Round(scores.Average(), 1) : 0;
                    double minScore = scores.Count > 0 ? scores.Min() : 0;
                    double maxScore = scores.Count > 0 ? scores.Max() : 0;

                    // Get weight info
                    decimal configuredWeight = weightMap.ContainsKey(criteriaId)
                        ? weightMap[criteriaId].weight : -1; // -1 = not configured
                    decimal multiplier = weightMap.ContainsKey(criteriaId)
                        ? weightMap[criteriaId].multiplier : 1;
                    decimal effWeight = effectiveWeights[criteriaId];

                    // Weighted contribution to overall
                    weightedOverallScore += (decimal)avgScore * (effWeight / 100);

                    // Collect AI summaries from evaluations
                    var aiSummaries = new List<string>();
                    var aiActionPlans = new List<string>();

                    // Build individual evaluation entries
                    var evalEntries = new List<Dictionary<string, object>>();
                    foreach (var eval in evals)
                    {
                        var evalId = eval.Id;
                        var name = eval.GetAttributeValue<string>("msdyn_name") ?? "";
                        var createdOn = eval.GetAttributeValue<DateTime>("createdon");
                        var score = eval.GetAttributeValue<int?>("msdyn_score");
                        var responseJson = eval.GetAttributeValue<string>("msdyn_responsejson") ?? "";
                        var agentRespJson = eval.GetAttributeValue<string>("msdyn_agentresponsejson") ?? "";

                        // Record type and regarding object
                        var relatedRecordTypeOsv = eval.GetAttributeValue<OptionSetValue>("msdyn_relatedrecordtype");
                        int? relatedRecordType = relatedRecordTypeOsv?.Value;
                        var regardingRef = eval.GetAttributeValue<EntityReference>("msdyn_regardingobjectid");
                        string recordTypeName = relatedRecordType switch
                        {
                            0 => "Case",
                            1 => "Conversation",
                            _ => "Unknown"
                        };
                        string regardingId = regardingRef?.Id.ToString();
                        string regardingName = regardingRef?.Name ?? "";
                        string regardingEntity = regardingRef?.LogicalName ?? "";

                        // Parse QEA response
                        object qeaResponse = ParseJsonSafe(responseJson, tracingService);

                        // Extension data
                        string extensionId = null;
                        object evaluatorResponse = null;
                        object computedScores = null;
                        string aiSummary = "";
                        string aiActionPlan = "";

                        // Legacy AI fields
                        if (!string.IsNullOrWhiteSpace(agentRespJson))
                        {
                            var aiData = ExtractAiFields(agentRespJson, tracingService);
                            aiSummary = aiData.summary;
                            aiActionPlan = aiData.actionPlan;
                        }

                        var extRef = eval.GetAttributeValue<EntityReference>("msdyn_evaluationextension");
                        if (extRef != null && extMap.ContainsKey(extRef.Id))
                        {
                            var ext = extMap[extRef.Id];
                            extensionId = ext.Id.ToString();
                            var extJson = ext.GetAttributeValue<string>("msdyn_responsejson") ?? "";
                            var parsed = ParseExtensionResponse(extJson, tracingService);
                            evaluatorResponse = parsed.evaluatorResponse;
                            computedScores = parsed.computedScores;

                            if (qeaResponse == null && parsed.qeaResponse != null)
                                qeaResponse = parsed.qeaResponse;

                            var extAgentJson = ext.GetAttributeValue<string>("msdyn_agentresponsejson") ?? "";
                            if (!string.IsNullOrWhiteSpace(extAgentJson))
                            {
                                var aiData = ExtractAiFields(extAgentJson, tracingService);
                                if (!string.IsNullOrWhiteSpace(aiData.summary)) aiSummary = aiData.summary;
                                if (!string.IsNullOrWhiteSpace(aiData.actionPlan)) aiActionPlan = aiData.actionPlan;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(aiSummary)) aiSummaries.Add(aiSummary);
                        if (!string.IsNullOrWhiteSpace(aiActionPlan)) aiActionPlans.Add(aiActionPlan);

                        evalEntries.Add(new Dictionary<string, object>
                        {
                            { "evaluationId", evalId.ToString() },
                            { "evaluationExtensionId", extensionId },
                            { "name", name },
                            { "date", createdOn.ToString("yyyy-MM-dd") },
                            { "score", score.HasValue ? score.Value : 0 },
                            { "recordType", recordTypeName },
                            { "regardingId", regardingId },
                            { "regardingName", regardingName },
                            { "regardingEntity", regardingEntity },
                            { "aiSummary", aiSummary },
                            { "aiActionPlan", aiActionPlan },
                            { "qeaResponse", qeaResponse },
                            { "evaluatorResponse", evaluatorResponse },
                            { "computedScores", computedScores }
                        });
                    }

                    questionnaires.Add(new Dictionary<string, object>
                    {
                        { "criteriaId", criteriaId.ToString() },
                        { "criteriaName", criteriaName },
                        { "evaluationCount", evals.Count },
                        { "avgScore", avgScore },
                        { "minScore", minScore },
                        { "maxScore", maxScore },
                        { "configuredWeight", configuredWeight },
                        { "priorityMultiplier", multiplier },
                        { "effectiveWeight", effWeight },
                        { "aiSummaries", aiSummaries },
                        { "aiActionPlans", aiActionPlans },
                        { "evaluations", evalEntries }
                    });
                }

                // Sort questionnaires by effective weight descending
                questionnaires.Sort((a, b) =>
                    ((decimal)b["effectiveWeight"]).CompareTo((decimal)a["effectiveWeight"]));

                // ── Step 8: Build response ──
                var result = new Dictionary<string, object>
                {
                    { "totalEvaluations", evalResults.Entities.Count },
                    { "questionnaireCount", groups.Count },
                    { "weightedOverallScore", Math.Round((double)weightedOverallScore, 1) },
                    { "dateFrom", dateFrom.ToString("yyyy-MM-dd") },
                    { "dateTo", dateTo.ToString("yyyy-MM-dd") },
                    { "questionnaires", questionnaires }
                };

                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                tracingService.Trace("GetConsolidatedEvaluations: Returning {0} questionnaire groups, {1} total evals",
                    groups.Count, evalResults.Entities.Count);
                context.OutputParameters["ConsolidatedJson"] = json;
            }
            catch (InvalidPluginExecutionException) { throw; }
            catch (Exception ex)
            {
                tracingService.Trace("GetConsolidatedEvaluations Error: {0}", ex.ToString());
                throw new InvalidPluginExecutionException(
                    $"Error retrieving consolidated evaluations: {ex.Message}", ex);
            }
        }

        // ═══════════════════════════════════════
        // Shared helpers (same as GetAgentEvaluations)
        // ═══════════════════════════════════════

        private static object ParseJsonSafe(string json, ITracingService tracing)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<JsonElement>(json); }
            catch (Exception ex) { tracing.Trace("ParseJsonSafe: Failed - {0}", ex.Message); return null; }
        }

        private static (string summary, string actionPlan) ExtractAiFields(string json, ITracingService tracing)
        {
            if (string.IsNullOrWhiteSpace(json)) return ("", "");
            try
            {
                try
                {
                    var element = JsonSerializer.Deserialize<JsonElement>(json);
                    var s = element.TryGetProperty("evaluationSummary", out var sEl) ? (sEl.GetString() ?? "") : "";
                    var a = element.TryGetProperty("actionPlan", out var aEl) ? (aEl.GetString() ?? "") : "";
                    if (!string.IsNullOrWhiteSpace(s) || !string.IsNullOrWhiteSpace(a)) return (s, a);
                }
                catch { }

                var objects = ExtractJsonObjects(json);
                foreach (var objStr in objects)
                {
                    try
                    {
                        var element = JsonSerializer.Deserialize<JsonElement>(objStr);
                        if (element.TryGetProperty("evaluationSummary", out _) || element.TryGetProperty("actionPlan", out _))
                        {
                            var s = element.TryGetProperty("evaluationSummary", out var sEl) ? (sEl.GetString() ?? "") : "";
                            var a = element.TryGetProperty("actionPlan", out var aEl) ? (aEl.GetString() ?? "") : "";
                            return (s, a);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { tracing.Trace("ExtractAiFields: Failed - {0}", ex.Message); }
            return ("", "");
        }

        private static (object qeaResponse, object evaluatorResponse, object computedScores)
            ParseExtensionResponse(string json, ITracingService tracing)
        {
            if (string.IsNullOrWhiteSpace(json)) return (null, null, null);
            try
            {
                var objects = ExtractJsonObjects(json);
                object qeaResponse = null, evaluatorResponse = null, computedScores = null;
                foreach (var objStr in objects)
                {
                    try
                    {
                        var element = JsonSerializer.Deserialize<JsonElement>(objStr);
                        if (element.TryGetProperty("categories", out _)) computedScores = element;
                        else if (element.TryGetProperty("evaluationSummary", out _)) qeaResponse = element;
                        else if (element.TryGetProperty("responses", out _)) evaluatorResponse = element;
                    }
                    catch { }
                }
                return (qeaResponse, evaluatorResponse, computedScores);
            }
            catch (Exception ex) { tracing.Trace("ParseExtensionResponse: Failed - {0}", ex.Message); return (null, null, null); }
        }

        private static List<string> ExtractJsonObjects(string input)
        {
            var objects = new List<string>();
            int depth = 0, start = -1;
            bool inString = false, escaped = false;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (escaped) { escaped = false; continue; }
                if (c == '\\' && inString) { escaped = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') { if (depth == 0) start = i; depth++; }
                else if (c == '}') { depth--; if (depth == 0 && start >= 0) { objects.Add(input.Substring(start, i - start + 1)); start = -1; } }
            }
            return objects;
        }
    }
}