using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Imvix.Services;
using System;
using System.IO;

namespace Imvix.Views
{
    public partial class ImagePreviewWindow : Window
    {
        private Bitmap? _previewBitmap;

        public ImagePreviewWindow()
        {
            InitializeComponent();
        }

        public ImagePreviewWindow(string filePath, bool svgUseBackground, string svgBackgroundColor)
            : this()
        {

            var fileName = Path.GetFileName(filePath);
            Title = fileName;
            FileNameText.Text = fileName;

            _previewBitmap = ImageConversionService.TryCreatePreview(filePath, 2200, svgUseBackground, svgBackgroundColor);
            PreviewImage.Source = _previewBitmap;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _previewBitmap?.Dispose();
            _previewBitmap = null;
        }
    }
}

