Checkers bot API
This project implements a REST Web API that accepts a checkers position (PDN) and returns the best move using a native checkers engine (KingsRow / Chinook). It is designed to run on Windows with IIS and .NET and to use Chinook 2–8 piece endgame databases for perfect play in endgames.
Purpose
•	Create a checker-playing bot accessible over HTTP that accepts PDN positions and returns the best move and search information.
•	Host as a normal ASP.NET Core Web API app behind IIS on Windows Server.
•	Integrate a native engine (KingsRow / Chinook) via a long-lived host process to avoid native crashes taking down the web app.
Stack
•	Windows Server
•	IIS (ASP.NET hosting)
•	.NET 10 (projects target net10.0)
•	KingsRow / Chinook native engine and Chinook endgame databases
Practical Windows notes
•	Publish and run the engine host as an x64 process and ensure the native DLLs are present and readable.
•	KingsRow native builds and documentation:
•	https://edgilbert.org/Checkers/KingsRow.htm
•	https://edgilbert.org/EnglishCheckers/KingsRowEnglish.htm
•	Use ASCII (ANSI) paths for engine files because the native engine reads them as ANSI.
How it works
•	The API exposes endpoints that validate requests, route work to a small pool of long-lived engine worker processes, and return the engine's best move and metadata.
•	The engine host is a small console program that loads KingsRow/Chinook and communicates over JSON lines on stdio. The API spawns and manages several host processes (workers) on startup and restarts them if they fail.
•	The application probes Chinook databases for positions with few pieces (<= 8) and returns perfect tablebase moves immediately when available.
•	For non-tablebase positions, the API asks the engine to search with the requested limits (depth or movetime) and verifies that the returned move is legal.
Engine adapter contract
The adapter exposes methods used by the application:
•	setPosition(pdn)
•	search(limits) -> { bestMove, pv, scoreOrWDL, nodes, depth, tablebaseHit }
Worker pool
•	Keep a small pool of long-lived engine worker processes (default: 2 workers). Do not spawn per request.
•	Each worker is dedicated to one native engine instance. Request routing is round-robin with an async lock per worker to prevent concurrent use.
•	On application start the pool is created and warmed up; health checks return 503 until all workers are ready.
API endpoints
POST /v1/move/suggest
Request example: { "gameId": "checkers-8x8", "state": { "notation": "PDN", "position": "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16" }, "level": "weak", "limits": { "maxDepth": 12, "softTimeMs": 250, "hardTimeMs": 1200 } }
Response example: { "engine": "chinook", "bestMove": "22-18x11-7", "pv": ["22-18","5-9","18x11","7-16","30-26"], "scoreOrWDL": 0, "depth": 12, "nodes": 153201, "positionKey": "pdn:B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16", "info": { "tablebaseHit": false, "timeMs": 281 } }
POST /v1/move/validate
Input: { "position": "PDN", "move": "22-18" }
Output: { "legal": true | false, "reason": "optional" }
GET /healthz
Output: { "ok": true|false, "workers": <number>, "engine": "ready|starting|error" }
Strength levels
•	weak: depth 6–8 or movetime ~100 ms. Fast play, no randomness.
•	medium: depth 10–12 or movetime ~250 ms.
•	strong: probe tablebases first (for <= 8 pieces). If not in DB, depth 14–18 or movetime 500–600 ms.
Request flow
1.	Parse and normalize PDN. Validate squares and piece counts. Return 422 for invalid requests.
2.	If total pieces <= 8, probe the tablebase for an immediate answer (return with tablebaseHit=true).
3.	Otherwise route the request to a worker and invoke search with limits based on the requested level and provided limits.
4.	Verify the engine's returned move is legal. If the best move is illegal, try the next PV move or return an EngineFailure (HTTP 500).
5.	Cache results by canonical PDN (LRU) for 15 minutes to speed repeated queries.
6.	Enforce softTimeMs in the adapter, and hardTimeMs at the controller level with a CancellationToken.
Caching
•	In-memory LRU cache keyed by canonical PDN.
•	Default capacity: 20,000 entries. TTL: 15 minutes.
Operations and hosting
•	Run as a normal ASP.NET Core Web API app behind IIS. No Windows Service required.
•	On app startup create N engine workers (default: 2) and warm them up.
•	Route requests to workers using round-robin; protect per-worker usage with async locks.
•	Restart workers that exit or become unresponsive.
•	Log JSON per request: requestId, timeMs, depth, nodes, tablebaseHit, cached.
Configuration example (appsettings.json)
{ "Engine": { "Type": "chinook", "Path": "C:\engines\chinook\chinook.exe", "HostPath": "engine-host\Checkers.Engine.KingsRowHost.exe", "Home": "C:\engines\kingsrow", "Databases": "D:\tb\chinook", "Workers": 2, "HashMb": 128, "DatabaseCacheMb": 256, "StartupTimeoutSeconds": 180, "StopGraceMs": 2000 }, "Cache": { "Capacity": 20000, "TtlMinutes": 15 }, "Limits": { "DefaultSoftTimeMs": 300, "DefaultHardTimeMs": 1200 }, "Levels": { "Weak": { "MaxDepth": 8, "SoftTimeMs": 100 }, "Medium": { "MaxDepth": 12, "SoftTimeMs": 250 }, "Strong": { "MaxDepth": 18, "SoftTimeMs": 500 } } }
Notes on native files
•	Engine:Home must point at a folder containing the KingsRow files, typically: C:\engines\kingsrow\egdb64.dll C:\engines\kingsrow\engines\Kingsrow64.dll other weights/bin files as required by KingsRow
•	Engine:Databases should point at the Chinook endgame DB folder (optional but required for tablebase probes).
•	Ensure the host executable and native DLLs are unblocked (Windows mark) and readable by the process.
Acceptance criteria
•	Health check returns ok when the app and workers are ready.
•	Tablebase positions with <= 8 pieces return within ~50 ms with tablebaseHit=true (depends on DB I/O).
•	Strong-level midgame positions return within ~600 ms with a legal move.
•	Invalid PDN requests return 422. Hard-timeout breaches return 504. Engine failures return 500.
Testing and validation
•	Unit/integration tests in tests/Checkers.Tests exercise the application logic and the worker pool with fakes; they do not require native DLLs.
•	For end-to-end testing with the real engine:
1.	Publish the host for win-x64: dotnet publish src\Checkers.Engine.KingsRowHost -r win-x64 --self-contained false -c Release -o src\Checkers.Api\bin\Debug\net10.0\engine-host
2.	Ensure Engine:Home and Engine:Databases are set correctly in appsettings.json or as environment variables.
3.	Start the API and monitor /healthz until it reports ready.
4.	Use the provided smoke-test script to validate basic acceptance scenarios.
Final delivery
•	The service should be deployed as an ASP.NET Core app under IIS.
•	A simple web board or small client app (web or Windows) can be provided to exercise the API and verify it works with the engine; the GUI is only required as a smoke test, not as a production UI.
Contact / troubleshooting
•	If the API responds with EngineUnavailableException, check logs for details (missing Engine:Home, host start failure, startup timeout, or process exit). Ensure the host exe and native DLLs are present and that the host is published for win-x64.
•	If tablebase probes never return hits, verify Engine:Databases path and that the Chinook DB files are present.
License and attribution
See the repository LICENSE for licensing details. KingsRow and Chinook are third-party native engines; consult their sites for licensing and redistribution rules.