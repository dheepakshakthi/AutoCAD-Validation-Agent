using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace KeepAttributesHorizontal.Validation
{
    /// <summary>
    /// HTTP client for communicating with the backend tool APIs.
    /// </summary>
    public class ValidationApiClient
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public ValidationApiClient(string baseUrl = "http://127.0.0.1:8000")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            if (!HttpClient.DefaultRequestHeaders.Accept.Contains(new MediaTypeWithQualityHeaderValue("application/json")))
            {
                HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
        }

        /// <summary>
        /// Check if the backend service is healthy.
        /// </summary>
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                using var response = await HttpClient.GetAsync(BuildUrl("/health"));
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Legacy helper. Converts GeometryPayload to canonical ValidationRequest and calls run_validation.
        /// </summary>
        public async Task<ValidationResponse?> ValidateAsync(GeometryPayload payload)
        {
            var request = ValidationRequest.FromPayload(payload);
            return await RunValidationAsync(request);
        }

        /// <summary>
        /// Execute deterministic validation using the typed tool endpoint.
        /// </summary>
        public async Task<ValidationResponse?> RunValidationAsync(ValidationRequest request)
        {
            return await PostJsonAsync<ValidationRequest, ValidationResponse>("/tools/run_validation", request);
        }

        /// <summary>
        /// Query the latest guardrail status for a project/session pair.
        /// </summary>
        public async Task<GuardrailStatusDto?> GetGuardrailStatusAsync(string projectId, string sessionId)
        {
            string route =
                $"/projects/{Uri.EscapeDataString(projectId)}/sessions/{Uri.EscapeDataString(sessionId)}/guardrail-status";
            return await GetJsonAsync<GuardrailStatusDto>(route);
        }

        /// <summary>
        /// Retrieve a validation run by run id.
        /// </summary>
        public async Task<ValidationRunRecord?> GetValidationRunAsync(string runId)
        {
            return await GetJsonAsync<ValidationRunRecord>($"/validation-runs/{Uri.EscapeDataString(runId)}");
        }

        /// <summary>
        /// Subscribe the workspace to guardrail updates (poll mode for MVP).
        /// </summary>
        public async Task<GuardrailSubscriptionResponse?> SubscribeGuardrailUpdatesAsync(GuardrailSubscriptionRequest request)
        {
            return await PostJsonAsync<GuardrailSubscriptionRequest, GuardrailSubscriptionResponse>(
                "/tools/subscribe_guardrail_updates",
                request);
        }

        /// <summary>
        /// Get deterministic chat guidance from the backend orchestration endpoint.
        /// </summary>
        public async Task<AgentChatResponse?> GetAgentChatResponseAsync(AgentChatRequest request)
        {
            return await PostJsonAsync<AgentChatRequest, AgentChatResponse>("/agent/chat", request);
        }

        /// <summary>
        /// Query unified agent outputs for all workspace sections in one request.
        /// </summary>
        public async Task<UnifiedAgentResponse?> QueryUnifiedAgentAsync(UnifiedAgentQueryRequest request)
        {
            return await PostJsonAsync<UnifiedAgentQueryRequest, UnifiedAgentResponse>("/agent/unified-query", request);
        }

        /// <summary>
        /// Get list of available rules.
        /// </summary>
        public async Task<RuleListResponse?> GetRulesAsync()
        {
            return await GetJsonAsync<RuleListResponse>("/rules");
        }

        private string BuildUrl(string route)
        {
            if (route.StartsWith('/'))
            {
                return $"{_baseUrl}{route}";
            }

            return $"{_baseUrl}/{route}";
        }

        private async Task<TResponse?> GetJsonAsync<TResponse>(string route)
        {
            try
            {
                using var response = await HttpClient.GetAsync(BuildUrl(route));
                response.EnsureSuccessStatusCode();

                string body = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<TResponse>(body, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GET {route} failed: {ex.Message}");
                return default;
            }
        }

        private async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(string route, TRequest payload)
        {
            try
            {
                string json = JsonSerializer.Serialize(payload, _jsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await HttpClient.PostAsync(BuildUrl(route), content);
                response.EnsureSuccessStatusCode();

                string body = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<TResponse>(body, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"POST {route} failed: {ex.Message}");
                return default;
            }
        }
    }

    #region Data Models

    public class EntityProperties
    {
        [JsonPropertyName("radius")]
        public double? Radius { get; set; }

        [JsonPropertyName("center")]
        public List<double>? Center { get; set; }

        [JsonPropertyName("start_point")]
        public List<double>? StartPoint { get; set; }

        [JsonPropertyName("end_point")]
        public List<double>? EndPoint { get; set; }

        [JsonPropertyName("length")]
        public double? Length { get; set; }

        [JsonPropertyName("start_angle")]
        public double? StartAngle { get; set; }

        [JsonPropertyName("end_angle")]
        public double? EndAngle { get; set; }

        [JsonPropertyName("text_height")]
        public double? TextHeight { get; set; }

        [JsonPropertyName("text_content")]
        public string? TextContent { get; set; }

        [JsonPropertyName("area")]
        public double? Area { get; set; }

        [JsonPropertyName("color")]
        public int? Color { get; set; }
    }

    public class Entity
    {
        [JsonPropertyName("handle")]
        public string Handle { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("layer")]
        public string Layer { get; set; } = "";

        [JsonPropertyName("properties")]
        public EntityProperties Properties { get; set; } = new();
    }

    public class GeometryPayload
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("geometry_version")]
        public string GeometryVersion { get; set; } = "v1";

        [JsonPropertyName("entities")]
        public List<Entity> Entities { get; set; } = new();
    }

    public class ValidationRequest
    {
        [JsonPropertyName("run_id")]
        public string RunId { get; set; } = "";

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "default-project";

        [JsonPropertyName("drawing_id")]
        public string DrawingId { get; set; } = "active-drawing";

        [JsonPropertyName("geometry_version_id")]
        public string GeometryVersionId { get; set; } = "v1";

        [JsonPropertyName("rule_pack_version")]
        public string RulePackVersion { get; set; } = "rp-default";

        [JsonPropertyName("correlation_id")]
        public string CorrelationId { get; set; } = "";

        [JsonPropertyName("requested_at")]
        public DateTime RequestedAt { get; set; }

        [JsonPropertyName("entities")]
        public List<Entity> Entities { get; set; } = new();

        public static ValidationRequest FromPayload(
            GeometryPayload payload,
            string projectId = "default-project",
            string drawingId = "active-drawing",
            string rulePackVersion = "rp-default")
        {
            return new ValidationRequest
            {
                RunId = $"run_{Guid.NewGuid().ToString("N").Substring(0, 10)}",
                SessionId = payload.SessionId,
                ProjectId = projectId,
                DrawingId = drawingId,
                GeometryVersionId = payload.GeometryVersion,
                RulePackVersion = rulePackVersion,
                CorrelationId = $"corr_{Guid.NewGuid().ToString("N").Substring(0, 12)}",
                RequestedAt = DateTime.UtcNow,
                Entities = payload.Entities
            };
        }
    }

    public class Violation
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("rule_id")]
        public string RuleId { get; set; } = "";

        [JsonPropertyName("category")]
        public string Category { get; set; } = "";

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("entity_ref")]
        public string EntityRef { get; set; } = "";
    }

    public class ValidationCategory
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("severity_counts")]
        public Dictionary<string, int> SeverityCounts { get; set; } = new();
    }

    public class ValidationResponse
    {
        [JsonPropertyName("run_id")]
        public string RunId { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("categories")]
        public List<ValidationCategory> Categories { get; set; } = new();

        [JsonPropertyName("violations")]
        public List<Violation> Violations { get; set; } = new();

        [JsonPropertyName("rule_pack_version")]
        public string RulePackVersion { get; set; } = "";

        [JsonPropertyName("worker_build_hash")]
        public string WorkerBuildHash { get; set; } = "";

        [JsonPropertyName("severity_counts")]
        public Dictionary<string, int> SeverityCounts { get; set; } = new();

        [JsonPropertyName("degraded_reason_codes")]
        public List<string> DegradedReasonCodes { get; set; } = new();
    }

    public class GuardrailStatusDto
    {
        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("run_id")]
        public string? RunId { get; set; }

        [JsonPropertyName("run_status")]
        public string RunStatus { get; set; } = "";

        [JsonPropertyName("category_state")]
        public List<ValidationCategory> CategoryState { get; set; } = new();

        [JsonPropertyName("severity_counts")]
        public Dictionary<string, int> SeverityCounts { get; set; } = new();

        [JsonPropertyName("degraded_reason_codes")]
        public List<string> DegradedReasonCodes { get; set; } = new();

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    public class ValidationRunRecord
    {
        [JsonPropertyName("run_id")]
        public string RunId { get; set; } = "";

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("drawing_id")]
        public string DrawingId { get; set; } = "";

        [JsonPropertyName("geometry_version_id")]
        public string GeometryVersionId { get; set; } = "";

        [JsonPropertyName("rule_pack_version")]
        public string RulePackVersion { get; set; } = "";

        [JsonPropertyName("worker_build_hash")]
        public string WorkerBuildHash { get; set; } = "";

        [JsonPropertyName("correlation_id")]
        public string CorrelationId { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("requested_at")]
        public DateTime RequestedAt { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("ended_at")]
        public DateTime? EndedAt { get; set; }

        [JsonPropertyName("categories")]
        public List<ValidationCategory> Categories { get; set; } = new();

        [JsonPropertyName("violations")]
        public List<Violation> Violations { get; set; } = new();

        [JsonPropertyName("severity_counts")]
        public Dictionary<string, int> SeverityCounts { get; set; } = new();

        [JsonPropertyName("degraded_reason_codes")]
        public List<string> DegradedReasonCodes { get; set; } = new();
    }

    public class GuardrailSubscriptionRequest
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "default-project";

        [JsonPropertyName("preferred_mode")]
        public string PreferredMode { get; set; } = "poll";
    }

    public class GuardrailSubscriptionResponse
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "poll";

        [JsonPropertyName("poll_interval_seconds")]
        public int PollIntervalSeconds { get; set; }

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = "";
    }

    public class AgentChatRequest
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "default-project";

        [JsonPropertyName("drawing_id")]
        public string DrawingId { get; set; } = "active-drawing";

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "Design Validation Mode";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    public class AgentChatResponse
    {
        [JsonPropertyName("answer")]
        public string Answer { get; set; } = "";

        [JsonPropertyName("suggested_tools")]
        public List<string> SuggestedTools { get; set; } = new();
    }

    public class RecommendationOption
    {
        [JsonPropertyName("option_id")]
        public string OptionId { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("rationale")]
        public string Rationale { get; set; } = "";

        [JsonPropertyName("target_entity_ref")]
        public string? TargetEntityRef { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("requires_approval")]
        public bool RequiresApproval { get; set; }
    }

    public class IssueRecord
    {
        [JsonPropertyName("issue_id")]
        public string IssueId { get; set; } = "";

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("run_id")]
        public string RunId { get; set; } = "";

        [JsonPropertyName("violation_id")]
        public string ViolationId { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("assignee")]
        public string? Assignee { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    public class ReportRecord
    {
        [JsonPropertyName("report_id")]
        public string ReportId { get; set; } = "";

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("run_id")]
        public string RunId { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("compliance_score")]
        public double ComplianceScore { get; set; }

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = "";

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    public class ApprovalItem
    {
        [JsonPropertyName("approval_id")]
        public string ApprovalId { get; set; } = "";

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("item_type")]
        public string ItemType { get; set; } = "";

        [JsonPropertyName("ref_id")]
        public string RefId { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("requested_at")]
        public DateTime RequestedAt { get; set; }
    }

    public class UnifiedAgentQueryRequest
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "default-project";

        [JsonPropertyName("drawing_id")]
        public string DrawingId { get; set; } = "active-drawing";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    public class UnifiedAgentResponse
    {
        [JsonPropertyName("answer")]
        public string Answer { get; set; } = "";

        [JsonPropertyName("executed_tools")]
        public List<string> ExecutedTools { get; set; } = new();

        [JsonPropertyName("guardrail_status")]
        public GuardrailStatusDto GuardrailStatus { get; set; } = new();

        [JsonPropertyName("latest_run")]
        public ValidationRunRecord? LatestRun { get; set; }

        [JsonPropertyName("recommendations")]
        public List<RecommendationOption> Recommendations { get; set; } = new();

        [JsonPropertyName("issues")]
        public List<IssueRecord> Issues { get; set; } = new();

        [JsonPropertyName("reports")]
        public List<ReportRecord> Reports { get; set; } = new();

        [JsonPropertyName("approvals")]
        public List<ApprovalItem> Approvals { get; set; } = new();
    }

    public class RuleDefinition
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("category")]
        public string Category { get; set; } = "";

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("entity_types")]
        public List<string> EntityTypes { get; set; } = new();

        [JsonPropertyName("condition")]
        public Dictionary<string, JsonElement> Condition { get; set; } = new();
    }

    public class RuleListResponse
    {
        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();

        [JsonPropertyName("rules")]
        public List<RuleDefinition> Rules { get; set; } = new();

        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }
    }

    #endregion
}
