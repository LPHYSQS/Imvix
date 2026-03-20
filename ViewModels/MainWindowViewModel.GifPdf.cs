using ImvixPro.Models;

namespace ImvixPro.ViewModels
{
    public partial class MainWindowViewModel
    {
        public bool IsGifPdfModeSelectorVisible =>
            IsGifPreviewVisible &&
            SelectedOutputFormat == OutputImageFormat.Pdf &&
            SelectedImage?.IsAnimatedGif == true;

        public bool IsGifHandlingSelectorVisible => IsGifPreviewVisible && !IsGifPdfModeSelectorVisible;

        public bool IsGifPdfExportAllFramesMode => IsGifPdfModeSelectorVisible && SelectedGifHandlingMode == GifHandlingMode.AllFrames;

        public bool IsGifPdfExportCurrentFrameMode => IsGifPdfModeSelectorVisible && SelectedGifHandlingMode == GifHandlingMode.SpecificFrame;

        public string GifPdfExportAllFramesText => T("GifPdfExportAllFrames");

        public string GifPdfExportCurrentFrameText => T("GifPdfExportCurrentFrame");

        public string GifPdfOutputDirectoryNoteText => T("GifPdfOutputDirectoryNote");

        public void SetGifPdfExportMode(GifHandlingMode mode)
        {
            if (mode is not (GifHandlingMode.AllFrames or GifHandlingMode.SpecificFrame))
            {
                return;
            }

            if (SelectedGifHandlingMode != mode)
            {
                SelectedGifHandlingMode = mode;
                return;
            }

            RefreshGifPdfUiState();
        }

        private void RefreshGifPdfUiState()
        {
            OnPropertyChanged(nameof(IsGifPdfModeSelectorVisible));
            OnPropertyChanged(nameof(IsGifHandlingSelectorVisible));
            OnPropertyChanged(nameof(IsGifPdfExportAllFramesMode));
            OnPropertyChanged(nameof(IsGifPdfExportCurrentFrameMode));
        }

        private void RefreshGifPdfLocalizedProperties()
        {
            OnPropertyChanged(nameof(GifPdfExportAllFramesText));
            OnPropertyChanged(nameof(GifPdfExportCurrentFrameText));
            OnPropertyChanged(nameof(GifPdfOutputDirectoryNoteText));
            OnPropertyChanged(nameof(GifSpecificFrameCountdownText));
            RefreshGifPdfUiState();
        }
    }
}
