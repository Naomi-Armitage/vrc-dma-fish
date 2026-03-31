# 🎣 VrcDmaFish (Safe Prototype)

Ciallo~ 这是一个基于 .NET 8.0 开发的 VRChat 自动钓鱼状态机原型喵！(∠・ω< )⌒★

本项目旨在演示如何通过有限状态机（FSM）逻辑实现钓鱼流程自动化。它采用了**解耦设计**，将信号读取（内存/视觉）与输入模拟（鼠标/硬件控制）完全分离，非常方便主人进行后续开发和自定义。

## ✨ 功能特性
- **六大状态流转**：包含 `Idle`, `Casting`, `Waiting`, `Hooked`, `Reeling`, `Cooldown`。
- **动态张力控制**：内置张力（Tension）监测逻辑，当鱼线太紧时会自动停手防止断线。
- **高度可配置**：所有时间参数、张力阈值、点击频率均可通过 `appsettings.json` 灵活调整。
- **易于扩展**：
  - 实现 `IFishSignalSource` 即可接入真实的 DMA 内存读取。
  - 实现 `IInputController` 即可接入 Kmbox 或其他物理控制器。
- **内置模拟器**：默认提供 `MockFishSignalSource`，无需打开游戏即可进行逻辑测试。

## 🚀 快速开始

### 1. 环境准备
确保你的机器上安装了 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

### 2. 获取代码并运行
```bash
git clone <your-repo-url>
cd vrc-dma-fish
dotnet run
```

## 🛠️ 项目结构
- `src/`：所有的 C# 源代码。
- `VrcDmaFish.csproj`：项目工程文件。
- `appsettings.json`：钓鱼逻辑配置文件。
- `README.md`：你正在看的这份超可爱文档喵！

## 📄 配置文件说明 (`appsettings.json`)
```json
{
  "TickIntervalMs": 100,           // 逻辑轮询间隔（毫秒）
  "Bot": {
    "CastDurationMs": 1200,        // 抛竿长按时间
    "CooldownMs": 1500,            // 钓起后的冷却时间
    "ReelTensionPauseThreshold": 0.8, // 停止收杆的张力上限
    "ReelTensionResumeThreshold": 0.55, // 恢复收杆的张力下限
    "ReelPulseMs": 80,             // 模拟点击时的按下时长
    "ReelRestMs": 120              // 模拟点击时的抬起间隔
  }
}
```

## ⚠️ 免责声明
本项目仅供学习研究状态机设计之用。默认版本不包含任何侵入性代码。请在合法合规的前提下参考使用喵！

---
Made with ❤️ by Naomi-Armitage and Taffy喵!
