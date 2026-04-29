using Avalonia.Controls;
using Avalonia.Interactivity;
using Imvix.Services;

namespace Imvix.Views
{
    public partial class UpdateNotesWindow : Window
    {
        public UpdateNotesWindow()
        {
            InitializeComponent();
            WindowWorkAreaAdapter.Attach(this);
        }

        public UpdateNotesWindow(
            string title,
            string header,
            string summary,
            string improvementsTitle,
            string improvementsBody,
            string editionTitle,
            string editionBody,
            string supportTitle,
            string supportBody,
            string closeButtonText)
            : this()
        {
            Title = title;
            HeaderText.Text = header;
            SummaryText.Text = summary;
            ImprovementsTitleText.Text = improvementsTitle;
            ImprovementsBodyText.Text = improvementsBody;
            EditionTitleText.Text = editionTitle;
            EditionBodyText.Text = editionBody;
            SupportTitleText.Text = supportTitle;
            SupportBodyText.Text = supportBody;
            CloseButtonText.Text = closeButtonText;
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
