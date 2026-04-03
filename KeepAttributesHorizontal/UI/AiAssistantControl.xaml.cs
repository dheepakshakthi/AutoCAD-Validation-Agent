using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;

namespace KeepAttributesHorizontal.UI
{
    public partial class AiAssistantControl : UserControl
    {
        private bool _isPlaceholderActive = true;

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

        private void SendMessage_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ChatInputBox.Text) || _isPlaceholderActive)
                return;

            string message = ChatInputBox.Text;
            ChatInputBox.Text = string.Empty;

            AddUserMessage(message);
            SimulateAiResponse("I am an AI assistant prototype. Your request has been queued for analysis.");
        }

        private void ValidateSop_Click(object sender, MouseButtonEventArgs e)
        {
            AddUserMessage("@workspace Validate against SOP");
            SimulateAiResponse("Analyzing current document against VARROC Standard Operating Procedures...");
            
            // Mock delay
            System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(2.0);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                AddBotMessage(
                    "? **Rule Violation Detected**:\n\n" +
                    "- Component: _Headlamp Housing Bracket_\n" +
                    "- Issue: Draft angle is 1.5°. Standard SOP requires a minimum of 3.0° for injection molding.\n\n" +
                    "?? **Suggestion**: Increase the draft angle on the mating faces.", true);
            };
            timer.Start();
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

        private void SimulateAiResponse(string initialMessage)
        {
            AddBotMessage(initialMessage, false);
            
            // Simulate processing time
            System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1.5);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
            };
            timer.Start();
        }

        private void AddBotMessage(string text, bool isErrorOrWarning)
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
                Text = "VARROC AI / Bot",
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204)), // #007ACC
                Margin = new Thickness(0, 0, 0, 5)
            });

            stack.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229))
            });

            border.Child = stack;
            MessagesPanel.Children.Add(border);
            ChatScroller.ScrollToBottom();
        }
    }
}