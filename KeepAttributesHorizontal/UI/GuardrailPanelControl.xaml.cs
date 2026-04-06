using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;
using KeepAttributesHorizontal.Validation;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;

namespace KeepAttributesHorizontal.UI
{
    public partial class GuardrailPanelControl : System.Windows.Controls.UserControl
    {
        private const string DefaultProjectId = "default-project";
        private const string DefaultRulePackVersion = "rp-default";

        private readonly ValidationApiClient _apiClient;
        private readonly GeometryListener _geometryListener;
        private bool _autoValidationEnabled = false;
        private bool _isValidating = false;
        private string? _latestRunId;

        // Color definitions
        private static readonly SolidColorBrush PassBackground = new(Color.FromRgb(45, 90, 45));
        private static readonly SolidColorBrush FailBackground = new(Color.FromRgb(90, 45, 45));
        private static readonly SolidColorBrush DegradedBackground = new(Color.FromRgb(90, 78, 35));
        private static readonly SolidColorBrush PassForeground = new(Color.FromRgb(78, 201, 78));
        private static readonly SolidColorBrush FailForeground = new(Color.FromRgb(201, 78, 78));
        private static readonly SolidColorBrush WarningForeground = new(Color.FromRgb(230, 180, 80));
        private static readonly SolidColorBrush NeutralForeground = new(Color.FromRgb(150, 150, 150));

        public GuardrailPanelControl()
        {
            InitializeComponent();

            _apiClient = new ValidationApiClient();
            _geometryListener = new GeometryListener();
            _geometryListener.GeometryChanged += OnGeometryChanged;

            Loaded += async (s, e) => await InitializeAsync();
        }

        public string SessionId => _geometryListener.SessionId;
        public string DrawingId => _geometryListener.DrawingId;
        public string ProjectId => DefaultProjectId;
        public string? LatestRunId => _latestRunId;

        private async Task InitializeAsync()
        {
            bool healthy = await CheckBackendHealthAsync();
            if (healthy)
            {
                await SubscribeGuardrailUpdatesAsync();
                await RefreshGuardrailStatusAsync();
            }
        }

        private async Task<bool> CheckBackendHealthAsync()
        {
            StatusText.Text = "Checking backend...";
            bool healthy = await _apiClient.IsHealthyAsync();

            if (healthy)
            {
                StatusText.Text = "Backend connected";
                StatusText.Foreground = PassForeground;
            }
            else
            {
                StatusText.Text = "Backend offline - Start the server";
                StatusText.Foreground = FailForeground;
            }

            return healthy;
        }

        private async Task SubscribeGuardrailUpdatesAsync()
        {
            var subscription = await _apiClient.SubscribeGuardrailUpdatesAsync(new GuardrailSubscriptionRequest
            {
                SessionId = SessionId,
                ProjectId = DefaultProjectId,
                PreferredMode = "poll"
            });

            if (subscription != null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Guardrail subscription active: {subscription.Mode} interval={subscription.PollIntervalSeconds}s channel={subscription.Channel}");
            }
        }

        private async void OnGeometryChanged(object? sender, GeometryChangedEventArgs e)
        {
            if (!_autoValidationEnabled) return;

            await Dispatcher.InvokeAsync(async () =>
            {
                await RunValidationAsync(e.Payload);
            });
        }

        private async void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isValidating) return;

