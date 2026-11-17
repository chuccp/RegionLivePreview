# RegionLivePreview

框选屏幕上的任意矩形区域，把该区域画面实时渲染到一个置顶、无边框的悬浮窗口中，用来长期盯行情、日志、图表、网页看板或录屏时的局部监控。

程序纯本地运行：**不联网、不上传画面、无遥测、无广告**。

> 当前代码支持 Windows 10 21H2+ / Windows 11 与 macOS 14+。macOS 使用 ScreenCaptureKit 捕获，支持 Intel 与 Apple Silicon。

## 功能

- **全局快捷键**：默认 `Alt + Shift + S`，可在设置中重新录制；注册失败（被占用）时会给出明确提示。
- **多显示器选区**：每个显示器一个半透明遮罩，支持跨显示器拖选；按物理像素记录选区，适配 125% / 150% / 200% 等 DPI 缩放。
- **实时悬浮预览**：
  - 无边框、可拖动；Windows 置顶，macOS 使用可被会议软件选中的普通窗口层级；
  - 拖动四边或四角自由调整窗口大小；
  - 每个窗口可开启“固定捕获比例”，缩放时始终保持原区域宽高比；
  - 滚轮等比缩放窗口；
  - 右上角关闭按钮；
  - 单窗口不透明度 30% – 100%；
  - 鼠标穿透开关（穿透后点击直接落到下层窗口）；
  - 支持同时打开多个预览窗口。
- **启动即选区**：手动启动后自动进入选区模式；Esc 取消后驻留托盘。可在设置中关闭此行为；开机自启时只驻留托盘。
- **帧率与画质**：10 / 15 / 30 / 60 fps 可选；捕获分辨率支持 100% / 75% / 50%，降低分辨率可减少 CPU 占用。
- **状态记忆**：自动保存每个预览窗口的捕获区域、位置、大小、不透明度、鼠标穿透状态；可选择在启动时恢复上次的预览窗口。
- **可被会议软件共享**：预览窗以独立应用窗口出现（Windows 任务栏 / Alt+Tab，macOS 窗口列表），Chrome/Teams 等窗口共享选择器可直接选中它。
- **托盘/菜单栏常驻**：无主窗口，菜单提供新建选区、显示全部、关闭全部、切换鼠标穿透、设置、关于、退出。
- **设置面板**：快捷键、帧率、画质、默认不透明度、默认穿透、启动时自动选区、登录自启、日志、恢复预览、语言（简体中文）。
- **登录自启**：Windows 写入当前用户注册表，macOS 使用用户级 LaunchAgent，均不申请管理员权限。
- **自包含发布**：无需用户安装 .NET 运行时；Windows 可输出单文件，macOS 输出带原生桥接库的 `.app`。

## 平台与捕获后端

| 项目 | 当前状态 |
| --- | --- |
| Windows 10 21H2+ / Windows 11 | 已实现并实测 |
| macOS 14+（Intel / Apple Silicon） | 已实现，ScreenCaptureKit |
| Windows 捕获后端 | **GDI `BitBlt` / `StretchBlt`** |
| macOS 捕获后端 | **ScreenCaptureKit** + Objective-C 原生桥接 |
| 渲染 | Avalonia 11.3.18 + Skia |
| 运行时 | .NET 10，单文件自包含发布 |

> **关于 Windows.Graphics.Capture**：需求文档指定 WGC，但 WGC 的显示器捕获需要通过 COM 互操作创建捕获项，并配套 D3D11 帧池；本版本尚未接入这套互操作，使用 GDI 屏幕捕获作为可用后端。GDI 能覆盖绝大多数普通桌面窗口，但受保护内容、部分独占全屏 / 硬件加速窗口仍可能黑屏。捕获后端已隔离在 `Services/ScreenCaptureService.cs`，后续可以替换为 WGC 实现。

## 构建

### Windows

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

### macOS

环境要求：

- macOS 14+
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Xcode 或 Xcode Command Line Tools（提供 ScreenCaptureKit SDK、Clang 与 `codesign`）

