# VrcDmaFish 🎣 [v1.1-AutoConfig Branch]

基于 **DMA (Direct Memory Access)** 硬件技术的 VRChat 全自动钓鱼机器人。本分支致力于实现“开箱即用”的自动化体验。

> **Warning**
> 本项目仅供技术研究，请遵守相关法律法规，文明钓鱼喵！(∠・ω< )⌒★

---

## ✨ 分支特性 (v1.1)

- 🔍 **智能雷达 (UnityScanner)**: 基于 GOM 特征码自动定位 `FishingLogic` 对象，无需手动搜偏移。
- ⚡ **KMBOX 兼容性**: 完美支持 B+ (Serial) 与 NET 版本的硬件控制器。
- 📊 **现代化仪表盘**: 使用 Spectre.Console 构建的分栏式实时监控界面。
- 🛠️ **一键部署**: `setup.ps1` 智能识别地域（海外直连/国内镜像），自动下发 FTD601/vmm 驱动。
- ✅ **编译修复**: 已解决 `Vmmsharp` 命名空间大小写导致的编译阻断问题。

---

## 🚀 快速开始

### 1. 克隆与切换分支
```bash
git clone https://github.com/Naomi-Armitage/vrc-dma-fish
cd vrc-dma-fish
git checkout feature/v1.1-auto-config
```

### 2. 部署运行环境 (副机/攻击机)
在 PowerShell 中右键运行 `setup.ps1`。
该脚本会自动检测您的 IP 地理位置，并从 GitHub 或加速镜像下载最新的 `MemProcFS` 相关二进制文件（包含 FT601 兼容驱动）。

### 3. 编译并启动
```bash
dotnet build
dotnet run
```
启动后，请跟随 **Interactive Config Wizard**（交互式配置向导）完成 KMBOX 端口设置喵。

---

## 📂 项目架构
- `src/Providers/UnityScanner.cs`: Unity 对象树自动搜索逻辑。
- `src/Providers/DmaProvider.cs`: DMA 内存读取核心实现（已接入真实 VMM 实例）。
- `src/UI/ConfigWizard.cs`: 交互式配置菜单。

## 📝 开发者备注
- 所有的 DMA 依赖项（`vmm.dll` 等）必须与 `VrcDmaFish.exe` 处于同一目录。
- 如需自定义偏移，请修改 `DmaProvider.cs` 中的 `Read()` 逻辑。

---
*Powered by Naomi-Armitage & WhatIsTheMel0dy* (≧ω≦)ゞ
