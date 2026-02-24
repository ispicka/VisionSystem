using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vision.Service.Composition;
using Vision.Service.State;

namespace Vision.UI.Services;

/// <summary>
/// Starts the same backend engine (Orchestrator) inside the UI process.
/// Later you can split to separate service if needed.
/// </summary>
public static class EngineHost
{
    private static IHost? _host;

    public static AppState State { get; private set; } = null!;

    public static async Task StartAsync(string? appsettingsPath = null, CancellationToken ct = default)
    {
        if (_host is not null) return;

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
            {
                if (!string.IsNullOrWhiteSpace(appsettingsPath))
                    cfg.AddJsonFile(appsettingsPath, optional: true, reloadOnChange: true);
                else
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

        State = _host.Services.GetRequiredService<AppState>();

        await _host.StartAsync(ct);
    }

    public static async Task StopAsync(CancellationToken ct = default)
    {
        if (_host is null) return;
        await _host.StopAsync(ct);
        _host.Dispose();
        _host = null;
    }
}