开发运行：

```bash
dotnet restore
dotnet build -c Release
dotnet run --project RegionLivePreview
```

首次启动或首次开始选区时，macOS 会请求“屏幕与系统音频录制”权限。请前往
`系统设置 → 隐私与安全性 → 屏幕与系统音频录制`，允许 `RegionLivePreview`
（开发运行时也可能是 `dotnet` 或承载它的终端）后重新启动程序。

构建可分发的 `.app`：

```bash
./scripts/build-macos.sh

# 指定架构
ARCHITECTURE=arm64 ./scripts/build-macos.sh
ARCHITECTURE=x64 ./scripts/build-macos.sh

# 使用 Developer ID 签名
CODESIGN_IDENTITY="Developer ID Application: ..." ./scripts/build-macos.sh
```

输出为 `artifacts/RegionLivePreview.app`。构建过程会编译
`Native/macOS/RegionLivePreviewMac.m`，并将 ScreenCaptureKit 桥接动态库放入应用包。
未经公证的本地构建可能需要在“隐私与安全性”中手动允许打开。

### 安装包

Windows MSI（需要 Windows 和 WiX Toolset v3）：

```powershell
choco install wixtoolset -y
.\scripts\build-msi.ps1

# Windows ARM64
.\scripts\build-msi.ps1 -Runtime win-arm64
```

macOS PKG：

```bash
# Apple Silicon
ARCHITECTURE=arm64 ./scripts/build-pkg.sh

# Intel
ARCHITECTURE=x64 ./scripts/build-pkg.sh
```

安装包输出到 `artifacts/installers/`。GitHub Actions 工作流
`.github/workflows/build-installers.yml` 会在手动触发或推送 `v*` 标签时自动构建
Windows x64/ARM64 MSI、macOS arm64/x64 PKG；标签构建还会自动创建 GitHub Release
并上传四个安装包。

可选证书与签名 Secrets：

- Windows Authenticode：
  - `WINDOWS_CERTIFICATE_PFX_BASE64`：包含私钥的代码签名 PFX，Base64 编码
  - `WINDOWS_CERTIFICATE_PASSWORD`：PFX 密码
- macOS Developer ID：
  - `APPLE_CERTIFICATE_P12_BASE64`：包含 Developer ID Application 和
    Developer ID Installer 私钥的 `.p12`，Base64 编码
  - `APPLE_CERTIFICATE_PASSWORD`：`.p12` 密码
- `APPLE_DEVELOPER_ID_APPLICATION`、`APPLE_DEVELOPER_ID_INSTALLER`
- `APPLE_ID`、`APPLE_TEAM_ID`、`APPLE_APP_PASSWORD`

配置 Windows PFX 后，工作流会为 `RegionLivePreview.exe` 和 MSI 添加 Authenticode
时间戳签名。配置 Apple 证书后，`.app` 和 `.pkg` 会分别签名；再配置 Apple ID、
Team ID 与应用专用密码后会自动公证并装订票据。未配置这些 Secrets 时仍会生成
未签名或临时签名的安装包。

## 使用

1. 手动启动程序后，默认会直接进入选区模式（程序本身没有主窗口，选择/取消后在系统托盘或 macOS 菜单栏常驻）。
2. 拖动鼠标框选区域，松开后自动生成实时预览窗口；按 `Esc` 取消本次选区。
3. 之后按默认快捷键（Windows `Alt + Shift + S`，macOS `Option + Shift + S`），或点击托盘/菜单栏图标进入选区。
4. 选区遮罩顶部会提示：快捷键、自动选区等可在**托盘图标/菜单栏图标 → 设置…**中修改。
5. 在预览窗口中：
   - 按住左键拖动窗口位置；
   - 拖动四边或四角调整窗口大小；
   - `⋯` 中的“固定捕获比例”控制拖拽缩放时是否锁定原区域宽高比；
   - 滚轮等比缩放窗口大小；
   - `⋯` 打开选项：鼠标穿透、不透明度；
   - `✕` 关闭当前预览。
