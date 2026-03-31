using System;
using VrcDmaFish;

namespace VrcDmaFish {
    class Program {
        static void Main(string[] args) {
            Console.WriteLine("Ciallo~ 欢迎使用小菲牌 VRC-DMA 钓鱼辅助！(∠・ω< )⌒★");
            
            DmaHandler dma = new DmaHandler();
            if (!dma.Initialize()) return;

            FishingBot bot = new FishingBot(dma);

            while (true) {
                bot.Tick();
                System.Threading.Thread.Sleep(100); // 降低 CPU 占用喵
            }
        }
    }
}
