# VisionSystem scaffold (PC + Raspberry Pi 5)

Jedna codebase, přepínání platformy pouze kompilátorem:

- PC: `-p:DefineConstants=PC`
- RPI: `-p:DefineConstants=RPI`

## Build & run

### PC (kamera = načítání z folderu)
```bash
cd src/Vision.Headless
dotnet restore
dotnet run -c Release -p:DefineConstants=PC
```

Folder layout (PC):
```
C:\data\frames\left\*.jpg
C:\data\frames\right\*.jpg
```

### Raspberry Pi 5 (kamera placeholder)
```bash
cd src/Vision.Headless
dotnet restore
dotnet run -c Release -p:DefineConstants=RPI
```

## Konfigurace
`src/Vision.Headless/appsettings.json`
- `Plc:Ip`
- `Cameras:Source`
- `Mode`: `Manual` nebo `Auto`

## Kde vložit tvůj algoritmus detekce mezery
Nahraď:
`src/Vision.Service/Services/PlaceholderGapDetector.cs`

A vrať:
- `LeftGapMm`
- `RightGapMm`
- `Quality` (0..1)

## PLC handshake (DB1122 ReservedForPublic)
`Vision.IO.Plc` používá soubor `CamCorrDb1122.cs` (přiložený) a volá `CamCorrHandshake.ExecuteOneStep(...)`.


## Nově: RegulationModule + Orchestrator
- Regulace je v `Vision.Core/Control/RegulationModule.cs` (2 instance L/R)
- Orchestrator smyčka je `Vision.Service/Services/EngineHostedService.cs`
- UI příkazy jsou připravené v `Vision.Service/State/AppState.cs` (RequestResetHandshake, RequestManualStep)

## UI (Avalonia)
Nový projekt: `src/Vision.UI`

Spuštění ve Visual Studiu:
- nastav jako Startup Project `Vision.UI`
- PC build: `DefineConstants=PC` (kamera = folder provider)
- RPI build: `DefineConstants=RPI` (kamera placeholder)

UI spouští backend (Orchestrator) v jednom procesu přes `Vision.UI/Services/EngineHost.cs`.
