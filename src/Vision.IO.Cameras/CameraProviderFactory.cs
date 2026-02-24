using System;
using Vision.IO.Cameras.Providers;

namespace Vision.IO.Cameras;

public static class CameraProviderFactory
{
    public static ICameraProvider Create(string source)
    {
#if PC
        return new FileFolderCameraProvider(source);
#elif RPI
        return new RpiCameraProvider();
#else
        throw new InvalidOperationException("Define build constant PC or RPI (e.g., -p:DefineConstants=PC).");
#endif
    }
}
