# Checkers bot API

A REST service that takes a checkers position in PDN and returns the best move, played by **KingsRow** (`Kingsrow64.dll`) with the **Chinook 8-piece endgame database** read through `egdb64.dll`. There is no search or evaluation code of our own: the only chess logic in the solution is the rules code (legal moves), which is needed to validate moves and to read the engine's answer. A test board is served at `/`.

> **Status.** The C# was written without a .NET SDK or a Windows machine to hand, so none of it has been compiled or run against the real DLLs. The rules and the board-layout logic were checked against a reference implementation (see "How KingsRow is driven"). Run `dotnet build` and `dotnet test` first, then `scripts\smoke-test.ps1` against the running service, and fix whatever those find.

## Projects

```
src/
  Checkers.Core                      Rules: Board, Move, PDN, legal moves. No dependencies.
  Checkers.Engine.Protocol           Contracts between the API and the engine host: SearchRequest, SearchResponse, JSON lines.
  Checkers.Application               Use cases and the port to the engine: suggest a move, validate, level policy, cache.
  Checkers.Engine                    Infrastructure: starts KingsRow host processes, round-robin worker pool, warm-up.
  Checkers.Engine.KingsRowHost       Console program that loads Kingsrow64.dll and egdb64.dll in its own process.
  Checkers.Api                       ASP.NET Core: controllers, configuration, the test board (wwwroot).
tests/
  Checkers.Tests                     xUnit tests; the application and pool tests use fakes, so no DLLs are needed.
scripts/
  publish.ps1                        Publishes the API and the host together.
  Install-ChinookDb.ps1              Downloads and unpacks the Chinook databases.
  smoke-test.ps1                     Checks a running service against the spec's acceptance list.
```

Dependencies point inwards. `Core` depends on nothing. `Application` depends on `Core` and `Protocol` and owns the `IEngineGateway` interface. `Engine` implements that interface. `Api` wires them together. The host references only `Core` and `Protocol`.

Why a separate host process: KingsRow keeps global state, so two searches cannot share one loaded DLL, and a native crash must not take the web app down. Each worker is its own process with its own copy of the DLL, and the API restarts one that dies or fails to stop.

## What you need on the server

Windows x64, the .NET 10 runtime or Hosting Bundle (the projects target `net10.0`; change `TargetFramework` in `Directory.Build.props` for another version), and a folder with the KingsRow files laid out like a CheckerBoard installation:

```
C:\engines\kingsrow\                 <- Engine:Home
    egdb64.dll
    engines\
        Kingsrow64.dll
        weights_v4.bin               KingsRow reads these at its first search;
        weights_nk_v4.bin            copy every file from your CheckerBoard\engines folder
        Kingsrow.odb                 opening book (not used: the book is switched off)
```

