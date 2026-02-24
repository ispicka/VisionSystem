using Microsoft.Extensions.DependencyInjection;
using Vision.Core;
using Vision.IO.Cameras;
using Vision.IO.Plc;
using Vision.Service.Services;
using Vision.Service.State;

namespace Vision.Service.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVisionEngine(this IServiceCollection services, string plcIp, string cameraSource)
    {
        services.AddSingleton<AppState>();

        services.AddSingleton<IPlcClient>(_ => new S7NetPlcClient(plcIp, rack: 0, slot: 2, dbNumber: 1122));
        services.AddSingleton<ICameraProvider>(_ => CameraProviderFactory.Create(cameraSource));

        // Placeholder detektory (Left/Right)
        services.AddSingleton<ILeftGapDetector, PlaceholderLeftGapDetector>();
        services.AddSingleton<IRightGapDetector, PlaceholderRightGapDetector>();

        services.AddHostedService<EngineHostedService>();

        return services;
    }
}
