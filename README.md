# VrcDmaFish

基于 DMA 的 VRChat 自动钓鱼工具。当前修复分支在保留自动配置能力的前提下，补齐了本地构建、DMA 回退、中文交互和更稳的收线点击策略。

## 当前分支包含的改动

- 修复 `feature/v1.1-auto-config` 中的编译问题，保证本地 `dotnet build` 可以通过。
- 将项目编译范围限制到顶层 `src`，避免嵌套目录把同一套源码重复编译。
- 收线点击策略参考 [`day123123123/vrc-auto-fish`](https://github.com/day123123123/vrc-auto-fish)：
  - 咬钩后先执行一次短点击确认。
  - 收线时根据当前张力和张力变化速度动态调整按住时长。
  - 张力过高立即松开，回落到恢复阈值后再继续。
- 汉化程序入口、配置向导、实时面板和安装脚本输出。

## 快速开始

### 1. 安装依赖文件

在 PowerShell 中运行：

```powershell
.\setup.ps1
```

脚本会尝试下载 `MemProcFS` 相关文件并复制到程序运行目录。

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

## 注意事项

- DMA 模式仅支持 Windows。
- 如果 DMA 初始化失败，程序会自动回退到 Mock 信号源，方便先检查流程和输入链路。
- 如果本地目录里还有嵌套仓库副本，请移走，避免重复编译源码。
