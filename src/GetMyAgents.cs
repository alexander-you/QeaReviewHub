using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Alex.ReviewSession.Plugins
{
    /// <summary>
    /// Plugin for alex_GetMyAgents Custom API.
    /// Retrieves agents that share queue membership with the calling user (manager).
    /// Returns a JSON array of agent objects shaped for the Review Session UI.
    /// </summary>
    public class GetMyAgents : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                var callerId = context.UserId;
                tracingService.Trace("GetMyAgents: Executing for user {0}", callerId);

                // ── Step 1: Get queues the calling user belongs to ──
                var callerQueueQuery = new QueryExpression("queuemembership")
                {
                    ColumnSet = new ColumnSet("queueid"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("systemuserid", ConditionOperator.Equal, callerId)
                        }
                    }
                };

                var callerQueueResults = service.RetrieveMultiple(callerQueueQuery);
                var queueIds = callerQueueResults.Entities
                    .Select(e => e.GetAttributeValue<Guid>("queueid"))
                    .Distinct()
                    .ToList();

                tracingService.Trace("GetMyAgents: Found {0} queues for caller", queueIds.Count);

                if (queueIds.Count == 0)
                {
                    context.OutputParameters["AgentsJson"] = "[]";
                    return;
                }

                // ── Step 2: Get all users in those queues (excluding the caller) ──
                var memberQuery = new QueryExpression("queuemembership")
                {
                    ColumnSet = new ColumnSet("systemuserid", "queueid"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("queueid", ConditionOperator.In, queueIds.Cast<object>().ToArray()),
                            new ConditionExpression("systemuserid", ConditionOperator.NotEqual, callerId)
                        }
                    }
                };

                var memberResults = service.RetrieveMultiple(memberQuery);

                // Build a map: userId → list of queueIds
                var userQueueMap = new Dictionary<Guid, List<Guid>>();
                foreach (var member in memberResults.Entities)
                {
                    var userId = member.GetAttributeValue<Guid>("systemuserid");
                    var queueId = member.GetAttributeValue<Guid>("queueid");

                    if (!userQueueMap.ContainsKey(userId))
                        userQueueMap[userId] = new List<Guid>();

                    userQueueMap[userId].Add(queueId);
                }

                tracingService.Trace("GetMyAgents: Found {0} unique agents across queues", userQueueMap.Count);

                if (userQueueMap.Count == 0)
                {
                    context.OutputParameters["AgentsJson"] = "[]";
                    return;
                }

                // ── Step 3: Retrieve user details ──
                // accessmode 0 = Read-Write (interactive human users only)
                // This excludes: Application Users (4), Non-Interactive (3),
                // Delegated Admin (5), and system/bot accounts
                var userQuery = new QueryExpression("systemuser")
                {
                    ColumnSet = new ColumnSet(
                        "systemuserid",
                        "fullname",
                        "jobtitle",
                        "internalemailaddress",
                        "isdisabled",
                        "accessmode"
                    ),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("systemuserid", ConditionOperator.In,
                                userQueueMap.Keys.Cast<object>().ToArray()),
                            new ConditionExpression("isdisabled", ConditionOperator.Equal, false),
                            new ConditionExpression("accessmode", ConditionOperator.Equal, 0),
                            new ConditionExpression("fullname", ConditionOperator.DoesNotBeginWith, "#")
                        }
                    },
                    Orders = { new OrderExpression("fullname", OrderType.Ascending) }
                };

                var userResults = service.RetrieveMultiple(userQuery);

                // ── Step 4: Retrieve queue names ──
                var allQueueIds = userQueueMap.Values.SelectMany(q => q).Distinct().ToList();
                var queueNameMap = new Dictionary<Guid, string>();

                if (allQueueIds.Count > 0)
                {
                    var queueQuery = new QueryExpression("queue")
                    {
                        ColumnSet = new ColumnSet("queueid", "name"),
                        Criteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression("queueid", ConditionOperator.In,
                                    allQueueIds.Cast<object>().ToArray())
                            }
                        }
                    };

                    var queueResults = service.RetrieveMultiple(queueQuery);
                    foreach (var q in queueResults.Entities)
                    {
                        queueNameMap[q.Id] = q.GetAttributeValue<string>("name") ?? "Unknown Queue";
                    }
                }

                // ── Step 5: Build response ──
                var agents = new List<Dictionary<string, object>>();

                foreach (var user in userResults.Entities)
                {
                    var userId = user.Id;
                    var fullName = user.GetAttributeValue<string>("fullname") ?? "Unknown";
                    var jobTitle = user.GetAttributeValue<string>("jobtitle") ?? "Support Engineer";
                    var email = user.GetAttributeValue<string>("internalemailaddress") ?? "";

                    // Get the primary queue (first queue found) and all queue names
                    var userQueues = userQueueMap.ContainsKey(userId) ? userQueueMap[userId] : new List<Guid>();
                    var queueNames = userQueues
                        .Where(qid => queueNameMap.ContainsKey(qid))
                        .Select(qid => queueNameMap[qid])
                        .ToList();

                    var primaryQueue = queueNames.FirstOrDefault() ?? "Unassigned";

                    // Generate initials from full name
                    var nameParts = fullName.Split(' ');
                    var initials = nameParts.Length >= 2
                        ? $"{nameParts[0][0]}{nameParts[nameParts.Length - 1][0]}"
                        : fullName.Length >= 2 ? fullName.Substring(0, 2) : fullName;

                    agents.Add(new Dictionary<string, object>
                    {
                        { "id", userId.ToString() },
                        { "name", fullName },
                        { "initials", initials.ToUpper() },
                        { "role", jobTitle },
                        { "queue", primaryQueue },
                        { "queues", queueNames },
                        { "email", email }
                    });
                }

                var json = JsonSerializer.Serialize(agents, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                tracingService.Trace("GetMyAgents: Returning {0} agents", agents.Count);
                context.OutputParameters["AgentsJson"] = json;
            }
            catch (Exception ex)
            {
                tracingService.Trace("GetMyAgents Error: {0}", ex.ToString());
                throw new InvalidPluginExecutionException(
                    $"Error retrieving agents: {ex.Message}", ex);
            }
        }
    }
}