6. 鼠标穿透开启后，预览窗口不再响应鼠标操作；可在托盘/菜单栏菜单“切换所有窗口鼠标穿透”中统一恢复。
7. 托盘/菜单栏菜单可打开设置、查看关于、关闭全部窗口或退出程序。

设置 → 预览窗口中可以关闭“启动时自动进入选区模式”。关闭后启动程序只驻留托盘；开机自启使用 `--autostart` 启动，也不会自动弹出选区。

### 在 Google Meet / Teams 中共享预览窗

预览窗以独立应用窗口形式出现（窗口标题形如 `RegionLivePreview 实时预览 500×350`），因此 Chrome/Teams 的“共享窗口”列表可以直接选中它。

macOS 会从 Chrome/Meet 的“窗口”列表中排除浮动层级窗口，因此 macOS 预览窗
默认使用普通窗口层级，可直接被共享选择器选中。这在 macOS 上意味着预览窗
不会始终保持置顶。

然后在 Meet/Teams 里打开“共享窗口”选择器，选择对应的
`RegionLivePreview 实时预览 …`。若不想共享预览窗，也可以直接共享“整个屏幕”
或原始内容所在的窗口。

> 如果选择器里看不到预览窗，重新打开一次共享选择器刷新列表即可。

### 本地模拟 Google Meet 共享窗口

`tools/window-share-test/` 提供不需要登录或创建会议的本地测试页。它调用 Chrome
相同的原生 `getDisplayMedia` 窗口选择器：

```bash
./scripts/open-share-test.sh
```

脚本会在 Chrome 打开 `http://127.0.0.1:8765/`。保持预览窗口打开，点击
“共享屏幕”并切换到“窗口”。窗口中应出现
`RegionLivePreview 实时预览 …`；选中后会显示共享画面和分辨率。

### 设置与数据位置

- 配置：`%AppData%\RegionLivePreview\config.json`
- 日志：`%AppData%\RegionLivePreview\logs\app-YYYYMMDD.log`（默认关闭，可在设置中启用）
- macOS 配置与日志：`~/Library/Application Support/RegionLivePreview/`
- macOS LaunchAgent：`~/Library/LaunchAgents/com.regionlivepreview.app.plist`
- 配置使用 JSON 序列化，字段为平台无关的物理像素坐标，Windows 与 macOS 共用同一格式。
- 日志仅记录运行错误、快捷键注册情况以及窗口/选区的尺寸与位置，不记录任何画面内容。

## 项目结构

项目采用单 Avalonia 工程 + 分层目录，共享 Core / UI，并按平台隔离原生依赖：

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
│  ├─ ScreenCaptureService.cs      # GDI / ScreenCaptureKit 捕获、帧缓冲、帧率控制
│  ├─ PreviewManager.cs            # 预览窗口生命周期、默认几何、状态持久化
│  └─ PreviewOpenOptions.cs
├─ Platform/
│  ├─ MonitorService.cs            # Windows/macOS 显示器枚举、DPI、工作区
│  ├─ IGlobalHotkeyService.cs      # 跨平台热键工厂
│  ├─ AutoStartService.cs          # 跨平台登录自启门面
│  ├─ WindowInterop.cs             # 跨平台鼠标穿透与原生窗口查询
│  ├─ Mac/                         # ScreenCaptureKit 权限、Carbon 热键、LaunchAgent
│  └─ Windows/
│     ├─ NativeMethods.cs          # user32/gdi32/advapi32 P/Invoke
│     ├─ GlobalHotkeyService.cs    # RegisterHotKey + 消息专用窗口
│     ├─ WindowInterop.cs          # WS_EX_TRANSPARENT 鼠标穿透
│     └─ AutoStartService.cs       # HKCU Run 开机自启
├─ Packaging/macOS/Info.plist      # macOS App Bundle 元数据
├─ Native/macOS/RegionLivePreviewMac.m # ScreenCaptureKit、屏幕/窗口、Carbon 热键
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

以下是当前版本刻意保留的范围说明：

