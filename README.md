# 🌸💽 Saki-ML

Spam classification microservice for Saki using ML.NET. It exposes a fast HTTP API to classify messages (ham/spam), backed by a background queue and a safe single prediction worker. Ships as a single-file, self-contained binary and Docker images (Debian-slim and Alpine).

## Features
- ML.NET text classification (ham/spam) using the generated `SpamClassifier`
- Background queue with a single worker for thread-safe inference
- Simple API key header authentication
- Minimal HTTP API: `/classify` and `/health`
- Analytics endpoints: `/analytics`, `/analytics/last/{minutes}`
- Unsure insights: `/insights/unsure`, `/insights/unsure/last/{minutes}`
- Configuration diagnostics: `/config/diagnostics`
- Single-file, self-contained builds with ReadyToRun
- Docker images for Debian-slim (recommended) and Alpine (experimental)

## Requirements
- For local runs: .NET 9 SDK (to build/publish) or use Docker
- Port 8080 available (configurable via `ASPNETCORE_URLS`)

## Quick start (Docker)
### Debian-slim (recommended)
```bash
docker build -t saki-ml:latest -f Dockerfile .
docker run --rm -p 8080:8080 -e SAKI_ML_API_KEY=your-strong-key saki-ml:latest
```

### Alpine (experimental)
```bash
docker build -t saki-ml:alpine -f Dockerfile.alpine .
docker run --rm -p 8080:8080 -e SAKI_ML_API_KEY=your-strong-key saki-ml:alpine
```

## Quick start (single-file binary)
Publish and run the self-contained executable (no .NET runtime required on target):

Linux (glibc):
```bash
dotnet publish Saki-ML.csproj -c Release -r linux-x64 --self-contained true -o out/linux-x64
chmod +x out/linux-x64/Saki-ML
SAKI_ML_API_KEY=your-strong-key ./out/linux-x64/Saki-ML
```

Linux (Alpine/musl):
```bash
dotnet publish Saki-ML.csproj -c Release -r linux-musl-x64 --self-contained true -o out/linux-musl-x64
chmod +x out/linux-musl-x64/Saki-ML
SAKI_ML_API_KEY=your-strong-key ./out/linux-musl-x64/Saki-ML
```

Windows:
```powershell
dotnet publish Saki-ML.csproj -c Release -r win-x64 --self-contained true -o out/win-x64
setx SAKI_ML_API_KEY your-strong-key
out\win-x64\Saki-ML.exe
```

## Configuration
- `SAKI_ML_API_KEY` (required in production): API key for requests
- `QueueCapacity` (optional, default 1000): bounded queue size
- `ASPNETCORE_URLS` (optional): host/port binding, default `http://0.0.0.0:8080`
- `UnsureThreshold` (optional, default 0.75): if top confidence is below this, verdict is `Unsure`
- `BlockThreshold` (optional, default 0.85): minimum confidence to auto-block when label is `spam`

## API
### Health
- `GET /health`
- Response: `{ "status": "ok" }`

### Classify
- `POST /classify`
- Headers: `x-api-key: your-strong-key`
- Body:
```json
{ "Text": "win an instant discord nitro giveaway, click here!" }
```
- Response:
```json
{
  "PredictedLabel": "spam",
  "Confidence": 0.94,
  "Scores": [
    { "Label": "spam", "Score": 0.94 },
    { "Label": "ham",  "Score": 0.06 }
  ],
  "Verdict": "Block",
  "Blocked": true,
  "Color": "#DC2626",
  "DurationMs": 1.72,
  "Explanation": "High confidence spam; message should be blocked."
}
```
### Analytics
- `GET /analytics` → lifetime process stats
- `GET /analytics/last/{minutes}` → rolling window stats

### Unsure insights
- `GET /insights/unsure?take=50` → most recent unsure items
- `GET /insights/unsure/last/{minutes}?take=50` → unsure items in a time window

### Configuration diagnostics
- `GET /config/diagnostics` → recommended settings and warnings

Example curl:
```bash
curl -X POST http://localhost:8080/classify \
  -H "Content-Type: application/json" \
  -H "x-api-key: your-strong-key" \
  -d '{"Text":"win an instant discord nitro giveaway, click here!"}'
```

## Model
- The model file is `Models/SpamClassifier.mlnet`. It is copied to the output folder.
- At runtime it is loaded from `AppContext.BaseDirectory/Models/SpamClassifier.mlnet`.
- If you retrain, replace that file and rebuild/publish.

## Architecture notes
- A bounded `Channel<ClassificationRequest>` is used to queue work.
- A single background worker processes items to ensure `PredictionEngine` safety.
- For higher throughput, consider adding more workers with separate prediction engine instances and measuring CPU/memory.

## Security
- Requests must include the `x-api-key` header matching `SAKI_ML_API_KEY`.
- For production, terminate TLS at a reverse proxy or use container-level TLS.

## Troubleshooting
- 401 Unauthorized: ensure `x-api-key` header matches `SAKI_ML_API_KEY`.
- Model not found: ensure `Models/SpamClassifier.mlnet` exists at build-time and is copied to the output.
- Alpine runtime issues: the Alpine image includes `icu-libs`, `gcompat`, and other native deps required by ML.NET/TorchSharp.
- GPU inference: not enabled in these images. For GPU, use appropriate CUDA base images and packages.

## Development
Run locally with the development key (for testing only):
```bash
SAKI_ML_API_KEY=dev-key dotnet run
```

Then call the API as shown above.

