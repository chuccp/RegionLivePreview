# RegionLivePreview

框选屏幕上的任意矩形区域，把该区域画面实时渲染到一个置顶、无边框的悬浮窗口中，用来长期盯行情、日志、图表、网页看板或录屏时的局部监控。

程序纯本地运行：**不联网、不上传画面、无遥测、无广告**。

> 当前代码实现了需求文档中 V1.x 的 Windows 核心能力。macOS 的 ScreenCaptureKit 后端尚未接入，详见[与需求文档的差异](#与需求文档的差异)。

## 功能

- **全局快捷键**：默认 `Alt + Shift + S`，可在设置中重新录制；注册失败（被占用）时会给出明确提示。
- **多显示器选区**：每个显示器一个半透明遮罩，支持跨显示器拖选；按物理像素记录选区，适配 125% / 150% / 200% 等 DPI 缩放。
- **实时悬浮预览**：
  - 无边框、置顶、可拖动；
  - 滚轮等比缩放窗口；
  - 右上角关闭按钮；
  - 单窗口不透明度 30% – 100%；
  - 鼠标穿透开关（穿透后点击直接落到下层窗口）；
  - 支持同时打开多个预览窗口。
- **帧率与画质**：10 / 15 / 30 / 60 fps 可选；捕获分辨率支持 100% / 75% / 50%，降低分辨率可减少 CPU 占用。
- **状态记忆**：自动保存每个预览窗口的捕获区域、位置、大小、不透明度、鼠标穿透状态；可选择在启动时恢复上次的预览窗口。
- **可被会议软件共享**：预览窗以独立应用窗口出现在任务栏，Chrome/Teams 等窗口共享选择器可直接选中它。
- **托盘常驻**：无主窗口，托盘菜单提供新建选区、显示全部、关闭全部、切换鼠标穿透、设置、关于、退出。
- **设置面板**：快捷键、帧率、画质、默认不透明度、默认穿透、开机自启、日志、恢复预览、语言（简体中文）。
- **开机自启**：写入当前用户注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不申请管理员权限。
- **单文件发布**：`self-contained` 单文件发布后无需用户安装 .NET 运行时。

## 平台与捕获后端

| 项目 | 当前状态 |
| --- | --- |
| Windows 10 21H2+ / Windows 11 | 已实现并实测 |
| macOS 14+ | 尚未实现（V2.0 路线） |
| 捕获后端 | **GDI `BitBlt` / `StretchBlt`**（屏幕区域捕获） |
| 渲染 | Avalonia 11.3.18 + Skia |
| 运行时 | .NET 10，单文件自包含发布 |

> **关于 Windows.Graphics.Capture**：需求文档指定 WGC，但 WGC 的显示器捕获需要通过 COM 互操作创建捕获项，并配套 D3D11 帧池；本版本尚未接入这套互操作，使用 GDI 屏幕捕获作为可用后端。GDI 能覆盖绝大多数普通桌面窗口，但受保护内容、部分独占全屏 / 硬件加速窗口仍可能黑屏。捕获后端已隔离在 `Services/ScreenCaptureService.cs`，后续可以替换为 WGC 实现。

## 构建

环境要求：

- Windows 10 21H2+ / Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/)

```powershell
dotnet restore
dotnet build -c Release
dotnet run --project RegionLivePreview
```

项目依赖均为常规 NuGet 包：`Avalonia`、`Avalonia.Desktop`、`Avalonia.Themes.Fluent`、`System.Text.Json`（运行时内置）。

### 发布单文件

```powershell
# x64
dotnet publish RegionLivePreview -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# ARM64（需要能还原 win-arm64 运行时包）
dotnet publish RegionLivePreview -c Release -r win-arm64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

输出目录：`RegionLivePreview/bin/Release/net10.0/<rid>/publish/`，其中 `RegionLivePreview.exe` 为单文件可执行程序。

## 使用

1. 启动程序。程序没有主窗口，只在系统托盘中常驻（新图标可能在托盘溢出区）。
2. 按 `Alt + Shift + S`（或点击托盘图标 / 托盘菜单“新建选区”）。
3. 拖动鼠标框选区域，松开后自动生成实时预览窗口，Esc 取消。
4. 在预览窗口中：
   - 按住左键拖动窗口位置；
   - 滚轮缩放窗口大小；
   - `⋯` 打开选项：鼠标穿透、不透明度；
   - `✕` 关闭当前预览。
5. 鼠标穿透开启后，预览窗口不再响应鼠标操作；可在托盘菜单“切换所有窗口鼠标穿透”中统一恢复。
6. 托盘菜单可打开设置、查看关于、关闭全部窗口或退出程序。

### 在 Google Meet / Teams 中共享预览窗

预览窗默认以独立应用窗口的形式出现在任务栏和 Alt+Tab 中（窗口标题形如 `RegionLivePreview 实时预览 500×350`），因此 Chrome/Teams 的“共享窗口”列表可以直接选中它。

在 Meet/Teams 里打开“共享窗口”选择器，选择对应的 `RegionLivePreview 实时预览 …` 即可。若不想共享预览窗，也可以直接共享“整个屏幕”或原始内容所在的窗口。

> 如果选择器里看不到预览窗，重新打开一次共享选择器刷新列表即可。

### 设置与数据位置

- 配置：`%AppData%\RegionLivePreview\config.json`
- 日志：`%AppData%\RegionLivePreview\logs\app-YYYYMMDD.log`（默认关闭，可在设置中启用）
- 配置使用 JSON 序列化，字段为平台无关的物理像素坐标；后续 macOS 版本可复用同一配置文件。
- 日志仅记录运行错误、快捷键注册情况以及窗口/选区的尺寸与位置，不记录任何画面内容。

## 项目结构

V1 采用单 Avalonia 项目 + 分层目录，便于 Avalonia 单文件发布与裁剪；逻辑上对应需求文档的 Core / UI / Platform 三层：

```
RegionLivePreview/
├─ App.axaml / App.axaml.cs        # 应用生命周期、托盘、热键、设置应用、退出
├─ Program.cs
├─ Core/                           # 共享模型与基础设施
│  ├─ AppConfig.cs                 # 配置模型、窗口状态、归一化
│  ├─ ConfigService.cs             # JSON 配置读写（原子替换）
│  ├─ HotkeyDefinition.cs          # 热键模型、VK 映射、显示格式
│  └─ Logger.cs                    # 极简本地日志（默认关闭）
├─ Services/
│  ├─ ScreenCaptureService.cs      # Windows GDI 捕获线程、帧缓冲、帧率控制
│  ├─ PreviewManager.cs            # 预览窗口生命周期、默认几何、状态持久化
│  └─ PreviewOpenOptions.cs
├─ Platform/
│  ├─ MonitorService.cs            # 显示器枚举、DPI、工作区
│  └─ Windows/
│     ├─ NativeMethods.cs          # user32/gdi32/advapi32 P/Invoke
│     ├─ GlobalHotkeyService.cs    # RegisterHotKey + 消息专用窗口
│     ├─ WindowInterop.cs          # WS_EX_TRANSPARENT 鼠标穿透、原生窗口查询
│     └─ AutoStartService.cs       # HKCU Run 开机自启
└─ Views/
   ├─ SelectionOverlayWindow.*     # 每显示器一个的多屏选区遮罩
   ├─ SelectionCoordinator.cs      # 跨屏选区会话、物理像素坐标换算
   ├─ PreviewWindow.*              # 悬浮预览窗口、滚轮缩放、选项 Flyout
   ├─ SettingsWindow.*             # 设置面板与热键录制
   ├─ AboutWindow.*
   ├─ MessageDialog.*
   └─ UiAssets.cs