The simplest way to get it is to copy your CheckerBoard folder. The host looks for each DLL in `Home\engines\` and then in `Home\`. `Engine:KingsRowDll` and `Engine:EgdbDll` override that.

The endgame database goes in its own folder (`Engine:Databases`). To get the Chinook one: `.\scripts\Install-ChinookDb.ps1 -Destination D:\tb\chinook` (2.7 GB download, 5.6 GB on disk). `egdb64.dll` also reads KingsRow's own 8 and 10 piece databases and Cake's. Without a database the service still works; it just never reports a tablebase hit.

## Build, run, test

```
dotnet build
dotnet test tests\Checkers.Tests
.\scripts\publish.ps1 -Output C:\inetpub\checkers-api
```

For development, publish the host once (`dotnet publish src\Checkers.Engine.KingsRowHost -r win-x64 --self-contained false -o src\Checkers.Api\bin\Debug\net10.0\engine-host`), set `Engine:Home` and `Engine:Databases`, then `dotnet run --project src\Checkers.Api` and open http://localhost:5080. The first start takes a while: each worker loads KingsRow, calibrates, and does its first search, which is when KingsRow initializes its endgame database. `/healthz` returns 503 until all workers are ready.

## Configuration (`appsettings.json`)

| Setting | Meaning |
|---|---|
| `Engine:Home` | Folder with the KingsRow files (see above). Required. |
| `Engine:Databases` | Endgame database folder. Optional. |
| `Engine:Workers` | Number of KingsRow processes (default 2). |
| `Engine:HashMb`, `Engine:DatabaseCacheMb` | KingsRow's hash table and database cache, per worker (128 and 256). Total memory is about workers times (hash + cache + 100 MB). |
| `Engine:HostPath` | The host exe, relative to the API folder (`engine-host\Checkers.Engine.KingsRowHost.exe`). |
| `Engine:StartupTimeoutSeconds` | How long a worker may take to become ready (180). |
| `Engine:StopGraceMs` | After a timeout, how long a worker gets to stop before it is killed and restarted (2000). |
| `Levels:Weak/Medium/Strong` | `MaxDepth` and `SoftTimeMs` per level: 8 / 100 ms, 12 / 250 ms, 18 / 500 ms. |
| `Limits:DefaultHardTimeMs` | Ceiling for the hard time (1200). Requests can lower it, not raise it. |
| `Limits:TablebaseProbeMs` | Search time for the endgame database attempt (40). |
| `Cache` | LRU capacity and time to live (20000 entries, 15 minutes). |

Environment variables work too, for example `Engine__Workers=4`.

## Endpoints

`POST /v1/move/suggest` takes `{ gameId, state: { notation, position }, level, limits }` as in the spec and returns `{ engine, bestMove, pv, scoreOrWDL, depth, nodes, positionKey, info: { tablebaseHit, timeMs, cached } }`. For a tablebase hit `scoreOrWDL` is -1, 0 or +1 (loss, draw, win for the side to move); otherwise it is KingsRow's score for the side to move, in centipawn-like units.

`POST /v1/move/validate` takes `{ position, move }`, returns `{ legal, reason? }`. `GET /healthz` returns `{ ok, workers, engine, databasePieces }`, or 503 while starting. `POST /v1/move/legal` is an extra used by the test board: every legal move with the position it leads to.

Errors are RFC 7807 problem responses: **422** invalid request or PDN (the `code` field says which), **503** engine not ready, **504** hard time limit passed, **500** engine failure.

Moves are written `22-18` and `22x15x8` (the squares the piece lands on). The spec's sample `22-18x11-7` does not match its own `pv`, so it was not followed.

## How a request flows

1. **Prepare.** Parse and normalize the PDN, check squares and piece counts, resolve level and limits (request limits can only tighten the level). Invalid gives 422.
2. **Database attempt.** If the piece count is within the database's range, search for `TablebaseProbeMs`. KingsRow answers a position in the database immediately; if the answer is a database result, it is returned.
3. **Search.** Otherwise search with the level's time and depth cap.
4. **Check.** The move must be legal; if not, the moves of the principal variation are tried, then 500.
5. **Cache** by level, limits and canonical PDN for 15 minutes.

The hard time limit is a `CancellationToken` set in the controller. When it fires, the host is told to stop (KingsRow returns its best move so far), the worker stays usable, and the request gets 504.

## How KingsRow is driven

`Kingsrow64.dll` exports the CheckerBoard engine interface. The host uses two of its functions:

- `enginecommand(command, reply)` for options. At startup it sets: `book 0` (the book chooses randomly, and weak must be deterministic), `hashsize`, `dbmbytes`, `dbpath`, `enable_wld`, `max_dbpieces`, `searchthreads 1`, `enable_mtc 0`. Everything is set explicitly because KingsRow otherwise uses what it saved in the registry.
- `getmove(board[8][8], color, maxtime, status, playnow, info, moreinfo, move)` for each search. `info` bit 1 ("exact time") is set so a search stops at the time limit instead of using up to three times its nominal time; without it a 500 ms level would not meet the 600 ms acceptance limit. A depth cap is applied by polling the status line and setting `playnow` when the depth is reached. `playnow` is also how a cancelled request stops the search.

`getmove` returns the position after its move, not the move. The host works out the move by finding the legal move whose result matches the returned board, so the move is always legal and never depends on parsing text. The status line (value, depth, speed, principal variation) is parsed leniently for the extras, in all of KingsRow's formats. Positive values favour Black. A value of ±1 is an endgame database draw; the database-only formats (a draw list with probabilities, "conv. in", "wins in") and values over 2000 count as database results when the position is within the database's range.

**Board layout.** How the 8x8 array is laid out (which corner is `[0][0]`, which index is the column) is not documented in the files provided, so it is not assumed. At startup the host tries the eight possible layouts on two real positions and keeps the one for which KingsRow's reply is a legal move; the log says which one it found (`board layout: ...`). The expected one is `board[x][y]` with White at `y = 0`. This was verified by simulation: each of the eight layouts is identified uniquely, and the other seven are rejected.

`egdb64.dll` is loaded first, by full path, so `Kingsrow64.dll` (which imports it) reuses that copy, and the host asks it (`egdb_identify`) which database is in the folder and how many pieces it covers. KingsRow does the lookups.

## Deploying under IIS

1. Install the .NET Hosting Bundle and restart IIS. Run `scripts\publish.ps1 -Output C:\inetpub\checkers-api`.
2. Create a site or application on that folder with an application pool set to **No Managed Code**, 64-bit.
3. The pool's identity needs read access to the KingsRow and database folders, permission to start processes, and a profile: enable **Load User Profile** so KingsRow can read its settings and write its log.
4. Set `Engine:Home` and `Engine:Databases` (in `appsettings.json` or as environment variables on the pool).
5. The engine starts in the background after the first request, so set the pool's Start Mode to `AlwaysRunning` and the site's Preload Enabled to true (needs the Application Initialization feature). Until the workers are ready, `/healthz` returns 503.
6. Logs are JSON, one line per request, on stdout (`stdoutLogEnabled` in `web.config` writes them to a file). Each suggest line has `RequestId`, `TimeMs`, `Depth`, `Nodes` and `TablebaseHit`. The host's own messages are logged as `kingsrow[n] ...`, including `endgame database: type ..., 8 pieces` and KingsRow's "Using the ... endgame database" line.

## Things to know

- **Chinook database coverage.** The Chinook 7 and 8 piece files only cover positions with at most 4 pieces on a side, so some positions with 8 pieces or fewer (for example 5 against 3) are searched rather than looked up, and `tablebaseHit` is false for them. KingsRow's own 10-piece database covers more.
- **No database result in the status line.** KingsRow does not say that an answer came from the database, so `tablebaseHit` is inferred (see above). It is only ever true for positions within the database's piece range, and the host switches the database off if KingsRow itself reports "Not using an endgame database".
- **Positions with a capture pending** are not in the databases, so they are searched normally.
- **Wins are not always the shortest.** The WLD database knows who wins, not how fast. KingsRow's separate MTC database helps with that (`enable_mtc`, off here).
- **No game history.** The API gets one position, so KingsRow cannot see earlier repetitions.
- **Paths.** Use ASCII paths for `Engine:Home` and `Engine:Databases`; KingsRow reads them as ANSI.
- **KingsRowEngine.cs** is the file you provided, with one edit: it only sends `set dbpath` when a database was found, because an empty path is not a valid command.
