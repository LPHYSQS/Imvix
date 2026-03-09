using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Imvix.Models;
using Imvix.Services;
using Imvix.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Imvix.Views
{
    public partial class MainWindow : Window
    {
        private static readonly IReadOnlyList<FilePickerFileType> ImageFileTypes =
        [
            new FilePickerFileType("Image Files")
            {
                Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.tif", "*.tiff", "*.ico", "*.svg"]
            }
        ];

        private readonly SettingsService _settingsService = new();
        private MainWindowViewModel? _subscribedViewModel;
        private bool _windowPlacementRestored;

        public MainWindow()
        {
            InitializeComponent();
            Opened += OnWindowOpened;
            Closing += OnWindowClosing;
            DataContextChanged += OnDataContextChanged;
            OnDataContextChanged(this, EventArgs.Empty);
        }

        private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.ConversionCompleted -= OnConversionCompleted;
                _subscribedViewModel.ConfirmDialogAsync = null;
            }

            _subscribedViewModel = ViewModel;
            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.ConversionCompleted += OnConversionCompleted;
                _subscribedViewModel.ConfirmDialogAsync = ShowConfirmationDialogAsync;
            }
        }

        private async void OnConversionCompleted(object? sender, ConversionSummary summary)
        {
            if (!IsVisible)
            {
                return;
            }

            var vm = ViewModel;
            if (vm is null)
            {
                return;
            }

            var dialog = new ConversionSummaryWindow(
                vm.ConversionSummaryTitleText,
                vm.BuildConversionSummaryText(summary),
                vm.CloseText)
            {
                FlowDirection = this.FlowDirection
            };

            await dialog.ShowDialog(this);
        }

        private async Task<bool> ShowConfirmationDialogAsync(string title, string message, string confirmText, string cancelText)
        {
            if (!IsVisible)
            {
                return false;
            }

            var dialog = new ConfirmationDialogWindow(title, message, confirmText, cancelText)
            {
                FlowDirection = this.FlowDirection
            };

            return await dialog.ShowDialog<bool>(this);
        }

        private async void OnShowAboutClick(object? sender, RoutedEventArgs e)
        {
            var vm = ViewModel;
            if (vm is null || !IsVisible)
            {
                return;
            }

            var dialog = new AboutWindow(vm)
            {
                FlowDirection = this.FlowDirection
            };

            await dialog.ShowDialog(this);
        }

        private void OnWindowOpened(object? sender, EventArgs e)
        {
            if (_windowPlacementRestored)
            {
                return;
            }

            _windowPlacementRestored = true;
            RestoreWindowPlacement();
        }

        private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.ConversionCompleted -= OnConversionCompleted;
                _subscribedViewModel.ConfirmDialogAsync = null;
                _subscribedViewModel = null;
            }

            SaveWindowPlacement();
        }

        private void RestoreWindowPlacement()
        {
            var settings = _settingsService.Load();

            if (settings.HasWindowPlacement)
            {
                if (settings.WindowWidth > 0)
                {
                    Width = Math.Max(MinWidth, settings.WindowWidth);
                }

                if (settings.WindowHeight > 0)
                {
                    Height = Math.Max(MinHeight, settings.WindowHeight);
                }

                var savedPosition = new PixelPoint(settings.WindowPositionX, settings.WindowPositionY);
                if (TryPlaceOnExistingScreen(savedPosition))
                {
                    return;
                }
            }

            CenterOnPrimaryScreen();
        }

        private bool TryPlaceOnExistingScreen(PixelPoint desiredPosition)
        {
            var screens = Screens;
            if (screens is null)
            {
                return false;
            }

            var screen = screens.ScreenFromPoint(desiredPosition);
            if (screen is null)
            {
                var fallbackScaling = screens.Primary?.Scaling ?? 1d;
                var estimatedSize = GetWindowPixelSize(fallbackScaling);
                var rect = new PixelRect(desiredPosition, estimatedSize);
                screen = screens.ScreenFromBounds(rect);
            }

            if (screen is null)
            {
                return false;
            }

            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = ClampToWorkingArea(desiredPosition, screen);
            return true;
        }

        private void CenterOnPrimaryScreen()
        {
            var screens = Screens;
            var primary = screens?.Primary
                ?? screens?.All.FirstOrDefault(s => s.IsPrimary)
                ?? screens?.All.FirstOrDefault();

            if (primary is null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            var area = primary.WorkingArea;
            var size = GetWindowPixelSize(primary.Scaling);

            var x = area.X + Math.Max(0, (area.Width - size.Width) / 2);
            var y = area.Y + Math.Max(0, (area.Height - size.Height) / 2);

            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }

        private PixelPoint ClampToWorkingArea(PixelPoint desiredPosition, Screen screen)
        {
            var area = screen.WorkingArea;
            var size = GetWindowPixelSize(screen.Scaling);

            var maxX = area.Right - size.Width;
            var maxY = area.Bottom - size.Height;

            var x = maxX < area.X
                ? area.X
                : Math.Clamp(desiredPosition.X, area.X, maxX);

            var y = maxY < area.Y
                ? area.Y
                : Math.Clamp(desiredPosition.Y, area.Y, maxY);

            return new PixelPoint(x, y);
        }

        private PixelSize GetWindowPixelSize(double scaling)
        {
            var safeScaling = scaling > 0 ? scaling : 1d;

            var widthDip = Bounds.Width > 0
                ? Bounds.Width
                : (double.IsNaN(Width) || Width <= 0 ? MinWidth : Width);

            var heightDip = Bounds.Height > 0
                ? Bounds.Height
                : (double.IsNaN(Height) || Height <= 0 ? MinHeight : Height);

            var widthPx = Math.Max(1, (int)Math.Round(widthDip * safeScaling));
            var heightPx = Math.Max(1, (int)Math.Round(heightDip * safeScaling));

            return new PixelSize(widthPx, heightPx);
        }

        private void SaveWindowPlacement()
        {
            if (WindowState == WindowState.Minimized)
            {
                return;
            }

            var settings = _settingsService.Load();
            settings.HasWindowPlacement = true;
            settings.WindowPositionX = Position.X;
            settings.WindowPositionY = Position.Y;

            if (WindowState == WindowState.Normal)
            {
                if (Bounds.Width > 0)
                {
                    settings.WindowWidth = Bounds.Width;
                }

                if (Bounds.Height > 0)
                {
                    settings.WindowHeight = Bounds.Height;
                }
            }

            _settingsService.Save(settings);
        }

        private async void OnImportClick(object? sender, RoutedEventArgs e)
        {
            var vm = ViewModel;
            if (vm is null)
            {
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = true,
                Title = vm.ImportDialogTitle,
                FileTypeFilter = ImageFileTypes
            });

            var paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>();

            vm.AddFiles(paths);
        }

        private async void OnImportFolderClick(object? sender, RoutedEventArgs e)
        {
            var vm = ViewModel;
            if (vm is null)
            {
                return;
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = true,
                Title = vm.ImportFolderDialogTitle
            });

            var paths = folders
                .Select(folder => folder.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>();

            vm.AddFiles(paths);
        }

        private async void OnSelectOutputFolderClick(object? sender, RoutedEventArgs e)
        {
            var vm = ViewModel;
            if (vm is null)
            {
                return;
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = vm.SelectFolderDialogTitle
            });

            var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            vm.SetOutputDirectory(folderPath);
        }

        private async void OnSelectWatchInputFolderClick(object? sender, RoutedEventArgs e)
        {
            var vm = ViewModel;
            if (vm is null)
            {
                return;
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = vm.SelectWatchInputFolderDialogTitle
            });

            var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            vm.SetWatchInputDirectory(folderPath);
        }

        private async void OnSelectWatchOutputFolderClick(object? sender, RoutedEventArgs e)
        {
            var vm = ViewModel;
            if (vm is null)
            {
                return;
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = vm.SelectWatchOutputFolderDialogTitle
            });

            var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            vm.SetWatchOutputDirectory(folderPath);
        }

        private void OnDropZoneDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
                ? DragDropEffects.Copy
                : DragDropEffects.None;

            e.Handled = true;
        }

        private void OnDropZoneDrop(object? sender, DragEventArgs e)
        {
            var vm = ViewModel;
            if (vm is null || !e.DataTransfer.Contains(DataFormat.File))
            {
                return;
            }

            var files = e.DataTransfer.TryGetFiles();
            if (files is null)
            {
                return;
            }

            var paths = files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToList();

            if (paths.Count > 0)
            {
                vm.AddFiles(paths);
            }
        }

        private async void OnImageItemDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is not Control { DataContext: ImageItemViewModel image })
            {
                return;
            }

            var vm = ViewModel;
            var previewWindow = new ImagePreviewWindow(
                image.FilePath,
                vm?.SvgUseBackground ?? false,
                vm?.SvgBackgroundColor ?? "#FFFFFFFF")
            {
                FlowDirection = this.FlowDirection
            };

            await previewWindow.ShowDialog(this);
            e.Handled = true;
        }
    }
}


