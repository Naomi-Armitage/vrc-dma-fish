using System;
using System.Threading;

namespace VrcDmaFish {
    public enum FishState { Idle, Casting, Waiting, Hooked, Reeling }

    public class FishingBot {
        private FishState _state = FishState.Idle;
        private DmaHandler _dma;

        public FishingBot(DmaHandler dma) {
            _dma = dma;
        }

        public void Tick() {
            switch (_state) {
                case FishState.Idle:
                    Console.WriteLine("[Bot] 闲置中，准备抛竿喵...");
                    // TODO: 调用 Kmbox 抛竿
                    _state = FishState.Waiting;
                    break;
                case FishState.Waiting:
                    // 模拟从 DMA 读取 isHooked 变量
                    bool isHooked = false; // dma.ReadMemory<bool>(offset);
                    if (isHooked) {
                        Console.WriteLine("[Bot] 鱼上钩了！(๑>◡<๑)");
                        _state = FishState.Hooked;
                    }
                    break;
                case FishState.Hooked:
                    // 模拟拉竿逻辑
                    _state = FishState.Reeling;
                    break;
                case FishState.Reeling:
                    // 模拟读取张力并控制 Kmbox
                    Console.WriteLine("[Bot] 正在收绳，努力博弈中喵！");
                    Thread.Sleep(2000); // 模拟收绳过程
                    _state = FishState.Idle;
                    break;
            }
        }
    }
}
