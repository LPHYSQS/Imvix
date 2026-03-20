using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ImvixPro.Views
{
    public partial class UpdateNotesWindow : Window
    {
        public UpdateNotesWindow()
        {
            InitializeComponent();
        }

        public UpdateNotesWindow(
            string title,
            string header,
            string summary,
            string fixesTitle,
            string fixesBody,
            string featuresTitle,
            string featuresBody,
            string closeButtonText)
            : this()
        {
            Title = title;
            HeaderText.Text = header;
            SummaryText.Text = summary;
            FixesTitleText.Text = fixesTitle;
            FixesBodyText.Text = fixesBody;
            FeaturesTitleText.Text = featuresTitle;
            FeaturesBodyText.Text = featuresBody;
            CloseButtonText.Text = closeButtonText;
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
