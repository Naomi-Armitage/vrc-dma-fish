# VrcDmaFish

基于 DMA 的 VRChat 自动钓鱼工具。当前修复分支在保留自动配置能力的前提下，补齐了本地构建、DMA 回退、中文交互和更稳的收线点击策略。

## 当前分支包含的改动

- 修复 `feature/v1.1-auto-config` 中的编译问题，保证本地 `dotnet build` 可以通过。
- 将项目编译范围限制到顶层 `src`，避免嵌套目录把同一套源码重复编译。
- 收线点击策略参考 [`day123123123/vrc-auto-fish`](https://github.com/day123123123/vrc-auto-fish)：
  - 咬钩后先执行一次短点击确认。
  - 收线时根据当前张力和张力变化速度动态调整按住时长。
  - 张力过高立即松开，回落到恢复阈值后再继续。
- 新增 DMA 位置控制通路：
  - 当 `FishPositionOffset` 和白条位置偏移可用时，优先按“让白条包住鱼图标”的方式控制。
  - 支持两种内存布局：`鱼位置 + 白条中心/高度`，或 `鱼位置 + 白条上边界/下边界`。
  - 若位置偏移未配置，自动回退到原先的张力控制，不强依赖屏幕识别。
- 汉化程序入口、配置向导、实时面板和安装脚本输出。

## 快速开始

### 1. 安装依赖文件

在 PowerShell 中运行：

```powershell
.\setup.ps1
```

脚本会尝试下载 `MemProcFS` 相关文件并复制到程序运行目录。
如果压缩包里没有携带 FTDI 驱动库，脚本会继续部署 `vmm.dll` / `leechcore.dll` / `info.db`，并提示你手动补 `FTD3XX.dll` 或 `FTD3XXWU.dll`。

### 2. 编译

```powershell
dotnet build
```

### 3. 运行

```powershell
dotnet run
```

如果当前终端支持交互，程序会自动进入中文配置向导。

## 配置说明

默认配置文件是 [appsettings.json](/C:/Users/Administrator/Documents/vrc-dma-fish/appsettings.json)。和点击策略相关的参数包括：

- `HookClickMs`：咬钩确认点击时长。
- `ReelPulseMs`：基础收线按住时长。
- `ReelHoldMinMs` / `ReelHoldMaxMs`：动态按住时长上下限。
- `ReelHoldGainMs`：张力偏差带来的按住时长修正。
- `ReelVelocityDampingMs`：张力上升过快时的阻尼修正。
- `ReelRestMs`：本轮不按时的休息时长。
- `PositionBaseHoldMs` / `PositionHoldGainMs`：位置模式下的基础按住时长和误差增益。
- `PositionVelocityDampingMs` / `PositionVelocitySmooth`：白条速度阻尼与速度平滑。
- `PositionDeadZoneRatio`：鱼图标落在白条中部多少范围内时，仅保持基础力度。
- `PositionPressMovesUp`：按住鼠标是否会让白条向上移动；若方向相反可改为 `false`。

和 DMA 位置控制相关的偏移包括：

- `FishPositionOffset`
- `BarCenterOffset` 与 `BarHeightOffset`
- 或 `BarTopOffset` 与 `BarBottomOffset`

## 注意事项

- DMA 模式仅支持 Windows。
- 如果 DMA 初始化失败，程序会自动回退到 Mock 信号源，方便先检查流程和输入链路。
- 位置控制是否真正启用，取决于 DMA 是否能稳定读到鱼位置和白条位置；面板里会显示“位置控制 已就绪/未就绪”。
- `setup.ps1` 不再强制要求 `FTD601.dll`；对于 PCILeech FPGA，请按上游说明把 `FTD3XX.dll` 或 `FTD3XXWU.dll` 放到 `leechcore.dll` 同目录。
- 如果本地目录里还有嵌套仓库副本，请移走，避免重复编译源码。
