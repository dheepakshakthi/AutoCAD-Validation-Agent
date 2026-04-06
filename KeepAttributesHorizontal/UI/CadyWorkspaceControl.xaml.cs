using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KeepAttributesHorizontal.Validation;
using Color = System.Windows.Media.Color;

namespace KeepAttributesHorizontal.UI
{
    public partial class CadyWorkspaceControl : System.Windows.Controls.UserControl, IDisposable
    {
        private const string DefaultProjectId = "default-project";
        private const string DefaultRulePackVersion = "rp-default";

        private readonly ValidationApiClient _apiClient;
        private readonly GeometryListener _geometryListener;

        private readonly ObservableCollection<string> _agentFeed = new();
        private readonly ObservableCollection<string> _categories = new();
        private readonly ObservableCollection<string> _violations = new();
        private readonly ObservableCollection<string> _recommendations = new();
        private readonly ObservableCollection<string> _issues = new();
        private readonly ObservableCollection<string> _reports = new();
        private readonly ObservableCollection<string> _approvals = new();

        private bool _initialized;
        private bool _placeholderActive = true;
        private bool _validationInFlight;

        public CadyWorkspaceControl()
        {
            InitializeComponent();

            _apiClient = new ValidationApiClient();
            _geometryListener = new GeometryListener();

            AgentFeedList.ItemsSource = _agentFeed;
            CategoryList.ItemsSource = _categories;
            ViolationList.ItemsSource = _violations;
            RecommendationList.ItemsSource = _recommendations;
            IssueList.ItemsSource = _issues;
            ReportList.ItemsSource = _reports;
            ApprovalList.ItemsSource = _approvals;

            Loaded += OnWorkspaceLoaded;
            Unloaded += OnWorkspaceUnloaded;
        }

        // Backward-compatible alias targets for existing commands.
        public void ShowAssistantTab()
        {
            ChatInputBox.Focus();
        }

        // Backward-compatible alias targets for existing commands.
        public void ShowGuardrailTab()
        {
            ViolationList.Focus();
        }

        private async void OnWorkspaceLoaded(object sender, RoutedEventArgs e)
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                _initialized = true;
                _geometryListener.GeometryChanged += OnGeometryChanged;

                UpdateContextText();
                AppendFeed("Workspace loaded. Initializing unified tool orchestration.");

