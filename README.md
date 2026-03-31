# VrcDmaFish

一个**安全原型版**钓鱼状态机项目，用于演示：
- 状态机设计
- 日志输出
- 可替换的数据源接口
- 可替换的输入执行接口
- 配置驱动的轮询控制

> 说明：本项目当前版本不接入任何游戏内存、DMA、注入、驱动、规避检测或真实自动化操作逻辑。
> 它是一个可运行的模拟器/原型，用于验证程序结构与流程。

## 功能
- Idle / Casting / Waiting / Hooked / Reeling / Cooldown 状态流转
- Mock 信号源模拟“鱼上钩 / 张力变化 / 收杆完成”
- 控制台日志
- JSON 配置文件
- 可扩展接口：`IFishSignalSource`、`IInputController`

## 运行
```bash
dotnet run
```

## 目录
- `Program.cs`：入口
- `AppConfig.cs`：配置模型
- `BotConfig.cs`：钓鱼参数
- `FishingBot.cs`：状态机
- `FishContext.cs`：运行态快照
- `IFishSignalSource.cs`：信号源接口
- `MockFishSignalSource.cs`：模拟信号源
- `IInputController.cs`：输入接口
- `ConsoleInputController.cs`：控制台输入实现
- `Logger.cs`：简单日志器
- `appsettings.json`：默认配置

## 后续可做
- 增加文件日志
- 增加 Web 面板
- 接入串口设备（仅限自有测试环境）
- 引入单元测试