1. **Windows 捕获后端**：当前使用 GDI 屏幕区域捕获，尚未使用 Windows.Graphics.Capture；受保护内容、部分独占全屏或硬件加速窗口可能黑屏。
2. **macOS 跨显示器区域**：ScreenCaptureKit 以单个显示器为捕获源。选区跨多个显示器时会捕获覆盖面积最大的显示器，跨屏部分不会进入同一个预览。
3. **Native AOT**：当前交付为 self-contained 单文件（无运行时依赖），但未启用/验证 Native AOT；启用 AOT 还需为配置 JSON 增加 source generator，并验证 Avalonia 裁剪。
4. **跨虚拟桌面/跨 Space 置顶**：Windows 的 Topmost 只在当前虚拟桌面内生效；macOS 预览窗口也不会被强制复制到所有 Space。
5. **GraphicsCapture 组策略检测**：未做 Windows 组策略探测；Windows/macOS 捕获失败时均通过预览窗口与日志反馈。
6. **跨屏选区**：通过“每个显示器一个遮罩 + 虚拟桌面物理坐标”实现；Windows 已在 150% 单屏实测，多显示器混合 DPI 环境尚未完整验证。macOS 已覆盖同一显示器内选区。
7. **镜像递归**：预览窗口捕获的是屏幕合成画面；如果把预览窗口拖到它自己的捕获区域上，会出现递归的“画中画”画面，这是区域屏幕捕获的固有现象。
8. **macOS 共享与置顶互斥**：Chrome/Meet 的窗口列表会排除浮动层级窗口；为默认支持会议共享，macOS 预览窗使用普通窗口层级，因此不再持续置顶。

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

macOS 14+（Apple Silicon、3840×2160 @2x）已完成以下冒烟测试：

- Debug / Release 构建、ScreenCaptureKit 原生桥接编译：0 警告、0 错误；
- Carbon 全局 `Option + Shift + S` 注册成功；
- 透明选区遮罩、鼠标拖选、物理像素尺寸标签；
- ScreenCaptureKit 实时出图、预览窗选项栏与关闭按钮；
- Chrome 原生“共享窗口”选择器默认能看到并选中
  `RegionLivePreview 实时预览 800×600`，共享分辨率识别为 `800×600`；
- 缩放下新选区预览尺寸非零，位置与捕获区域一致；
- 预览窗口位置和尺寸在重启后恢复；
- `.app` 生成、Info.plist 校验、自包含发布与 `codesign --verify --deep --strict`；
- `osx-arm64` 与 `osx-x64` 可分别生成匹配架构的托管主程序和原生桥接库。

应用包跨机器分发、多显示器、混合 DPI、多预览并发压力与登录自启仍需在不同硬件组合上继续验证。

尚未实机验证：Windows 多显示器/混合 DPI、多预览并发压力、开机自启、热键冲突恢复。

## 调试参数（开发用）

```text
--settings              启动后打开设置窗口（不会自动进入选区）
--about                 启动后打开关于窗口（不会自动进入选区）
--preview x,y,w,h       按物理像素直接创建一块预览（不会自动进入选区，用于无鼠标环境冒烟测试）
--autostart             开机自启内部参数（不会自动进入选区）
```

以上任何 `--` 参数启动时都不会自动打开选区遮罩。

## 隐私与安全

- 不包含任何网络客户端、HTTP 请求或上传逻辑。
- 不读写除配置文件、日志目录之外的用户数据。
- 日志默认关闭；开启后也不记录屏幕内容。
- 登录自启只写入当前用户注册表或 `~/Library/LaunchAgents`，不需要管理员权限。

## 后续计划

- 接入 Windows.Graphics.Capture（D3D11 帧池 + 显示器捕获 COM 互操作），替换 GDI 后端；
- 增加 macOS 多显示器跨屏捕获策略与不同 Space 的行为选项；
- 为 Native AOT 增加 JSON source generator 与裁剪验证；
- 多显示器混合 DPI、长时间运行与多预览压力测试。

## License

MIT © 2026 RegionLivePreview contributors
