# Checkers bot API

This project implements a REST Web API that accepts a checkers position (PDN) and returns the best move using a native checkers engine (KingsRow / Chinook). It is designed to run on Windows with IIS and .NET and to use Chinook 2–8 piece endgame databases for perfect play in endgames.

## Purpose

- Provide a checker-playing bot over HTTP that accepts PDN and returns the best move and search metadata.
- Host as a standard ASP.NET Core Web API app behind IIS on Windows Server.
- Integrate a native engine (KingsRow / Chinook) via long-lived host processes to isolate native code.

## Stack

- Windows Server
- IIS (ASP.NET hosting)
- .NET 10 (projects target net10.0)
- KingsRow / Chinook native engine and Chinook endgame databases

## Practical Windows notes

- Publish and run the engine host as an x64 process and ensure native DLLs are present and readable.
- KingsRow documentation:
  - https://edgilbert.org/Checkers/KingsRow.htm
  - https://edgilbert.org/EnglishCheckers/KingsRowEnglish.htm
- Use ASCII (ANSI) paths for engine files because the native engine reads them as ANSI.

## How it works

- The API validates requests, routes work to a small pool of long-lived engine worker processes, and returns the engine's best move and metadata.
- The engine host is a console program that loads KingsRow/Chinook and communicates over JSON lines on stdio.
- Positions with few pieces (<= 8) are probed against the Chinook tablebases and return perfect moves when available.
- Non-tablebase positions are searched by the engine with limits (depth or movetime); returned moves are validated by the API.

## Engine adapter contract

- setPosition(pdn)
- search(limits) -> { bestMove, pv, scoreOrWDL, nodes, depth, tablebaseHit }

## Worker pool

- Default pool size: 2 workers (long-lived processes). Do not spawn per request.
- Round-robin routing with per-worker async lock to prevent concurrent use.
- Workers are warmed on startup; /healthz returns 503 until ready.

## Endpoints

POST /v1/move/suggest
- Request: { gameId, state: { notation, position }, level, limits }
- Response: { engine, bestMove, pv, scoreOrWDL, depth, nodes, positionKey, info: { tablebaseHit, timeMs, cached } }

POST /v1/move/validate
- Input: { position, move }
- Output: { legal: true|false, reason?: string }

GET /healthz
- Output: { ok: boolean, workers: number, engine: "ready|starting|error" }

## Strength levels

- weak: depth ~6–8 or movetime ~100 ms.
- medium: depth ~10–12 or movetime ~250 ms.
- strong: probe tablebases first (<= 8 pieces); otherwise depth ~14–18 or movetime 500–600 ms.

## Request flow

1. Parse and normalize PDN; return 422 for invalid requests.
2. If pieces <= 8, probe tablebase and return if found.
3. Otherwise route to a worker and run search with resolved limits.
4. Verify returned move is legal; if not, try PV entries or return 500.
5. Cache canonical PDN responses (LRU, 15 minutes).
6. Enforce softTimeMs in adapter; enforce hardTimeMs at controller with CancellationToken.

## Caching

- In-memory LRU cache keyed by canonical PDN.
- Default: 20,000 entries, TTL 15 minutes.

## Operations and hosting

- Run as ASP.NET Core Web API under IIS; no Windows Service required.
- On startup create N workers (default 2) and warm them up.
- Restart workers that exit or hang.
- Log JSON per request: requestId, timeMs, depth, nodes, tablebaseHit, cached.

## Configuration example (appsettings.json)

{
  "Engine": {
	"Type": "chinook",
	"Path": "C:\\engines\\chinook\\chinook.exe",
	"HostPath": "engine-host\\Checkers.Engine.KingsRowHost.exe",
	"Home": "C:\\engines\\kingsrow",
	"Databases": "D:\\tb\\chinook",
	"Workers": 2,
	"HashMb": 128,
	"DatabaseCacheMb": 256,
	"StartupTimeoutSeconds": 180,
	"StopGraceMs": 2000
  },
  "Cache": { "Capacity": 20000, "TtlMinutes": 15 },
  "Limits": { "DefaultSoftTimeMs": 300, "DefaultHardTimeMs": 1200 }
}

## Testing and validation

- Unit/integration tests in tests/Checkers.Tests use fakes for the engine and do not require native DLLs.
- End-to-end steps with real engine:
  1. Publish the host for win-x64:
	 dotnet publish src\\Checkers.Engine.KingsRowHost -r win-x64 --self-contained false -c Release -o src\\Checkers.Api\\bin\\Debug\\net10.0\\engine-host
  2. Set Engine:Home and Engine:Databases in appsettings.json or environment variables.
  3. Start API and wait for /healthz to report ready.
  4. Run scripts\smoke-test.ps1 to validate scenarios.

## Acceptance criteria

- /healthz reports ready when app and workers are initialized.
- Tablebase positions (<= 8 pieces) return quickly with tablebaseHit=true.
- Strong-level midgame positions return within ~600 ms with a legal move.
- Invalid PDN returns 422; hard-timeout returns 504; engine failures return 500.

## Notes & troubleshooting

- EngineUnavailableException indicates missing configuration, host start failure, startup timeout or worker exit; check logs.
- Ensure native DLLs are unblocked and readable by the host process.

## Credits

- [KingsRow](https://edgilbert.org/EnglishCheckers/KingsRowEnglish.htm) by Ed Gilbert
- [Chinook endgame databases](https://webdocs.cs.ualberta.ca/~chinook/databases/) by Jonathan Schaeffer and the Chinook team, University of Alberta
- Engine interface after [CheckerBoard](https://github.com/eygilbert/CheckerBoard) by Martin Fierz.
