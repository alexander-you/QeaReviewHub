using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Alex.ReviewSession.Plugins
{
    /// <summary>
    /// Plugin for alex_GetAgentEvaluations Custom API.
    /// 
    /// Entity: msdyn_evaluation
    /// Agent field: msdyn_regardingobjectowner (owner of the evaluated interaction)
    /// Score field: msdyn_score (int)
    /// Extension: linked via msdyn_evaluationextension lookup ON the evaluation record
    /// </summary>
    public class GetAgentEvaluations : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                if (!context.InputParameters.Contains("AgentId") ||
                    string.IsNullOrWhiteSpace(context.InputParameters["AgentId"]?.ToString()))
                {
                    throw new InvalidPluginExecutionException("AgentId is required.");
                }

                var agentIdStr = context.InputParameters["AgentId"].ToString();
                if (!Guid.TryParse(agentIdStr, out Guid agentId))
                {
                    throw new InvalidPluginExecutionException("AgentId must be a valid GUID.");
                }

                var top = 10;
                // Note: Top input parameter ignored — always return up to 10 evaluations
                tracingService.Trace("GetAgentEvaluations: Agent={0}, Top={1}", agentId, top);

                // ── Step 1: Query evaluations ──
                // Filter logic per QEA documentation:
                //   - msdyn_regardingobjectowner = the evaluated agent
                //   - statuscode = InProgress (700610001)
                //   - msdyn_evaluatorduedate >= today OR NULL
                var rootFilter = new FilterExpression(LogicalOperator.And);
                rootFilter.AddCondition("msdyn_regardingobjectowner", ConditionOperator.Equal, agentId);
                rootFilter.AddCondition("statecode", ConditionOperator.Equal, 0);
                rootFilter.AddCondition("statuscode", ConditionOperator.Equal, 700610001);

                var dueDateFilter = new FilterExpression(LogicalOperator.Or);
                dueDateFilter.AddCondition("msdyn_evaluatorduedate", ConditionOperator.OnOrAfter, DateTime.UtcNow.Date);
                dueDateFilter.AddCondition("msdyn_evaluatorduedate", ConditionOperator.Null);
                rootFilter.AddFilter(dueDateFilter);

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
                        "msdyn_regardingobjectowner",
                        "msdyn_evaluatorduedate",
                        "ownerid",
                        "createdon",
                        "statuscode",
                        "msdyn_qualityagentstatus"
                    ),
                    Criteria = rootFilter,
                    Orders = { new OrderExpression("createdon", OrderType.Descending) },
                    TopCount = top
                };

                var evalResults = service.RetrieveMultiple(evalQuery);
                tracingService.Trace("GetAgentEvaluations: Found {0} evaluations", evalResults.Entities.Count);

                if (evalResults.Entities.Count == 0)
                {
                    context.OutputParameters["EvaluationsJson"] = "[]";
                    return;
                }

                // ── Step 2: Batch fetch extension records ──
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
                    {
                        extMap[ext.Id] = ext;
                    }
                }

                tracingService.Trace("GetAgentEvaluations: Found {0} extensions", extMap.Count);

                // ── Step 3: Check for existing manager reviews ──
                var evalIds = evalResults.Entities.Select(e => e.Id).ToList();
                var reviewMap = new Dictionary<Guid, (Guid reviewId, int status)>();

                try
                {
                    var reviewQuery = new QueryExpression("alex_managereview")
                    {
                        ColumnSet = new ColumnSet("alex_managereviewid", "alex_evaluation", "alex_status"),
                        Criteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression("alex_evaluation", ConditionOperator.In,
                                    evalIds.Cast<object>().ToArray())
                            }
                        }
                    };

                    var reviewResults = service.RetrieveMultiple(reviewQuery);
                    foreach (var review in reviewResults.Entities)
                    {
                        var evalRef = review.GetAttributeValue<EntityReference>("alex_evaluation");
                        var status = review.GetAttributeValue<OptionSetValue>("alex_status");
                        if (evalRef != null && !reviewMap.ContainsKey(evalRef.Id))
                        {
                            reviewMap[evalRef.Id] = (review.Id, status?.Value ?? 100000000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    tracingService.Trace("GetAgentEvaluations: Review lookup skipped - {0}", ex.Message);
                }

                // ── Step 4: Build response ──
                var evaluations = new List<Dictionary<string, object>>();

                foreach (var eval in evalResults.Entities)
                {
                    var evalId = eval.Id;
                    var name = eval.GetAttributeValue<string>("msdyn_name") ?? "";
                    var createdOn = eval.GetAttributeValue<DateTime>("createdon");
                    var score = eval.GetAttributeValue<int?>("msdyn_score");
                    var responseJson = eval.GetAttributeValue<string>("msdyn_responsejson") ?? "";
                    var agentRespJson = eval.GetAttributeValue<string>("msdyn_agentresponsejson") ?? "";
                    var scoreJson = eval.GetAttributeValue<string>("msdyn_scorejson") ?? "";

                    var qeaResponse = ParseJsonSafe(responseJson, tracingService);

                    object evaluatorResponse = null;
                    object computedScores = null;
                    string extensionId = null;
                    string aiSummary = "";
                    string aiActionPlan = "";

                    // Parse AI summary + action plan from agentresponsejson on the eval record first (legacy path)
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

                        // v2 pattern: AI payload lives on the extension record
                        var extAgentJson = ext.GetAttributeValue<string>("msdyn_agentresponsejson") ?? "";
                        if (!string.IsNullOrWhiteSpace(extAgentJson))
                        {
                            var aiData = ExtractAiFields(extAgentJson, tracingService);
                            // Prefer extension values (v2) over evaluation values (legacy)
                            if (!string.IsNullOrWhiteSpace(aiData.summary)) aiSummary = aiData.summary;
                            if (!string.IsNullOrWhiteSpace(aiData.actionPlan)) aiActionPlan = aiData.actionPlan;
                        }
                    }

                    if (computedScores == null && !string.IsNullOrWhiteSpace(scoreJson))
                        computedScores = ParseJsonSafe(scoreJson, tracingService);

                    string existingReviewId = null;
                    string existingReviewStatus = null;
                    if (reviewMap.ContainsKey(evalId))
                    {
                        var (revId, revStatus) = reviewMap[evalId];
                        existingReviewId = revId.ToString();
                        existingReviewStatus = revStatus switch
                        {
                            100000000 => "Draft",
                            100000001 => "InProgress",
                            100000002 => "Completed",
                            _ => "Unknown"
                        };
                    }

                    evaluations.Add(new Dictionary<string, object>
                    {
                        { "evaluationId", evalId.ToString() },
                        { "evaluationExtensionId", extensionId },
                        { "name", name },
                        { "date", createdOn.ToString("yyyy-MM-dd") },
                        { "overallScore", score.HasValue ? score.Value : 0 },
                        { "existingReviewId", existingReviewId },
                        { "existingReviewStatus", existingReviewStatus },
                        { "aiSummary", aiSummary },
                        { "aiActionPlan", aiActionPlan },
                        { "qeaResponse", qeaResponse },
                        { "evaluatorResponse", evaluatorResponse },
                        { "computedScores", computedScores }
                    });
                }

                var json = JsonSerializer.Serialize(evaluations, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                tracingService.Trace("GetAgentEvaluations: Returning {0} evaluations", evaluations.Count);
                context.OutputParameters["EvaluationsJson"] = json;
            }
            catch (InvalidPluginExecutionException) { throw; }
            catch (Exception ex)
            {
                tracingService.Trace("GetAgentEvaluations Error: {0}", ex.ToString());
                throw new InvalidPluginExecutionException(
                    $"Error retrieving evaluations: {ex.Message}", ex);
            }
        }

        private static object ParseJsonSafe(string json, ITracingService tracing)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<JsonElement>(json); }
            catch (Exception ex) { tracing.Trace("ParseJsonSafe: Failed - {0}", ex.Message); return null; }
        }

        /// <summary>
        /// Extracts evaluationSummary and actionPlan from a QEA AI response JSON.
        /// Handles both clean JSON objects and concatenated-object payloads.
        /// </summary>
        private static (string summary, string actionPlan) ExtractAiFields(string json, ITracingService tracing)
        {
            if (string.IsNullOrWhiteSpace(json))
                return ("", "");

            try
            {
                // Try direct parse first
                try
                {
                    var element = JsonSerializer.Deserialize<JsonElement>(json);
                    var s = element.TryGetProperty("evaluationSummary", out var sEl) ? (sEl.GetString() ?? "") : "";
                    var a = element.TryGetProperty("actionPlan", out var aEl) ? (aEl.GetString() ?? "") : "";
                    if (!string.IsNullOrWhiteSpace(s) || !string.IsNullOrWhiteSpace(a))
                        return (s, a);
                }
                catch { /* fall through to multi-object parsing */ }

                // Fall back to splitting concatenated objects
                var objects = ExtractJsonObjects(json);
                foreach (var objStr in objects)
                {
                    try
                    {
                        var element = JsonSerializer.Deserialize<JsonElement>(objStr);
                        var hasSummary = element.TryGetProperty("evaluationSummary", out _);
                        var hasActionPlan = element.TryGetProperty("actionPlan", out _);
                        if (hasSummary || hasActionPlan)
                        {
                            var s = element.TryGetProperty("evaluationSummary", out var sEl) ? (sEl.GetString() ?? "") : "";
                            var a = element.TryGetProperty("actionPlan", out var aEl) ? (aEl.GetString() ?? "") : "";
                            return (s, a);
                        }
                    }
                    catch { /* skip malformed object */ }
                }
            }
            catch (Exception ex)
            {
                tracing.Trace("ExtractAiFields: Failed - {0}", ex.Message);
            }

            return ("", "");
        }

        private static (object qeaResponse, object evaluatorResponse, object computedScores)
            ParseExtensionResponse(string json, ITracingService tracing)
        {
            if (string.IsNullOrWhiteSpace(json))
                return (null, null, null);

            try
            {
                var objects = ExtractJsonObjects(json);
                object qeaResponse = null, evaluatorResponse = null, computedScores = null;

                foreach (var objStr in objects)
                {
                    try
                    {
                        var element = JsonSerializer.Deserialize<JsonElement>(objStr);
                        if (element.TryGetProperty("categories", out _))
                            computedScores = element;
                        else if (element.TryGetProperty("evaluationSummary", out _))
                            qeaResponse = element;
                        else if (element.TryGetProperty("responses", out _))
                            evaluatorResponse = element;
                    }
                    catch (Exception ex)
                    {
                        tracing.Trace("ParseExtensionResponse: Skipping - {0}", ex.Message);
                    }
                }
                return (qeaResponse, evaluatorResponse, computedScores);
            }
            catch (Exception ex)
            {
                tracing.Trace("ParseExtensionResponse: Failed - {0}", ex.Message);
                return (null, null, null);
            }
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