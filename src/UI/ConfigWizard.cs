using Spectre.Console;
using System.IO.Ports;
using VrcDmaFish.Models;
using System.Text.Json;

namespace VrcDmaFish.UI;

public static class ConfigWizard
{
    public static AppConfig Run(AppConfig current)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("VrcDmaFish").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[bold cyan]Ciallo~ 欢迎进入小菲配置向导喵！(∠・ω< )⌒★[/]\n");

        if (!AnsiConsole.Confirm("是否需要修改当前配置喵？", false)) return current;

        // 1. 选择输入类型
        current.Input.Type = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("请选择您的 [green]控制器类型[/] 喵:")
                .AddChoices(new[] { "Serial", "Net", "Mock" }));

        // 2. 根据类型配置细节
        if (current.Input.Type == "Serial")
        {
            var ports = SerialPort.GetPortNames();
            if (ports.Length > 0)
                current.Input.ComPort = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("发现串口，请选择盒子所在的端口喵:").AddChoices(ports));
            else
                current.Input.ComPort = AnsiConsole.Ask<string>("没找到串口喵，请手动输入端口号 (如 COM3):");
        }
        else if (current.Input.Type == "Net")
        {
            current.Input.NetIp = AnsiConsole.Ask<string>("请输入盒子的 [yellow]IP地址[/] 喵:", "192.168.2.188");
        }

        // 3. 配置张力阈值
        current.Bot.ReelTensionPauseThreshold = AnsiConsole.Ask<float>("设置 [red]张力暂停[/] 阈值 (0.1~1.0):", 0.8f);

        // 保存配置
        File.WriteAllText("appsettings.json", JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }));
        AnsiConsole.MarkupLine("\n[green]配置已保存！咱们出发去钓鱼吧喵！(≧▽≦)ゞ[/]");
        Thread.Sleep(1500);
        
        return current;
    }
}
