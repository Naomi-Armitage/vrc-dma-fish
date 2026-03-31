# VrcDmaFish

一个基于 .NET 8 的 VRChat 钓鱼状态机原型。

当前仓库默认以 `Mock` 信号源运行，开箱即可做状态机和输入流程验证；DMA 读取保留为可选实验路径，只有在配置了目标地址和偏移后才会启用。

## 当前特性

- 默认可运行的模拟模式，不依赖游戏进程
- 明确的状态流转：`Idle` -> `Casting` -> `Waiting` -> `Hooked` -> `Reeling` -> `Cooldown`
- 使用暂停/恢复双阈值的张力控制，避免在阈值附近抖动
- `appsettings.json` 配置加载，支持尾逗号和 JSON 注释
- DMA 模式初始化失败时自动回退到 `Mock`，避免直接崩溃

## 运行环境

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 快速开始

```bash
dotnet run -- --ticks 80
```

`--ticks` 是可选参数，用来限制循环次数，方便做冒烟验证；不传时会持续运行，按 `Ctrl+C` 退出。

## 配置说明

默认配置文件是 `appsettings.json`。

```json
{
  "TickIntervalMs": 100,
  "Input": {
    "Type": "Console"
  },
  "SignalSource": {
    "Type": "Mock",
    "ProcessName": "VRChat",
    "TargetObjectName": "FishingLogic"
  },
  "Bot": {
    "CastDurationMs": 1200,
    "CooldownMs": 1500,
    "ReelTensionPauseThreshold": 0.8,
    "ReelTensionResumeThreshold": 0.55,
    "ReelPulseMs": 80,
    "ReelRestMs": 120
  }
}
```

### `SignalSource`

- `Type`: `Mock` 或 `Dma`
- `ProcessName`: DMA 模式下要附加的进程名
- `TargetObjectName`: 自动扫描时尝试查找的对象名
- `TargetObjectAddress`: 可选，支持十进制或 `0x` 十六进制；配置后会跳过自动扫描
- `HookedOffset` / `CatchCompletedOffset` / `TensionOffset`: DMA 读取所需偏移，支持十进制或 `0x` 十六进制

如果 `Type` 为 `Dma` 但地址或偏移不完整，程序会记录警告并回退到 `Mock`。

## 安装 MemProcFS 原生库

DMA 模式依赖 `vmm.dll`、`leechcore.dll` 等原生文件。可以运行：

```powershell
.\setup.ps1
```

脚本会下载 MemProcFS `v5.17` 的 Windows x64 压缩包，并把所需文件复制到已存在的 `bin/*/net8.0` 输出目录；如果目录还不存在，则默认复制到 `bin/Debug/net8.0`。

也可以显式指定目标目录：

```powershell
.\setup.ps1 -TargetDir bin\Release\net8.0
```

## 项目结构

- `src/Core`: 状态机
- `src/Inputs`: 输入控制器
- `src/Models`: 配置和上下文模型
- `src/Providers`: 信号源实现
- `src/UI`: 控制台输出

## 现阶段限制

- `ConsoleInputController` 只做日志输出，不会驱动真实硬件
- `UnityScanner` 还没有可靠的自动签名扫描，DMA 最稳妥的方式仍然是手动提供目标地址
- 仓库目前没有单元测试，建议每次改动后至少执行一次 `dotnet build` 和一次带 `--ticks` 的冒烟运行
