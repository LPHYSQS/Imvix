<p align="center">
  <img src="Assets/logo.png" alt="Imvix Pro logo" width="128" />
</p>

<h1 align="center">Imvix Pro</h1>

<p align="center">
  A modern desktop image converter built for batch processing, smart format guidance, and controlled output quality.
</p>

<p align="center">
  <a href="README.md">Chinese</a> |
  <a href="#highlights">Highlights</a> |
  <a href="#build-and-run">Build</a> |
  <a href="#license">License</a>
</p>

<p align="center">
  <a href="https://get.microsoft.com/installer/download/9n3ztwz2f3z9?referrer=appbadge" target="_self" >
	<img src="https://get.microsoft.com/images/zh-cn%20dark.svg" width="200"/>
  </a>
</p>

<p align="center">
  .NET 10 | Avalonia 11 | MVVM | Batch Workflows | Folder Watch
</p>

> Imvix Pro focuses on repetitive image conversion work: import files or folders, preview results, tune compression and resizing, save presets, and optionally watch a folder for new files.

## Overview

Imvix Pro is a desktop image converter for people who need more control than a one-click converter but still want a clean, fast interface. The app combines format conversion, batch compression, resizing, rename rules, smart recommendations, history tracking, and failure logging in a single UI.

Current repository version: `1.3.3`

## Highlights

| Area | What Imvix Pro provides |
| --- | --- |
| Batch intake | Multi-file import, folder import, drag-and-drop, and optional recursive folder expansion |
| Output control | PNG, JPEG, WEBP, BMP, GIF, TIFF, ICO, and SVG output with source-folder or custom-folder routing |
| Quality tuning | Compression presets, custom quality, resize modes, SVG background fill, and overwrite control |
| Workflow tools | Presets, pause/resume/cancel, recent history, failure logs, and automatic folder watch mode |
| Smart assistance | Format recommendations, size estimation, transparency warnings, and high-compression warnings |
| UX | Preview pane, double-click full preview, light/dark themes, window placement restore, and multilingual UI |

## Supported Formats

| Type | Formats |
| --- | --- |
| Input | PNG, JPG, JPEG, WEBP, BMP, GIF, TIFF, TIF, ICO, SVG |
| Output | PNG, JPEG, WEBP, BMP, GIF, TIFF, ICO, SVG |

`GIF` and `TIFF` export are Windows-only in the current build because they rely on `System.Drawing.Common` for encoding.

## Conversion Flow

```mermaid
flowchart LR
    A["Import files or folders"] --> B["Analyze content and size"]
    B --> C["Show warnings and format recommendations"]
    C --> D["Apply format, compression, resize, rename, and output rules"]
    D --> E["Write converted files"]
    E --> F["Store history and failure logs"]
```

## Feature Detail

- Smart format recommendation based on detected content type such as photo, transparent graphic, icon, or vector art.
- Estimated output size range before running a batch.
- Preflight warnings for transparency loss when exporting to JPEG and for high-compression settings.
- Folder watch mode with debounced file readiness checks to process newly added images automatically.
- Recent conversion history limited to the latest 12 jobs.
- Failure log generation only when a batch contains errors.
- JSON-based settings and localization dictionaries for easier maintenance.

## Repository Layout

```text
Imvix Pro/
|-- Assets/                  # logo, icons, localization dictionaries
|-- Dependencies/Svg/        # bundled SVG-related assemblies
|-- Models/                  # options, presets, history, summaries, enums
|-- Services/                # conversion, analysis, watch, settings, logs, localization
|-- ViewModels/              # primary MVVM logic and advanced workflows
|-- Views/                   # main window and dialog windows
|-- App.axaml                # theme resources, icons, global styles
|-- App.axaml.cs             # app startup and main window wiring
`-- Imvix Pro.csproj             # .NET 10 desktop project definition
```

### Key Implementation Pieces

| Path | Responsibility |
| --- | --- |
| `ViewModels/MainWindowViewModel.cs` | Primary UI state, presets, settings synchronization, preview state, localization, and manual conversion entry points |
| `ViewModels/MainWindowViewModel.V3.cs` | Advanced workflow layer: watch mode, warnings, history, failure logs, pause/resume/cancel, and conversion insights |
| `Services/ImageConversionService.cs` | Core conversion pipeline, preview generation, resize logic, format encoding, naming, and output routing |
| `Services/ImageAnalysisService.cs` | Content classification, transparency detection, recommendation logic, and size estimation |
| `Services/FolderWatchService.cs` | Debounced file-system monitoring for watch mode |
| `Services/SettingsService.cs` | Persistent user settings |
| `Services/ConversionHistoryService.cs` | Recent-job history persistence |
| `Services/ConversionLogService.cs` | Failure log writing |
| `Assets/Localization/*.json` | UI translations |

## Build and Run

### Requirements

- Windows is the primary tested target in this repository.
- `.NET 10 SDK`
- A desktop environment supported by Avalonia

### Run Locally

```bash
dotnet restore
dotnet build "Imvix Pro.csproj"
dotnet run --project "Imvix Pro.csproj"
```

### Publish a Windows Single-File Build

```bash
dotnet publish "Imvix Pro.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

The repository already includes a Windows publish profile at `Properties/PublishProfiles/FolderProfile.pubxml`.

## Configuration and Data

On Windows, Imvix Pro stores application data under `%AppData%\Imvix Pro`:

| File or folder | Purpose |
| --- | --- |
| `settings.json` | UI preferences, default conversion options, presets, watch configuration, and window placement |
| `history.json` | Recent conversion history |
| `Logs/conversion-*.log` | Failure logs for batches that contain errors |

## Localization

The app includes built-in localization assets for:

- `zh-CN`
- `zh-TW`
- `en-US`
- `ja-JP`
- `ko-KR`
- `fr-FR`
- `de-DE`
- `it-IT`
- `ru-RU`
- `ar-SA`

Arabic uses right-to-left layout support in the UI.

## Tech Stack

- `.NET 10`
- `Avalonia UI 11`
- `CommunityToolkit.Mvvm`
- `SkiaSharp`
- `Svg.Skia`
- `System.Drawing.Common` for the current Windows-specific GIF/TIFF encoding path

## License

This repository ships with a custom non-commercial license in [`LICENSE`](LICENSE).

You may use, study, modify, and redistribute the software for personal, educational, research, evaluation, and other non-commercial purposes. Any commercial use is prohibited unless you first obtain written permission from the author or other copyright holder.

Important: because commercial use is restricted, this license is source-available, not an OSI-approved open source license.

## Commercial Use

If you want to use Imvix Pro in a commercial product, paid service, revenue-generating workflow, or internal business operation, you must obtain prior written permission from the author or other copyright holder first.
