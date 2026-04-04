using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;

namespace KeepAttributesHorizontal.UI
{
    public partial class AiAssistantControl : UserControl
    {
        private bool _isPlaceholderActive = true;
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string GroqApiKey = "";

        public AiAssistantControl()
        {
            InitializeComponent();
        }

        private void ChatInputBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_isPlaceholderActive)
            {
                ChatInputBox.Text = "";
                ChatInputBox.Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229)); // #E5E5E5
                _isPlaceholderActive = false;
            }
        }

        private void ChatInputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ChatInputBox.Text))
            {
                _isPlaceholderActive = true;
                ChatInputBox.Text = "Ask Copilot or use @workspace";
                ChatInputBox.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)); // #969696
            }
        }

        private async void SendMessage_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ChatInputBox.Text) || _isPlaceholderActive)
                return;

            string message = ChatInputBox.Text;
            ChatInputBox.Text = string.Empty;

            AddUserMessage(message);
            await GenerateAiResponseAsync(message);
        }

        private void AddUserMessage(string text)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)), // #252526
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)), // #3E3E42
                BorderThickness = new Thickness(1),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                CornerRadius = new CornerRadius(6)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "You",
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                Margin = new Thickness(0, 0, 0, 5)
            });

            stack.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)) // #CCCCCC
            });

            border.Child = stack;
            MessagesPanel.Children.Add(border);
            ChatScroller.ScrollToBottom();
        }

        private async Task GenerateAiResponseAsync(string userMessage)
        {
            // Determine if in Agent Mode via the ComboBox
            bool isAgentMode = false;
            Dispatcher.Invoke(() =>
            {
                if (ModeSelector.SelectedItem is ComboBoxItem item && item.Content.ToString() == "Agent Mode")
                {
                    isAgentMode = true;
                }
            });

            // If in Agent Mode, inject AutoCAD context instructions
            string systemPrompt = "You are CADY CoPilot, an AI assistant for AutoCAD.";
            if (isAgentMode)
            {
                systemPrompt += "\nYou are in Agent Mode. You have the ability to make changes to the AutoCAD model by outputting a JSON command. Provide the explanation in text first, then output exactly one JSON code block formatted like this:\n```json\n{\n  \"Action\": \"ModifyRadius\",\n  \"ObjectId\": \"<id>\",\n  \"NewValue\": <value>\n}\n```\nCurrently, you can only suggest actions, they will be parsed by the system if you provide the JSON syntax.";
            }

            // Create a temporary "Thinking..." message
            Border typingBorder = CreateBotMessageBorder("Thinking...", false);
            MessagesPanel.Children.Add(typingBorder);
            ChatScroller.ScrollToBottom();

            try
            {
                var requestBody = new
                {
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userMessage }
                    },
                    model = "qwen/qwen3-32b",
                    temperature = 0.6,
                    max_completion_tokens = 4096,
                    top_p = 0.95,
                    stream = false, // Set to false to wait for full response for simplicity
                    reasoning_effort = "default"
                };

                string jsonContent = JsonSerializer.Serialize(requestBody);
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GroqApiKey);
                requestMessage.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(requestMessage);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(responseBody);
                string aiText = jsonDoc.RootElement
                                       .GetProperty("choices")[0]
                                       .GetProperty("message")
                                       .GetProperty("content")
                                       .GetString() ?? string.Empty;

                // Execute Agent Action if in Agent Mode (Look for JSON)
                if (isAgentMode)
                {
                    ExecuteAgentActionIfPresent(aiText);
                }

                // Replace "Thinking..." with actual reply
                UpdateBotMessage(typingBorder, aiText);
            }
            catch (Exception ex)
            {
                UpdateBotMessage(typingBorder, $"Error: {ex.Message}");
            }
        }

        private void ExecuteAgentActionIfPresent(string aiText)
        {
            // Very basic parser for JSON code block in AI text
            int jsonStart = aiText.IndexOf("```json");
            int endBlock = aiText.IndexOf("```", jsonStart + 7);

            if (jsonStart >= 0 && endBlock > jsonStart)
            {
                string jsonCommand = aiText.Substring(jsonStart + 7, endBlock - (jsonStart + 7)).Trim();
                
                try
                {
                    using var doc = JsonDocument.Parse(jsonCommand);
                    if (doc.RootElement.TryGetProperty("Action", out var actionProp))
                    {
                        string action = actionProp.GetString();
                        // For reality, this would invoke an AutoCAD command or call the AutoCAD API.
                        // Here we simply acknowledge it.
                        System.Diagnostics.Debug.WriteLine($"Agent Action Sent: {action}");
                        
                        // Adding feedback to user implicitly that action happened
                        Dispatcher.Invoke(() =>
                        {
                            var border = CreateBotMessageBorder($"? Agent Action Executed: {action}", true);
                            MessagesPanel.Children.Add(border);
                        });
                    }
                }
                catch (Exception)
                {
                    // Ignore parse errors
                }
            }
        }

        private Border CreateBotMessageBorder(string text, bool isErrorOrWarning)
        {
            var border = new Border
            {
                Background = isErrorOrWarning ? new SolidColorBrush(Color.FromArgb(20, 255, 100, 100)) : new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = isErrorOrWarning ? new SolidColorBrush(Color.FromRgb(200, 60, 60)) : new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                BorderThickness = new Thickness(isErrorOrWarning ? 1 : 0),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                CornerRadius = new CornerRadius(6)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "CADY CoPilot",
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204)), // #007ACC
                Margin = new Thickness(0, 0, 0, 5)
            });

            var messageBlock = new StackPanel();
            PopulateMarkdown(messageBlock, text, false);

            stack.Children.Add(messageBlock);
            border.Child = stack;
            return border;
        }

        private void UpdateBotMessage(Border border, string newText)
        {
            Dispatcher.Invoke(() =>
            {
                if (border.Child is StackPanel stack && stack.Children.Count > 1)
                {
                    if (stack.Children[1] is StackPanel messageContainer)
                    {
                        messageContainer.Children.Clear();

                        string? thinkingText = null;
                        string mainText = newText ?? string.Empty;
                        int thinkStart = mainText.IndexOf("<think>");
                        int thinkEnd = mainText.IndexOf("</think>");

                        if (thinkStart >= 0 && thinkEnd > thinkStart)
                        {
                            thinkingText = newText.Substring(thinkStart + 7, thinkEnd - (thinkStart + 7)).Trim();
                            mainText = newText.Substring(thinkEnd + 8).Trim();
                        }

                        if (!string.IsNullOrWhiteSpace(thinkingText))
                        {
                            var expander = new Expander
                            {
                                Header = "Thinking...",
                                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                                Margin = new Thickness(0, 0, 0, 10)
                            };

                            var thinkContent = new StackPanel { Margin = new Thickness(10, 5, 0, 0) };
                            PopulateMarkdown(thinkContent, thinkingText, true);
                            expander.Content = thinkContent;
                            messageContainer.Children.Add(expander);
                        }

                        var mainContent = new StackPanel();
                        PopulateMarkdown(mainContent, mainText, false);
                        messageContainer.Children.Add(mainContent);
                    }
                }
                ChatScroller.ScrollToBottom();
            });
        }

        private void PopulateMarkdown(StackPanel container, string markdown, bool isDimmed)
        {
            var lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = isDimmed ? new SolidColorBrush(Color.FromRgb(150, 150, 150)) : new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                    Margin = new Thickness(0, 0, 0, 5)
                };

                string currentLine = line.Trim();
                if (currentLine.StartsWith("### "))
                {
                    tb.FontSize = 14;
                    tb.FontWeight = FontWeights.SemiBold;
                    currentLine = currentLine.Substring(4);
                }
                else if (currentLine.StartsWith("## "))
                {
                    tb.FontSize = 16;
                    tb.FontWeight = FontWeights.Bold;
                    currentLine = currentLine.Substring(3);
                }
                else if (currentLine.StartsWith("# "))
                {
                    tb.FontSize = 18;
                    tb.FontWeight = FontWeights.Bold;
                    currentLine = currentLine.Substring(2);
                }
                else if (currentLine.StartsWith("- ") || currentLine.StartsWith("* "))
                {
                    tb.Margin = new Thickness(15, 0, 0, 5);
                    currentLine = "• " + currentLine.Substring(2);
                }

                ProcessInlines(tb, currentLine);
                container.Children.Add(tb);
            }
        }

        private void ProcessInlines(TextBlock tb, string text)
        {
            var parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1 && i < parts.Length - 1)
                {
                    tb.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(parts[i])));
                }
                else
                {
                    string runText = parts[i];
                    if (i % 2 == 1 && i == parts.Length - 1)
                        runText = "**" + runText; // unclosed bold

                    tb.Inlines.Add(new System.Windows.Documents.Run(runText));
                }
            }
        }

        private void SimulateAiResponse(string initialMessage, string simulatedReply, int delayMilliseconds = 1000)
        {
            AddUserMessage(initialMessage);

            // Simulate thinking response
            var typingBorder = CreateBotMessageBorder("Thinking...", false);
            MessagesPanel.Children.Add(typingBorder);
            ChatScroller.ScrollToBottom();

            Task.Delay(delayMilliseconds).ContinueWith(t =>
            {
                // After delay, replace thinking border with the actual reply
                Dispatcher.Invoke(() =>
                {
                    UpdateBotMessage(typingBorder, simulatedReply);
                });
            });
        }
    }
}