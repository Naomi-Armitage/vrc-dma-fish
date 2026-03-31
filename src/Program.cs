using System.Text.Json;
using Spectre.Console;
using VrcDmaFish.Inputs;

namespace VrcDmaFish;

public static class Program
{
    public static void Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("VRC-DMA-FISH").Centered().Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[bold cyan]Ciallo~ 欢迎使用小菲牌钓鱼机器人喵！(∠・ω< )⌒★[/]");

        var config = LoadConfig();
        
        IInputController input = config.Input.Type.ToLower() switch
        {
            "serial" => new SerialKmboxController(config.Input.ComPort, config.Input.BaudRate),
            "net" => new NetKmboxController(config.Input.NetIp, config.Input.NetPort),
            "mock" => new ConsoleInputController(),
            _ => throw new ArgumentException("Unknown input type喵!")
        };

        IFishSignalSource signalSource = new MockFishSignalSource();
        var bot = new FishingBot(signalSource, input, config.Bot);

        // 创建 UI 布局
        var layout = new Layout("Root")
            .SplitColumns(
                new Layout("Panel").Ratio(1),
                new Layout("Log").Ratio(2)
            );

        AnsiConsole.Live(layout)
            
            .Start(ctx =>
            {
                while (true)
                {
                    bot.Tick();

                    // 更新 UI 内容
                    var tensionVal = bot.LastContext.Tension * 100;
                    var tensionColor = tensionVal > 80 ? "red" : (tensionVal > 50 ? "yellow" : "green");

                    var statsTable = new Table().Border(TableBorder.Rounded);
                    statsTable.AddColumn("项目");
                    statsTable.AddColumn("当前值");
                    statsTable.AddRow("状态", $"[bold yellow]{bot.State}[/]");
                    statsTable.AddRow("鱼上钩", bot.LastContext.IsHooked ? "[bold red]YES[/]" : "[grey]NO[/]");
                    statsTable.AddRow("张力", $"[{tensionColor}]{tensionVal:F1}%[/]");
                    
                    var bar = new BarChart()
                        .Width(30)
                        .Label("[bold]张力监控[/]")
                        .AddItem("Tension", Math.Round(tensionVal), Color.FromInt32((int)tensionVal));

                    layout["Panel"].Update(
                        new Panel(
                            new Rows(
                                statsTable,
                                new Padder(bar, new Padding(0, 1, 0, 0))
                            )
                        ).Header("机器人仪表盘").BorderColor(Color.Cyan1)
                    );

                    ctx.Refresh();
                    Thread.Sleep(config.TickIntervalMs);
                }
            });
    }

    private static AppConfig LoadConfig()
    {
        const string path = "appsettings.json";
        if (!File.Exists(path))
        {
            var def = new AppConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true }));
            return def;
        }
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppConfig();
    }
}
