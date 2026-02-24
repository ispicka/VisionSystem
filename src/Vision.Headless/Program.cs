using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vision.Core;
using Vision.Service.Composition;
using Vision.Service.State;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        cfg.AddEnvironmentVariables(prefix: "VISION_");
    })
    .ConfigureServices((ctx, services) =>
    {
        var plcIp = ctx.Configuration["Plc:Ip"] ?? "192.168.0.10";
        var camSource = ctx.Configuration["Cameras:Source"] ?? "C:\\data\\frames";
        services.AddVisionEngine(plcIp, camSource);
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    })
    .Build();

var state = host.Services.GetRequiredService<AppState>();
var modeStr = host.Services.GetRequiredService<IConfiguration>()["Mode"] ?? "Manual";
state.Mode = Enum.TryParse<ControlMode>(modeStr, ignoreCase: true, out var m) ? m : ControlMode.Manual;
state.AddMsg($"Mode = {state.Mode}");

// Periodic status print
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
_ = Task.Run(async () =>
{
    while (!lifetime.ApplicationStopping.IsCancellationRequested)
    {
        var snap = state.Snapshot();
        Console.WriteLine($"{snap.Timestamp:HH:mm:ss} PLC={(snap.Plc.Connected ? "OK" : "DOWN")} Mode={snap.Mode} Gap={(snap.LastGap is null ? "n/a" : $"{snap.LastGap.LeftGapMm:F2}/{snap.LastGap.RightGapMm:F2}")} Act={(snap.LastAction?.Action.ToString() ?? "n/a")}");
        await Task.Delay(1000);
    }
});

await host.RunAsync();
