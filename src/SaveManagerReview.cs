using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace Alex.ReviewSession.Plugins
{
    /// <summary>
    /// Plugin for alex_SaveManagerReview Custom API.
    /// Creates the complete review session in a single transaction:
    ///   - alex_managereview (header)
    ///   - alex_managereviewline (one per category)
    ///   - alex_reviewactionitem (one per action item)
    /// 
    /// Uses ExecuteTransactionRequest for all-or-nothing consistency.
    /// </summary>
    public class SaveManagerReview : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                // ── Parse input ──
                if (!context.InputParameters.Contains("ReviewPayloadJson") ||
                    string.IsNullOrWhiteSpace(context.InputParameters["ReviewPayloadJson"]?.ToString()))
                {
                    throw new InvalidPluginExecutionException("ReviewPayloadJson is required.");
                }

                var payloadStr = context.InputParameters["ReviewPayloadJson"].ToString();
                var payload = JsonSerializer.Deserialize<ReviewPayload>(payloadStr, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (payload == null)
                    throw new InvalidPluginExecutionException("Failed to parse ReviewPayloadJson.");

                // ── Validate AgentId (always required) ──
                if (!Guid.TryParse(payload.AgentId, out Guid agentId))
                    throw new InvalidPluginExecutionException("AgentId must be a valid GUID.");

                // ── Determine mode: UPDATE if ReviewId supplied, CREATE otherwise ──
                Guid existingReviewId = Guid.Empty;
                bool updateMode = !string.IsNullOrWhiteSpace(payload.ReviewId)
                    && Guid.TryParse(payload.ReviewId, out existingReviewId);

                tracingService.Trace("SaveManagerReview: Mode={0}, Agent={1}", updateMode ? "Update" : "Create", agentId);

                var transactionRequest = new ExecuteTransactionRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    ReturnResponses = true
                };

                if (updateMode)
                {
                    // ════════════════════════════════════════════════════════
                    // UPDATE PATH — finalise existing draft review
                    // ════════════════════════════════════════════════════════
                    tracingService.Trace("SaveManagerReview: Updating review {0}", existingReviewId);

                    var updateHeader = new Entity("alex_managereview", existingReviewId);
                    updateHeader["alex_overallscore"]    = payload.OverallScore;
                    updateHeader["alex_status"]          = new OptionSetValue(100000002); // Completed
                    updateHeader["alex_openingresponse"] = TruncateField(payload.OpeningResponse, 4000);
                    updateHeader["alex_agentcomments"]   = TruncateField(payload.AgentComments, 4000);
                    updateHeader["alex_managersummary"]  = TruncateField(payload.ManagerSummary, 4000);

                    if (payload.PrepNotes != null)
                    {
                        updateHeader["alex_prepstrengths"]     = TruncateField(payload.PrepNotes.Strengths, 4000);
                        updateHeader["alex_prepimprovements"]  = TruncateField(payload.PrepNotes.Improvements, 4000);
                        updateHeader["alex_preptalkingpoints"] = TruncateField(payload.PrepNotes.TalkingPoints, 4000);
                    }

                    transactionRequest.Requests.Add(new UpdateRequest { Target = updateHeader });

                    // Delete existing action items so we can recreate from current UI state
                    var existingItems = service.RetrieveMultiple(new Microsoft.Xrm.Sdk.Query.QueryExpression("alex_reviewactionitemid")
                    {
                        ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet("alex_reviewactionitemidid"),
                        Criteria = new Microsoft.Xrm.Sdk.Query.FilterExpression
                        {
                            Conditions =
                            {
                                new Microsoft.Xrm.Sdk.Query.ConditionExpression("alex_managereview",
                                    Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, existingReviewId)
                            }
                        }
                    });

                    tracingService.Trace("SaveManagerReview: Deleting {0} existing action items", existingItems.Entities.Count);
                    foreach (var oldItem in existingItems.Entities)
                        transactionRequest.Requests.Add(new DeleteRequest { Target = oldItem.ToEntityReference() });

                    // Create updated action items
                    if (payload.ActionItems != null)
                    {
                        for (int i = 0; i < payload.ActionItems.Count; i++)
                        {
                            var item = payload.ActionItems[i];
                            if (string.IsNullOrWhiteSpace(item.Description)) continue;

                            var action = new Entity("alex_reviewactionitemid");
                            action["alex_name"]          = TruncateField(item.Description, 500);
                            action["alex_managereview"]  = new EntityReference("alex_managereview", existingReviewId);
                            action["alex_agent"]         = new EntityReference("systemuser", agentId);
                            action["alex_status"]        = new OptionSetValue(100000000); // Open
                            action["alex_sortorder"]     = i + 1;

                            if (!string.IsNullOrWhiteSpace(item.CategoryId))
                                action["alex_categoryid"] = item.CategoryId;
                            if (item.DueDate.HasValue)
                                action["alex_duedate"] = item.DueDate.Value;

                            transactionRequest.Requests.Add(new CreateRequest { Target = action });
                        }
                    }

                    tracingService.Trace("SaveManagerReview: Executing update transaction with {0} operations",
                        transactionRequest.Requests.Count);
                    service.Execute(transactionRequest);
                    tracingService.Trace("SaveManagerReview: Update success. ReviewId={0}", existingReviewId);

                    context.OutputParameters["ReviewId"]     = existingReviewId.ToString();
                    context.OutputParameters["Success"]      = true;
                    context.OutputParameters["ErrorMessage"] = "";
                    return;
                }

                // ════════════════════════════════════════════════════════
                // CREATE PATH — original single-evaluation logic
                // ════════════════════════════════════════════════════════
                if (!Guid.TryParse(payload.EvaluationId, out Guid evaluationId))
                    throw new InvalidPluginExecutionException("EvaluationId must be a valid GUID.");

                tracingService.Trace("SaveManagerReview: Creating review for Eval={0}", evaluationId);

                Guid? extensionId = null;
                if (!string.IsNullOrWhiteSpace(payload.EvaluationExtensionId) &&
                    Guid.TryParse(payload.EvaluationExtensionId, out Guid extId))
                {
                    extensionId = extId;
                }

                // ── Check for duplicate review ──
                var dupCheck = new Microsoft.Xrm.Sdk.Query.QueryExpression("alex_managereview")
                {
                    ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet("alex_managereviewid", "alex_status"),
                    Criteria = new Microsoft.Xrm.Sdk.Query.FilterExpression
                    {
                        Conditions =
                        {
                            new Microsoft.Xrm.Sdk.Query.ConditionExpression(
                                "alex_evaluation", Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, evaluationId)
                        }
                    }
                };

                var existing = service.RetrieveMultiple(dupCheck);
                if (existing.Entities.Count > 0)
                {
                    var existingStatus = existing.Entities[0]
                        .GetAttributeValue<OptionSetValue>("alex_status")?.Value ?? 0;

                    if (existingStatus == 100000002) // Completed
                    {
                        throw new InvalidPluginExecutionException(
                            "A completed review already exists for this evaluation. " +
                            "Cannot create a duplicate.");
                    }

                    if (existingStatus == 100000000 || existingStatus == 100000001)
                    {
                        tracingService.Trace("SaveManagerReview: Deleting existing Draft/InProgress review");
                        service.Delete("alex_managereview", existing.Entities[0].Id);
                    }
                }

                // ── 1. Create header ──
                var headerId = Guid.NewGuid();
                var header = new Entity("alex_managereview", headerId);
                header["alex_agent"] = new EntityReference("systemuser", agentId);
                header["alex_reviewer"] = new EntityReference("systemuser", context.UserId);
                header["alex_evaluation"] = new EntityReference("msdyn_evaluation", evaluationId);

                if (extensionId.HasValue)
                {
                    header["alex_evaluationextension"] =
                        new EntityReference("msdyn_evaluationextension", extensionId.Value);
                }

                header["alex_criteriaid"] = payload.CriteriaId ?? "";
                header["alex_sessiondate"] = DateTime.UtcNow;
                header["alex_overallscore"] = payload.OverallScore;
                header["alex_status"] = new OptionSetValue(100000002); // Completed

                // Prep notes
                header["alex_prepstrengths"] = TruncateField(payload.PrepNotes?.Strengths, 4000);
                header["alex_prepimprovements"] = TruncateField(payload.PrepNotes?.Improvements, 4000);
                header["alex_preptalkingpoints"] = TruncateField(payload.PrepNotes?.TalkingPoints, 4000);

                // Session data
                header["alex_openingresponse"] = TruncateField(payload.OpeningResponse, 4000);
                header["alex_agentcomments"] = TruncateField(payload.AgentComments, 4000);
                header["alex_managersummary"] = TruncateField(payload.ManagerSummary, 4000);

                transactionRequest.Requests.Add(new CreateRequest { Target = header });

                // ── 2. Create category lines ──
                if (payload.CategoryFeedback != null)
                {
                    for (int i = 0; i < payload.CategoryFeedback.Count; i++)
                    {
                        var cat = payload.CategoryFeedback[i];
                        var line = new Entity("alex_managereviewline");
                        line["alex_managereview"] = new EntityReference("alex_managereview", headerId);
                        line["alex_categoryid"] = cat.CategoryId ?? "";
                        line["alex_categoryname"] = TruncateField(cat.CategoryName, 200);
                        line["alex_categoryweight"] = cat.CategoryWeight;
                        line["alex_categoryscore"] = cat.CategoryScore;
                        line["alex_qearesponsejson"] = TruncateField(cat.QeaResponseJson, 10000);
                        line["alex_evaluatorresponsejson"] = TruncateField(cat.EvaluatorResponseJson, 10000);
                        line["alex_agentresponse"] = TruncateField(cat.AgentResponse, 4000);
                        line["alex_managerassessment"] = TruncateField(cat.ManagerAssessment, 4000);
                        line["alex_sortorder"] = i + 1;

                        transactionRequest.Requests.Add(new CreateRequest { Target = line });
                    }
                }

                // ── 3. Create action items ──
                if (payload.ActionItems != null)
                {
                    for (int i = 0; i < payload.ActionItems.Count; i++)
                    {
                        var item = payload.ActionItems[i];
                        if (string.IsNullOrWhiteSpace(item.Description)) continue;

                        var action = new Entity("alex_reviewactionitemid");
                        action["alex_name"] = TruncateField(item.Description, 500);
                        action["alex_managereview"] = new EntityReference("alex_managereview", headerId);
                        action["alex_agent"] = new EntityReference("systemuser", agentId);
                        action["alex_status"] = new OptionSetValue(100000000); // Open
                        action["alex_sortorder"] = i + 1;

                        if (!string.IsNullOrWhiteSpace(item.CategoryId))
                            action["alex_categoryid"] = item.CategoryId;

                        if (item.DueDate.HasValue)
                            action["alex_duedate"] = item.DueDate.Value;

                        transactionRequest.Requests.Add(new CreateRequest { Target = action });
                    }
                }

                tracingService.Trace("SaveManagerReview: Executing transaction with {0} operations",
                    transactionRequest.Requests.Count);

                // ── Execute ──
                service.Execute(transactionRequest);

                tracingService.Trace("SaveManagerReview: Success. ReviewId={0}", headerId);

                // ── Output ──
                context.OutputParameters["ReviewId"] = headerId.ToString();
                context.OutputParameters["Success"] = true;
                context.OutputParameters["ErrorMessage"] = "";
            }
            catch (InvalidPluginExecutionException)
            {
                context.OutputParameters["ReviewId"] = "";
                context.OutputParameters["Success"] = false;
                context.OutputParameters["ErrorMessage"] = "Validation error";
                throw;
            }
            catch (Exception ex)
            {
                tracingService.Trace("SaveManagerReview Error: {0}", ex.ToString());
                context.OutputParameters["ReviewId"] = "";
                context.OutputParameters["Success"] = false;
                context.OutputParameters["ErrorMessage"] = ex.Message;
                throw new InvalidPluginExecutionException(
                    $"Error saving review: {ex.Message}", ex);
            }
        }

        private static string TruncateField(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return null;
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }

        // ═══════════════════════════════════════
        // Payload DTOs
        // ═══════════════════════════════════════

        private class ReviewPayload
        {
            public string ReviewId { get; set; }          // optional — triggers UPDATE mode
            public string AgentId { get; set; }
            public string EvaluationId { get; set; }      // required only in CREATE mode
            public string EvaluationExtensionId { get; set; }
            public string CriteriaId { get; set; }
            public decimal OverallScore { get; set; }
            public PrepNotesDto PrepNotes { get; set; }
            public string OpeningResponse { get; set; }
            public List<CategoryFeedbackDto> CategoryFeedback { get; set; }
            public List<ActionItemDto> ActionItems { get; set; }
            public string AgentComments { get; set; }
            public string ManagerSummary { get; set; }
        }

        private class PrepNotesDto
        {
            public string Strengths { get; set; }
            public string Improvements { get; set; }
            public string TalkingPoints { get; set; }
        }

        private class CategoryFeedbackDto
        {
            public string CategoryId { get; set; }
            public string CategoryName { get; set; }
            public int CategoryWeight { get; set; }
            public decimal CategoryScore { get; set; }
            public string QeaResponseJson { get; set; }
            public string EvaluatorResponseJson { get; set; }
            public string AgentResponse { get; set; }
            public string ManagerAssessment { get; set; }
        }

        private class ActionItemDto
        {
            public string Description { get; set; }
            public string CategoryId { get; set; }
            public DateTime? DueDate { get; set; }
        }
    }
}