            var payload = _geometryListener.ExtractGeometrySnapshot();
            await RunValidationAsync(payload);
        }

        private void ToggleAutoButton_Click(object sender, RoutedEventArgs e)
        {
            _autoValidationEnabled = !_autoValidationEnabled;

            if (_autoValidationEnabled)
            {
                _geometryListener.StartListening();
                ToggleAutoButton.Content = "Auto: ON";
                ToggleAutoButton.Background = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                StatusText.Text = "Auto-validation enabled";
            }
            else
            {
                _geometryListener.StopListening();
                ToggleAutoButton.Content = "Auto: OFF";
                ToggleAutoButton.Background = new SolidColorBrush(Color.FromRgb(62, 62, 66));
                StatusText.Text = "Auto-validation disabled";
            }
        }

        private async Task RunValidationAsync(GeometryPayload payload)
        {
            _isValidating = true;
            StatusText.Text = "Validating...";
            ValidateButton.IsEnabled = false;

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
                    StatusText.Text = "Validation failed - check backend";
                    StatusText.Foreground = FailForeground;
                    return;
                }

                _latestRunId = response.RunId;

                var guardrailStatus = await _apiClient.GetGuardrailStatusAsync(DefaultProjectId, SessionId);
                if (guardrailStatus != null)
                {
                    UpdateGuardrailStatus(guardrailStatus);

                    if (!string.IsNullOrWhiteSpace(guardrailStatus.RunId))
                    {
                        var run = await _apiClient.GetValidationRunAsync(guardrailStatus.RunId);
                        if (run != null)
                        {
                            UpdateViolationsList(run.Violations);
                        }
                        else
                        {
                            UpdateViolationsList(response.Violations);
                        }
                    }
                    else
                    {
                        UpdateViolationsList(response.Violations);
                    }
                }
                else
                {
                    UpdateUI(response);
                }

                StatusText.Text = $"Run {response.RunId} {response.Status}";
                StatusText.Foreground = NeutralForeground;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                StatusText.Foreground = FailForeground;
            }
            finally
            {
                _isValidating = false;
                ValidateButton.IsEnabled = true;
            }
        }

        private async Task RefreshGuardrailStatusAsync()
        {
            var status = await _apiClient.GetGuardrailStatusAsync(DefaultProjectId, SessionId);
            if (status == null)
            {
                return;
            }

            UpdateGuardrailStatus(status);

            if (!string.IsNullOrWhiteSpace(status.RunId))
            {
                var run = await _apiClient.GetValidationRunAsync(status.RunId);
                if (run != null)
                {
                    UpdateViolationsList(run.Violations);
                }
            }
        }

        private void UpdateGuardrailStatus(GuardrailStatusDto status)
        {
            int highCount = status.SeverityCounts.GetValueOrDefault("high", 0);
            int mediumCount = status.SeverityCounts.GetValueOrDefault("medium", 0);
            int lowCount = status.SeverityCounts.GetValueOrDefault("low", 0);
            int totalViolations = highCount + mediumCount + lowCount;

            if (status.RunStatus == "degraded")
            {
                OverallStatusBanner.Background = DegradedBackground;
                OverallStatusIcon.Text = "!";
                OverallStatusIcon.Foreground = WarningForeground;
                OverallStatusTitle.Text = "Validation Degraded";
                OverallStatusSubtitle.Text = string.Join(", ", status.DegradedReasonCodes.Count > 0 ? status.DegradedReasonCodes : new List<string> { "backend_warning" });
                ViolationCount.Text = totalViolations.ToString();
                ViolationCount.Foreground = WarningForeground;
            }
            else if (status.RunStatus == "idle")
            {
                OverallStatusBanner.Background = new SolidColorBrush(Color.FromRgb(55, 55, 55));
                OverallStatusIcon.Text = "-";
                OverallStatusIcon.Foreground = NeutralForeground;
                OverallStatusTitle.Text = "Waiting For Validation";
                OverallStatusSubtitle.Text = "No runs available yet";
                ViolationCount.Text = "0";
                ViolationCount.Foreground = NeutralForeground;
            }
            else if (totalViolations == 0)
            {
                OverallStatusBanner.Background = PassBackground;
                OverallStatusIcon.Text = "✓";
                OverallStatusIcon.Foreground = PassForeground;
                OverallStatusTitle.Text = "All Checks Passed";
                OverallStatusSubtitle.Text = $"{status.CategoryState.Count} categories checked";
                ViolationCount.Text = "0";
                ViolationCount.Foreground = PassForeground;
            }
            else
            {
                OverallStatusBanner.Background = FailBackground;
                OverallStatusIcon.Text = "✗";
                OverallStatusIcon.Foreground = FailForeground;
                OverallStatusTitle.Text = status.RunStatus == "superseded" ? "Run Superseded" : "Issues Found";
                OverallStatusSubtitle.Text = $"{highCount} high, {mediumCount} medium, {lowCount} low";
                ViolationCount.Text = totalViolations.ToString();
                ViolationCount.Foreground = FailForeground;
            }

            UpdateCategoryCards(status.CategoryState);
        }

        private void UpdateUI(ValidationResponse response)
        {
            // Update overall status banner
            int totalViolations = response.Violations.Count;
            int highCount = 0, mediumCount = 0, lowCount = 0;

            foreach (var cat in response.Categories)
            {
                if (cat.SeverityCounts.TryGetValue("high", out int h)) highCount += h;
                if (cat.SeverityCounts.TryGetValue("medium", out int m)) mediumCount += m;
                if (cat.SeverityCounts.TryGetValue("low", out int l)) lowCount += l;
            }

            if (totalViolations == 0)
            {
                OverallStatusBanner.Background = PassBackground;
                OverallStatusIcon.Text = "✓";
                OverallStatusIcon.Foreground = PassForeground;
                OverallStatusTitle.Text = "All Checks Passed";
                OverallStatusSubtitle.Text = $"{response.Categories.Count} categories checked";
                ViolationCount.Text = "0";
                ViolationCount.Foreground = PassForeground;
            }
            else
            {
                OverallStatusBanner.Background = FailBackground;
                OverallStatusIcon.Text = "✗";
                OverallStatusIcon.Foreground = FailForeground;
                OverallStatusTitle.Text = "Issues Found";
                OverallStatusSubtitle.Text = $"{highCount} high, {mediumCount} medium, {lowCount} low";
                ViolationCount.Text = totalViolations.ToString();
                ViolationCount.Foreground = FailForeground;
            }

            // Update category cards
            UpdateCategoryCards(response.Categories);

            // Update violations list
            UpdateViolationsList(response.Violations);
        }

        private void UpdateCategoryCards(List<ValidationCategory> categories)
        {
            CategoriesPanel.Children.Clear();

            foreach (var category in categories)
            {
                var card = CreateCategoryCard(category);
                CategoriesPanel.Children.Add(card);
            }
        }

        private Border CreateCategoryCard(ValidationCategory category)
        {
            bool isPassing = category.Status == "pass";
            int high = category.SeverityCounts.GetValueOrDefault("high", 0);
            int medium = category.SeverityCounts.GetValueOrDefault("medium", 0);
            int low = category.SeverityCounts.GetValueOrDefault("low", 0);
            int total = high + medium + low;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                BorderBrush = isPassing
                    ? new SolidColorBrush(Color.FromRgb(60, 90, 60))
                    : new SolidColorBrush(Color.FromRgb(90, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            // Status indicator
            var statusIcon = new TextBlock
            {
                Text = isPassing ? "✓" : "✗",
                FontSize = 16,
                Foreground = isPassing ? PassForeground : FailForeground,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(statusIcon, 0);

            // Category name and details
            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock
            {
                Text = category.Name,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229))
            });

            if (!isPassing)
            {
                var detailsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };

                if (high > 0)
                    detailsPanel.Children.Add(CreateSeverityBadge("H", high, FailForeground));
                if (medium > 0)
                    detailsPanel.Children.Add(CreateSeverityBadge("M", medium, WarningForeground));
                if (low > 0)
                    detailsPanel.Children.Add(CreateSeverityBadge("L", low, NeutralForeground));

                infoStack.Children.Add(detailsPanel);
            }
            Grid.SetColumn(infoStack, 1);

            // Count badge
            var countBadge = new Border
            {
                Background = isPassing
                    ? new SolidColorBrush(Color.FromRgb(45, 70, 45))
                    : new SolidColorBrush(Color.FromRgb(70, 45, 45)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            countBadge.Child = new TextBlock
            {
                Text = total.ToString(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = isPassing ? PassForeground : FailForeground
            };
            Grid.SetColumn(countBadge, 2);

            grid.Children.Add(statusIcon);
            grid.Children.Add(infoStack);
            grid.Children.Add(countBadge);

            border.Child = grid;
            return border;
        }

        private Border CreateSeverityBadge(string label, int count, SolidColorBrush color)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, color.Color.R, color.Color.G, color.Color.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 5, 0)
            };
            badge.Child = new TextBlock
            {
                Text = $"{label}:{count}",
                FontSize = 10,
                Foreground = color
            };
            return badge;
        }

        private void UpdateViolationsList(List<Violation> violations)
        {
            ViolationsPanel.Children.Clear();

            if (violations.Count == 0)
            {
                ViolationsPanel.Children.Add(new TextBlock
                {
                    Text = "No violations",
                    Foreground = NeutralForeground,
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            foreach (var violation in violations)
            {
                var item = CreateViolationItem(violation);
                ViolationsPanel.Children.Add(item);
            }
        }

        private Border CreateViolationItem(Violation violation)
        {
            var severityColor = violation.Severity switch
            {
                "high" => FailForeground,
                "medium" => WarningForeground,
                _ => NeutralForeground
            };

            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 0, 5)
            };

            var stack = new StackPanel();

            // Header with severity and category
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock
            {
                Text = $"[{violation.Severity.ToUpper()}]",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = severityColor,
                Margin = new Thickness(0, 0, 5, 0)
            });
            header.Children.Add(new TextBlock
            {
                Text = violation.Category,
                FontSize = 10,
                Foreground = NeutralForeground
            });

            // Message
            var message = new TextBlock
            {
                Text = violation.Message,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            };

            // Entity reference
            var entityRef = new TextBlock
            {
                Text = $"Entity: {violation.EntityRef}",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 2, 0, 0)
            };

            stack.Children.Add(header);
            stack.Children.Add(message);
            stack.Children.Add(entityRef);

            border.Child = stack;
            return border;
        }
    }
}
