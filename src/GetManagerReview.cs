using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Alex.ReviewSession.Plugins
{
    /// <summary>
    /// Plugin for alex_GetManagerReview Custom API.
    /// Retrieves a complete saved review: header + lines + action items.
    /// Used by the read-only review viewer.
    /// </summary>
    public class GetManagerReview : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                if (!context.InputParameters.Contains("ReviewId") ||
                    string.IsNullOrWhiteSpace(context.InputParameters["ReviewId"]?.ToString()))
                {
                    throw new InvalidPluginExecutionException("ReviewId is required.");
                }

                if (!Guid.TryParse(context.InputParameters["ReviewId"].ToString(), out Guid reviewId))
                {
                    throw new InvalidPluginExecutionException("ReviewId must be a valid GUID.");
                }

                tracingService.Trace("GetManagerReview: Fetching review {0}", reviewId);

                // ── Fetch header ──
                var header = service.Retrieve("alex_managereview", reviewId, new ColumnSet(true));
                tracingService.Trace("GetManagerReview: Retrieved header");

                // ── Fetch lines ──
                var lineQuery = new QueryExpression("alex_managereviewline")
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("alex_managereview",
                                ConditionOperator.Equal, reviewId)
                        }
                    },
                    Orders = { new OrderExpression("alex_sortorder", OrderType.Ascending) }
                };
                var lines = service.RetrieveMultiple(lineQuery);
                tracingService.Trace("GetManagerReview: Retrieved {0} lines", lines.Entities.Count);

                // ── Fetch action items ──
                var actionQuery = new QueryExpression("alex_reviewactionitemid")
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("alex_managereview",
                                ConditionOperator.Equal, reviewId)
                        }
                    },
                    Orders = { new OrderExpression("alex_sortorder", OrderType.Ascending) }
                };
                var actions = service.RetrieveMultiple(actionQuery);
                tracingService.Trace("GetManagerReview: Retrieved {0} action items", actions.Entities.Count);

                // ── Build response ──
                var result = new Dictionary<string, object>
                {
                    { "reviewId", header.Id.ToString() },
                    { "name", header.GetAttributeValue<string>("alex_name") ?? "" },
                    { "agentId", header.GetAttributeValue<EntityReference>("alex_agent")?.Id.ToString() },
                    { "agentName", header.GetAttributeValue<EntityReference>("alex_agent")?.Name ?? "" },
                    { "reviewerId", header.GetAttributeValue<EntityReference>("alex_reviewer")?.Id.ToString() },
                    { "reviewerName", header.GetAttributeValue<EntityReference>("alex_reviewer")?.Name ?? "" },
                    { "evaluationId", header.GetAttributeValue<EntityReference>("alex_evaluation")?.Id.ToString() },
                    { "evaluationName", header.GetAttributeValue<EntityReference>("alex_evaluation")?.Name ?? "" },
                    { "criteriaId", header.GetAttributeValue<string>("alex_criteriaid") ?? "" },
                    { "sessionDate", header.GetAttributeValue<DateTime?>("alex_sessiondate")?.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                    { "overallScore", header.GetAttributeValue<decimal?>("alex_overallscore") },
                    { "status", header.GetAttributeValue<OptionSetValue>("alex_status")?.Value },
                    { "statusLabel", GetStatusLabel(header.GetAttributeValue<OptionSetValue>("alex_status")?.Value) },
                    { "prepStrengths", header.GetAttributeValue<string>("alex_prepstrengths") ?? "" },
                    { "prepImprovements", header.GetAttributeValue<string>("alex_prepimprovements") ?? "" },
                    { "prepTalkingPoints", header.GetAttributeValue<string>("alex_preptalkingpoints") ?? "" },
                    { "openingResponse", header.GetAttributeValue<string>("alex_openingresponse") ?? "" },
                    { "agentComments", header.GetAttributeValue<string>("alex_agentcomments") ?? "" },
                    { "managerSummary", header.GetAttributeValue<string>("alex_managersummary") ?? "" },
                    { "createdOn", header.GetAttributeValue<DateTime>("createdon").ToString("yyyy-MM-ddTHH:mm:ssZ") },
                    { "modifiedOn", header.GetAttributeValue<DateTime>("modifiedon").ToString("yyyy-MM-ddTHH:mm:ssZ") },
                    { "categoryFeedback", lines.Entities.Select(l => new Dictionary<string, object>
                        {
                            { "lineId", l.Id.ToString() },
                            { "categoryId", l.GetAttributeValue<string>("alex_categoryid") ?? "" },
                            { "categoryName", l.GetAttributeValue<string>("alex_categoryname") ?? "" },
                            { "categoryWeight", l.GetAttributeValue<int?>("alex_categoryweight") ?? 0 },
                            { "categoryScore", l.GetAttributeValue<decimal?>("alex_categoryscore") },
                            { "qeaResponseJson", l.GetAttributeValue<string>("alex_qearesponsejson") ?? "" },
                            { "evaluatorResponseJson", l.GetAttributeValue<string>("alex_evaluatorresponsejson") ?? "" },
                            { "agentResponse", l.GetAttributeValue<string>("alex_agentresponse") ?? "" },
                            { "managerAssessment", l.GetAttributeValue<string>("alex_managerassessment") ?? "" },
                            { "sortOrder", l.GetAttributeValue<int?>("alex_sortorder") ?? 0 }
                        }).ToList() },
                    { "actionItems", actions.Entities.Select(a => new Dictionary<string, object>
                        {
                            { "actionId", a.Id.ToString() },
                            { "description", a.GetAttributeValue<string>("alex_name") ?? "" },
                            { "categoryId", a.GetAttributeValue<string>("alex_categoryid") ?? "" },
                            { "status", a.GetAttributeValue<OptionSetValue>("alex_status")?.Value },
                            { "statusLabel", GetActionStatusLabel(a.GetAttributeValue<OptionSetValue>("alex_status")?.Value) },
                            { "dueDate", a.GetAttributeValue<DateTime?>("alex_duedate")?.ToString("yyyy-MM-dd") },
                            { "sortOrder", a.GetAttributeValue<int?>("alex_sortorder") ?? 0 }
                        }).ToList() }
                };

                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                context.OutputParameters["ReviewJson"] = json;
                tracingService.Trace("GetManagerReview: Success");
            }
            catch (InvalidPluginExecutionException) { throw; }
            catch (Exception ex)
            {
                tracingService.Trace("GetManagerReview Error: {0}", ex.ToString());
                throw new InvalidPluginExecutionException(
                    $"Error retrieving review: {ex.Message}", ex);
            }
        }

        private static string GetStatusLabel(int? value) => value switch
        {
            100000000 => "Draft",
            100000001 => "In Progress",
            100000002 => "Completed",
            _ => "Unknown"
        };

        private static string GetActionStatusLabel(int? value) => value switch
        {
            100000000 => "Open",
            100000001 => "In Progress",
            100000002 => "Completed",
            _ => "Unknown"
        };
    }
}