                await InitializeAsync();
            }
            catch (Exception ex)
            {
                AppendFeed($"Workspace initialization error: {ex.Message}");
            }
        }

        private void OnWorkspaceUnloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }

        private string SessionId => _geometryListener.SessionId;
        private string DrawingId => _geometryListener.DrawingId;

        private async Task InitializeAsync()
        {
            bool healthy = await _apiClient.IsHealthyAsync();
            if (!healthy)
            {
                BackendStatusText.Text = "Backend: offline";
                BackendStatusText.Foreground = new SolidColorBrush(Color.FromRgb(201, 78, 78));
                AppendFeed("Backend is offline. Start backend service to enable unified tool outputs.");
                return;
            }

            BackendStatusText.Text = "Backend: connected";
            BackendStatusText.Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 78));

            await _apiClient.SubscribeGuardrailUpdatesAsync(new GuardrailSubscriptionRequest
            {
                SessionId = SessionId,
                ProjectId = DefaultProjectId,
                PreferredMode = "poll"
            });

            _geometryListener.StartListening();
            AppendFeed("Automatic validation enabled. Listening to geometry changes.");

            await RunValidationAndRefreshAsync(_geometryListener.ExtractGeometrySnapshot(), "initial snapshot");
        }

        private async void OnGeometryChanged(object? sender, GeometryChangedEventArgs e)
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                // GeometryListener debounces on a timer thread, so route work to the WPF Dispatcher.
                await await Dispatcher.InvokeAsync(() => RunValidationAndRefreshAsync(e.Payload, "geometry delta"));
            }
            catch (Exception ex)
            {
                AppendFeed($"Geometry event handling error: {ex.Message}");
            }
        }

        private async Task RunValidationAndRefreshAsync(GeometryPayload payload, string reason)
        {
            if (_validationInFlight)
            {
                return;
            }

            _validationInFlight = true;
            try
            {
                var request = ValidationRequest.FromPayload(
                    payload,
                    DefaultProjectId,
                    DrawingId,
                    DefaultRulePackVersion);

                var response = await _apiClient.RunValidationAsync(request);
                if (response == null)
                {
                    AppendFeed("run_validation failed. Guardrail state may be stale.");
                    return;
                }

                AppendFeed($"run_validation completed for {response.RunId} ({reason}).");
                await RefreshUnifiedAgentAsync($"Auto refresh from {reason}");
            }
            finally
            {
                _validationInFlight = false;
            }
        }

        private async Task RefreshUnifiedAgentAsync(string message)
        {
            UpdateContextText();

            var response = await _apiClient.QueryUnifiedAgentAsync(new UnifiedAgentQueryRequest
            {
                SessionId = SessionId,
                ProjectId = DefaultProjectId,
                DrawingId = DrawingId,
                Message = message
            });

            if (response == null)
            {
                AppendFeed("Unified agent query failed.");
                return;
            }

            ApplyUnifiedResponse(response);
        }

        private void ApplyUnifiedResponse(UnifiedAgentResponse response)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ApplyUnifiedResponse(response));
                return;
            }

            int high = response.GuardrailStatus.SeverityCounts.GetValueOrDefault("high", 0);
            int medium = response.GuardrailStatus.SeverityCounts.GetValueOrDefault("medium", 0);
            int low = response.GuardrailStatus.SeverityCounts.GetValueOrDefault("low", 0);
            int total = high + medium + low;

            RunStatusText.Text = $"Run status: {response.GuardrailStatus.RunStatus}";
            RunCountsText.Text = $"High: {high} | Medium: {medium} | Low: {low} | Total: {total}";
            UpdateBannerColor(response.GuardrailStatus.RunStatus, total, high);

            _categories.Clear();
            foreach (var category in response.GuardrailStatus.CategoryState)
            {
                int categoryHigh = category.SeverityCounts.GetValueOrDefault("high", 0);
                int categoryMedium = category.SeverityCounts.GetValueOrDefault("medium", 0);
                int categoryLow = category.SeverityCounts.GetValueOrDefault("low", 0);
                _categories.Add(
                    $"{category.Name}: {category.Status} | H:{categoryHigh} M:{categoryMedium} L:{categoryLow}");
            }

            _violations.Clear();
            if (response.LatestRun?.Violations?.Count > 0)
            {
                foreach (var violation in response.LatestRun.Violations)
                {
                    _violations.Add(
                        $"[{violation.Severity.ToUpper()}] {violation.Category} | {violation.Message} | Entity {violation.EntityRef}");
                }
            }
            else
            {
                _violations.Add("No violations in latest run.");
            }

            _recommendations.Clear();
            if (response.Recommendations.Count > 0)
            {
                foreach (var recommendation in response.Recommendations)
                {
                    string approvalFlag = recommendation.RequiresApproval ? " | approval required" : string.Empty;
                    _recommendations.Add(
                        $"{recommendation.Title} ({recommendation.Confidence:P0}){approvalFlag} | {recommendation.Rationale}");
                }
            }
            else
            {
                _recommendations.Add("No recommendation cards available.");
            }

            _issues.Clear();
            if (response.Issues.Count > 0)
            {
                foreach (var issue in response.Issues)
                {
                    _issues.Add($"{issue.IssueId} | {issue.Status} | {issue.Severity} | {issue.Title}");
                }
            }
            else
            {
                _issues.Add("No issues tracked for this session.");
            }

            _reports.Clear();
            if (response.Reports.Count > 0)
            {
                foreach (var report in response.Reports)
                {
                    _reports.Add($"{report.ReportId} | {report.Status} | Score {report.ComplianceScore:F1}");
                }
            }
            else
            {
                _reports.Add("No reports generated yet.");
            }

            _approvals.Clear();
            if (response.Approvals.Count > 0)
            {
                foreach (var approval in response.Approvals)
                {
                    _approvals.Add($"{approval.ApprovalId} | {approval.Status} | {approval.Title}");
                }
            }
            else
            {
                _approvals.Add("No pending approvals.");
            }

            if (response.ExecutedTools.Count > 0)
            {
                AppendFeed($"Agent tools: {string.Join(", ", response.ExecutedTools)}");
            }
            AppendFeed($"CADY: {response.Answer}");
        }

        private void UpdateBannerColor(string runStatus, int totalViolations, int highSeverityCount)
        {
            if (runStatus == "degraded")
            {
                RunStatusBanner.Background = new SolidColorBrush(Color.FromRgb(90, 78, 35));
                return;
            }

            if (runStatus == "idle")
            {
                RunStatusBanner.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                return;
            }

            if (runStatus == "superseded")
            {
                RunStatusBanner.Background = new SolidColorBrush(Color.FromRgb(70, 70, 35));
                return;
            }

            if (totalViolations == 0)
            {
                RunStatusBanner.Background = new SolidColorBrush(Color.FromRgb(45, 90, 45));
                return;
            }

            RunStatusBanner.Background = highSeverityCount > 0
                ? new SolidColorBrush(Color.FromRgb(110, 45, 45))
                : new SolidColorBrush(Color.FromRgb(90, 60, 45));
        }

        private void UpdateContextText()
        {
            ContextText.Text =
                $"Project: {DefaultProjectId} | Session: {SessionId} | Drawing: {DrawingId}";
        }

        private void AppendFeed(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendFeed(text));
                return;
            }

            _agentFeed.Add($"[{DateTime.Now:HH:mm:ss}] {text}");

            while (_agentFeed.Count > 120)
            {
                _agentFeed.RemoveAt(0);
            }

            if (_agentFeed.Count > 0)
            {
                AgentFeedList.ScrollIntoView(_agentFeed.Last());
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SubmitMessageAsync();
            }
            catch (Exception ex)
            {
                AppendFeed($"Message send failed: {ex.Message}");
            }
        }

        private async Task SubmitMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(ChatInputBox.Text) || _placeholderActive)
            {
                return;
            }

            string message = ChatInputBox.Text.Trim();
            ChatInputBox.Text = string.Empty;

            AppendFeed($"You: {message}");
            await RefreshUnifiedAgentAsync(message);
        }

        private async void ChatInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                try
                {
                    await SubmitMessageAsync();
                }
                catch (Exception ex)
                {
                    AppendFeed($"Message submit failed: {ex.Message}");
                }
            }
        }

        private void ChatInputBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_placeholderActive)
            {
                ChatInputBox.Text = string.Empty;
                ChatInputBox.Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229));
                _placeholderActive = false;
            }
        }

        private void ChatInputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ChatInputBox.Text))
            {
                return;
            }

            _placeholderActive = true;
            ChatInputBox.Text =
                "Ask CADY anything about compliance, issues, recommendations, reports, or approvals";
            ChatInputBox.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        }

        public void Dispose()
        {
            _geometryListener.GeometryChanged -= OnGeometryChanged;
            _geometryListener.Dispose();
        }
    }
}
