<p align="center">
  <img src="Assets/logo.png" alt="Imvix Pro logo" width="128" />
</p>

<h1 align="center">Imvix Pro</h1>

<p align="center">
  面向批量处理、智能格式判断与输出可控性的现代桌面图像转换工具
</p>

<p align="center">
  <a href="README.en.md">English</a> ·
  <a href="#核心亮点">核心亮点</a> ·
  <a href="#构建与运行">构建</a> ·
  <a href="#许可说明">许可</a>
</p>

<p align="center">
  <a href="https://get.microsoft.com/installer/download/9n3ztwz2f3z9?referrer=appbadge" target="_self" >
	<img src="https://get.microsoft.com/images/zh-cn%20dark.svg" width="200"/>
  </a>
</p>

<p align="center">
  .NET 10 · Avalonia 11 · MVVM · 批量工作流 · 文件夹监听
</p>

> Imvix Pro 不是只做“格式改后缀”的轻量小工具，而是围绕高频图片处理场景设计的桌面工作台，强调批量效率、参数可控、结果可预览、错误可追踪。

## 项目概览

Imvix Pro 是一个桌面端图片转换器，适合需要批量导入、压缩、缩放、重命名、预览、记录历史和自动监听目录的工作流。它把常用操作放在主界面中，同时保留压缩质量、尺寸策略、输出目录、覆盖规则、SVG 背景色等细粒度控制。

当前仓库版本：`1.3.3`

## 核心亮点

| 能力方向 | Imvix Pro 提供的内容 |
| --- | --- |
| 批量导入 | 支持多文件导入、文件夹导入、拖拽导入，以及按需递归导入子目录 |
| 输出控制 | 支持 PNG、JPEG、WEBP、BMP、GIF、TIFF、ICO、SVG 输出，并可保存到源目录或指定目录 |
| 质量调优 | 支持压缩模式、自定义质量、尺寸调整、SVG 背景填充与覆盖同名文件控制 |
| 工作流增强 | 支持预设保存、暂停、继续、取消、最近历史、失败日志与自动监听目录 |
| 智能辅助 | 提供格式推荐、体积预估、透明背景风险提示和高压缩风险提示 |
| 界面体验 | 提供预览面板、双击大图预览、浅色/深色主题、多语言界面与窗口位置记忆 |

## 支持的格式

| 类型 | 格式 |
| --- | --- |
| 输入 | PNG、JPG、JPEG、WEBP、BMP、GIF、TIFF、TIF、ICO、SVG |
| 输出 | PNG、JPEG、WEBP、BMP、GIF、TIFF、ICO、SVG |

当前构建中的 `GIF` 和 `TIFF` 导出依赖 `System.Drawing.Common`，因此以 Windows 为主要支持目标。

## 转换流程

```mermaid
flowchart LR
    A["导入文件或文件夹"] --> B["分析内容与体积"]
    B --> C["给出提示与格式推荐"]
    C --> D["应用格式、压缩、缩放、重命名与输出规则"]
    D --> E["写出转换结果"]
    E --> F["记录历史与失败日志"]
```

## 功能细节

- 基于图片内容类型进行智能推荐，例如照片、透明图形、图标或矢量素材。
- 在执行批量任务前给出输出体积预估区间。
- 当导出为 JPEG 可能丢失透明背景时，会提前给出风险提示。
- 当压缩强度过高时，会提示可能带来的画质损失。
- 文件夹监听模式具备去抖和就绪检测逻辑，可自动处理新加入的图片。
- 最近转换历史默认保留最近 12 条记录。
- 仅当任务出现失败项时才会写出失败日志。
- 设置项和本地化资源采用 JSON 组织，便于维护与扩展。

## 仓库结构

```text
Imvix Pro/
|-- Assets/                  # logo、图标、本地化字典
|-- Dependencies/Svg/        # 随项目分发的 SVG 相关程序集
|-- Models/                  # 转换选项、预设、历史、摘要、枚举模型
|-- Services/                # 转换、分析、监听、设置、日志、本地化服务
|-- ViewModels/              # 主 MVVM 逻辑与增强工作流逻辑
|-- Views/                   # 主窗口与各类对话框
|-- App.axaml                # 主题资源、图标资源、全局样式
|-- App.axaml.cs             # 应用启动与主窗口装配
`-- Imvix Pro.csproj             # .NET 10 桌面项目定义
```

### 关键实现模块

| 路径 | 作用 |
| --- | --- |
| `ViewModels/MainWindowViewModel.cs` | 主界面状态、预设管理、设置同步、预览状态、多语言切换、手动转换入口 |
| `ViewModels/MainWindowViewModel.V3.cs` | 文件夹监听、风险提示、历史记录、失败日志、暂停/继续/取消与转换洞察 |
| `Services/ImageConversionService.cs` | 核心转换流程、预览生成、缩放逻辑、格式编码、命名规则与输出路径处理 |
| `Services/ImageAnalysisService.cs` | 内容分类、透明检测、格式推荐与体积估算 |
| `Services/FolderWatchService.cs` | 基于文件系统事件的监听与去抖处理 |
| `Services/SettingsService.cs` | 用户设置持久化 |
| `Services/ConversionHistoryService.cs` | 最近任务历史持久化 |
| `Services/ConversionLogService.cs` | 失败日志输出 |
| `Assets/Localization/*.json` | 界面翻译资源 |

## 构建与运行

### 环境要求

- 当前仓库主要以 Windows 作为验证和发布目标
- `.NET 10 SDK`
- Avalonia 支持的桌面运行环境

### 本地运行

```bash
dotnet restore
dotnet build "Imvix Pro.csproj"
dotnet run --project "Imvix Pro.csproj"
```

### 发布 Windows 单文件版本

```bash
dotnet publish "Imvix Pro.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

仓库中已经包含一个 Windows 发布配置文件：`Properties/PublishProfiles/FolderProfile.pubxml`。

## 配置与数据存储

在 Windows 上，Imvix Pro 会将应用数据保存到 `%AppData%\Imvix Pro`：

| 文件或目录 | 说明 |
| --- | --- |
| `settings.json` | 界面偏好、默认转换参数、预设、监听配置与窗口位置 |
| `history.json` | 最近转换历史 |
| `Logs/conversion-*.log` | 批量任务失败时生成的错误日志 |

## 本地化支持

当前内置了以下界面语言资源：

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

其中阿拉伯语界面支持从右向左布局。

## 技术栈

- `.NET 10`
- `Avalonia UI 11`
- `CommunityToolkit.Mvvm`
- `SkiaSharp`
- `Svg.Skia`
- `System.Drawing.Common`（当前 GIF/TIFF 编码路径依赖 Windows）

## 许可说明

本仓库附带的许可文件为英文版 [`LICENSE`](LICENSE)，采用自定义的非商用许可。

你可以在个人使用、学习研究、评估测试、非商用二次开发和非商用分发场景中使用本项目；但任何商业用途都必须事先取得作者或其他版权持有者的书面许可。

需要特别说明的是：由于该许可明确限制商业使用，因此它在严格定义上属于 source-available，而不是 OSI 意义上的开源许可。

## 商业使用

如果你希望将 Imvix Pro 用于商业产品、付费服务、营收型流程或企业内部商业场景，请先获得作者或其他版权持有者的书面授权。