```

## 与需求文档的差异

本版本是 Windows 可用的 MVP，以下差异是刻意保留的范围说明：

1. **捕获后端**：当前使用 GDI 屏幕区域捕获，尚未使用 Windows.Graphics.Capture；因此暂不覆盖 WGC 对独占全屏/硬件加速窗口的兼容性叙事，受保护内容可能黑屏。
2. **macOS**：未实现 ScreenCaptureKit、macOS 全局热键、菜单栏状态项与 launchd 自启；程序在非 Windows 平台启动时会提示不支持屏幕捕获。
3. **Native AOT**：当前交付为 self-contained 单文件（无运行时依赖），但未启用/验证 Native AOT；启用 AOT 还需为配置 JSON 增加 source generator，并验证 Avalonia 裁剪。
4. **跨虚拟桌面/跨 Space 置顶**：Windows 的 Topmost 只在当前虚拟桌面内生效，无法强制在切换桌面后仍显示；macOS Space 同理未实现。
5. **GraphicsCapture 组策略检测**：未做专门的组策略探测，捕获失败时通过预览窗口内的错误提示与日志反馈。
6. **跨屏选区**：通过“每个显示器一个遮罩 + 虚拟桌面物理坐标”实现；已在 150% 单屏实测，多显示器混合 DPI 环境尚未实机验证。
7. **镜像递归**：预览窗口捕获的是屏幕合成画面；如果把预览窗口拖到它自己的捕获区域上，会出现递归的“画中画”画面，这是区域屏幕捕获的固有现象。

## 已验证

在 Windows 11（3840×2160 @150%）上完成以下冒烟测试：

- Debug / Release 构建：0 警告、0 错误；
- 全局 `Alt + Shift + S` 热键、透明遮罩、拖选与尺寸标签；
- 选区松开后生成置顶预览窗，画面实时刷新；
- 预览窗位置、大小、不透明度、穿透状态持久化，并在启动时恢复；
- 鼠标穿透样式（`WS_EX_TRANSPARENT`）已置位，穿透时隐藏窗口按钮；
- 设置窗口、托盘图标与中文工具提示正常；
- 预览窗以独立应用窗口创建：无隐藏 owner、带 `WS_EX_APPWINDOW` 和标题，出现在任务栏，符合 Chrome/Teams 窗口共享列表的识别条件；
- `net10.0 win-x64` self-contained 单文件发布可正常启动。

尚未实机验证：Chrome/Meet 共享选择器内的最终列出效果（测试时用户正在通话，未弹出共享选择器打扰）、多显示器/混合 DPI、多预览并发压力、开机自启、热键冲突恢复、权限撤销（Windows 无该权限模型）、macOS。

## 调试参数（开发用）

```text
--settings              启动后打开设置窗口
--about                 启动后打开关于窗口
--preview x,y,w,h       按物理像素直接创建一块预览（用于无鼠标环境冒烟测试）
```

## 隐私与安全

- 不包含任何网络客户端、HTTP 请求或上传逻辑。
- 不读写除配置文件、日志目录之外的用户数据。
- 日志默认关闭；开启后也不记录屏幕内容。
- 开机自启只写入当前用户注册表，不需要管理员权限。

## 后续计划

- 接入 Windows.Graphics.Capture（D3D11 帧池 + 显示器捕获 COM 互操作），替换 GDI 后端；
- 接入 ScreenCaptureKit、macOS 菜单栏与 launchd 自启，统一配置格式；
- 为 Native AOT 增加 JSON source generator 与裁剪验证；
- 多显示器混合 DPI、长时间运行与多预览压力测试。

## License

MIT © 2026 RegionLivePreview contributors
