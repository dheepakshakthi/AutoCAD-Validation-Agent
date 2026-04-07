using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KeepAttributesHorizontal.Agent
{
    public class OrchestrationService
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private const string GroqApiKey = "";
        private readonly List<object> _conversationHistory = new List<object>();

        public Action<string, bool>? OnUpdateMessage { get; set; }

        public OrchestrationService()
        {
            _conversationHistory.Add(new
            {
                role = "system",
                content = "You are an AutoCAD AI Assistant. You have access to tools to modify and inspect the AutoCAD drawing. \n\nCRITICAL: When calling tools, you MUST pass numerical parameters (like startX, endY, etc.) as literal numbers, NOT as strings. For example, use 10.5, not \"10.5\". Do not wrap your response in XML tags. Respond concisely and explain your actions after tool execution."
            });
        }

        public async Task ProcessUserMessageAsync(string userMessage)
        {
            _conversationHistory.Add(new { role = "user", content = userMessage });

            bool isDone = false;
            while (!isDone)
            {
                var requestBody = new
                {
                    model = "meta-llama/llama-4-scout-17b-16e-instruct", // Keeping the user's preferred model
                    messages = _conversationHistory,
                    temperature = 0.1,
                    tools = GetToolDefinitions(),
                    tool_choice = "auto"
                };

                string jsonContent = JsonSerializer.Serialize(requestBody);
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GroqApiKey);
                requestMessage.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(requestMessage);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    OnUpdateMessage?.Invoke($"API Error: {err}", true);
                    return;
                }

                string responseBody = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseBody);
                var choice = jsonDoc.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");

                string? content = message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind != JsonValueKind.Null ? contentElement.GetString() : null;

                if (message.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
                {
                    var toolCallsObjList = new List<object>();
                    foreach (var tc in toolCallsElement.EnumerateArray())
                    {
                        var functionElement = tc.GetProperty("function");
                        toolCallsObjList.Add(new
                        {
                            id = tc.GetProperty("id").GetString(),
                            type = tc.GetProperty("type").GetString(),
                            function = new
                            {
                                name = functionElement.GetProperty("name").GetString(),
                                arguments = functionElement.GetProperty("arguments").GetString()
                            }
                        });
                    }

                    _conversationHistory.Add(new
                    {
                        role = "assistant",
                        content = content ?? "",
                        tool_calls = toolCallsObjList
                    });

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        OnUpdateMessage?.Invoke(content, false);
                    }

                    // Execute tools
                    foreach (var tc in toolCallsElement.EnumerateArray())
                    {
                        string toolId = tc.GetProperty("id").GetString()!;
                        string funcName = tc.GetProperty("function").GetProperty("name").GetString()!;
                        string arguments = tc.GetProperty("function").GetProperty("arguments").GetString()!;

                        string toolResult = "";
                        try
                        {
                            await Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.ExecuteInCommandContextAsync(async (obj) =>
                            {
                                if (funcName == "DrawLine")
                                {
                                    toolResult = AutoCADTools.DrawLine(arguments);
                                }
                                else if (funcName == "GetSelectedEntities")
                                {
                                    toolResult = AutoCADTools.GetSelectedEntities(arguments);
                                }
                                else
                                {
                                    toolResult = $"Error: Tool {funcName} not found.";
                                }
                                await Task.CompletedTask;
                            }, null);
                        }
                        catch (Exception ex)
                        {
                            toolResult = $"Tool execution error: {ex.Message}";
                        }

                        _conversationHistory.Add(new
                        {
                            role = "tool",
                            tool_call_id = toolId,
                            name = funcName,
                            content = toolResult
                        });
                    }
                }
                else
                {
                    _conversationHistory.Add(new
                    {
                        role = "assistant",
                        content = content ?? ""
                    });

                    OnUpdateMessage?.Invoke(content ?? "", false);
                    isDone = true;
                }
            }
        }

        private object[] GetToolDefinitions()
        {
            return new object[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "DrawLine",
                        description = "Draws a line in AutoCAD. All coordinate parameters MUST be numbers (e.g. 10.0), NOT strings.",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                startX = new { type = "number", description = "The X coordinate of the start point (number)" },
                                startY = new { type = "number", description = "The Y coordinate of the start point (number)" },
                                startZ = new { type = "number", description = "The Z coordinate of the start point (number)" },
                                endX = new { type = "number", description = "The X coordinate of the end point (number)" },
                                endY = new { type = "number", description = "The Y coordinate of the end point (number)" },
                                endZ = new { type = "number", description = "The Z coordinate of the end point (number)" }
                            },
                            required = new[] { "startX", "startY", "startZ", "endX", "endY", "endZ" }
                        }
                    }
                },
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "GetSelectedEntities",
                        description = "Gets the count of currently selected entities in the active AutoCAD drawing.",
                        parameters = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>(),
                            required = Array.Empty<string>()
                        }
                    }
                }
            };
        }
    }
}
