# DeskVerse

A Windows-native wallpaper and desktop experience engine. Discover, import, curate, and
rotate static and video wallpapers — with an on-device recommendation engine, a hardened
security layer around every file and network path, and a token-authenticated local API.

Built with WinUI 3 on .NET 10. Nothing leaves the device except the search terms you type
into a catalog.

---

## Features

| Area | What it does |
|---|---|
| **Discover** | Aggregates Wallhaven.cc, the local library, and a deterministic mock catalog concurrently. One slow provider never blocks the others; per-provider failures surface in the UI instead of failing the search. |
| **Library** | Import local images and videos, or download from a catalog. Search, filter by kind/category/favorites/pinned/cached/imported, sort six ways, paginate. |
| **Static wallpapers** | Applied through `IDesktopWallpaper` COM with a `SystemParametersInfo` fallback. The wallpaper active before the first DeskVerse apply is remembered and restorable. |
| **Video wallpapers** | `MediaPlayerElement` hosted in a window parented into the desktop `WorkerW` layer, so playback sits under the icons and survives `Win+D`. |
| **Engine controls** | Apply, pause, resume, stop, restore previous — wired end-to-end in the UI and the API. |
| **Rotation** | Background scheduler with sequential / random / favorites-only / recommended / collection modes and a configurable interval. |
| **Recommendations** | Scores the library on preference match, learned category affinity from usage history, visual features (dominant color, brightness, density), and freshness. Every score ships with its weighted factors and a plain-language explanation. |
| **Surprise me** | One call picks the top recommendation and applies it. |
| **Duplicates** | Exact content-hash groups plus visual-similarity candidates for review. |
| **Storage** | Configurable cache directory and size limit, live health reporting, LRU cleanup that never evicts the active or a pinned wallpaper, and crash reconciliation of staged files. |
| **Local API** | 40 REST endpoints on `127.0.0.1`, OS-assigned port, bearer token. See [docs/API.md](docs/API.md). |
| **First run** | Onboarding dialog that picks the cache location and storage limit before anything is written. |
| **Multi-monitor** | Display topology monitoring; a changed layout raises a message that re-lays out per-display targets. Video wallpapers target a specific display. |

---

## Getting started

### Prerequisites

- Windows 10 19041 or later, x64 (or ARM64).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Visual Studio 2022 17.10+ with the *Windows application development* workload, or any
  editor — the app is unpackaged, so no deployment tooling is required to run it.

### Build and run

```powershell
dotnet build DeskVerse.slnx
dotnet run --project src/Deskverse.App
```

The executable lands in
`src/Deskverse.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/Deskverse.App.exe`
and can be launched directly.

### Test

```powershell
dotnet test DeskVerse.slnx
```

155 tests across three projects: `Deskverse.UnitTests` (pure logic),
`Deskverse.SecurityTests` (path, URL, and file-validation attacks), and
`Deskverse.IntegrationTests` (real SQLite, real migrations, the local API over HTTP).

---

## Project layout

```
src/
  Deskverse.Core            entities, enums, query models, all shared interfaces
  Deskverse.Infrastructure  EF Core + SQLite, repositories, Win32 environment, migrations
  Deskverse.Security        path safety, URL policy, safe downloads, file validation
  Deskverse.Storage         managed cache: accounting, LRU eviction, reconciliation
  Deskverse.Intelligence    recommendation scoring, usage profiling, duplicate detection
  Deskverse.Providers       Wallhaven, mock catalog, local library, aggregation
  Deskverse.WallpaperEngine static + video engines, WorkerW host, resource governor
  Deskverse.Application     use-case services that compose everything above
  Deskverse.Api             embedded Kestrel host, auth middleware, 40 endpoints
  Deskverse.App             WinUI 3 shell, view models, views, Windows imaging
tests/
  Deskverse.UnitTests  Deskverse.SecurityTests  Deskverse.IntegrationTests
```

Dependencies point one way: `App → Api → Application → {Providers, Intelligence, Storage,
WallpaperEngine, Infrastructure} → Security → Core`. `Core` references nothing.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the runtime design,
[docs/SECURITY.md](docs/SECURITY.md) for the threat model, and
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for conventions and the migration workflow.

---

## Tech stack

| Layer | Technology |
|---|---|
| UI | WinUI 3 / Windows App SDK 2.5.1, XAML, CommunityToolkit.Mvvm |
| Runtime | .NET 10, C# `latest`, nullable enabled, unpackaged self-contained |
| Data | SQLite via EF Core 10, migrations applied at startup |
| Imaging | Windows Imaging Component (dominant color, brightness, density, thumbnails) |
| Video | `Windows.Media.Playback.MediaPlayer` in a `WorkerW`-parented window |
| API | ASP.NET Core minimal API in a `CreateSlimBuilder` host |
| DI | `Microsoft.Extensions.DependencyInjection`, one composition root |
| Logging | Serilog, rolling daily files under `%LOCALAPPDATA%\DeskVerse\logs` |

---

## Provider keys

DeskVerse works out of the box: **Wallhaven needs no key** for SFW content, and the Local and
Mock providers are always available. Keys are only needed to raise rate limits or to add a
catalog that requires authentication.

Put them in `%LOCALAPPDATA%\DeskVerse\settings.json` (created on demand, never committed, read
at startup and merged over the defaults):

```json
{
  "Wallhaven": { "ApiKey": "", "Purity": "sfw", "PageSize": 24 },
  "Unsplash":  { "AccessKey": "" },
  "Giphy":     { "ApiKey": "" }
}
```

Environment variables override the file: `DESKVERSE_Wallhaven__ApiKey`,
`DESKVERSE_Unsplash__AccessKey`, and so on. `DESKVERSE_WALLHAVEN_APIKEY` is also honoured
directly. Only the sections a provider actually implements are read — the other blocks are
inert placeholders for when a provider is added.

| Source | Free tier | Key needed? | What it unlocks |
|---|---|---|---|
| Wallhaven.cc | yes | **no** (SFW) | Live search + trending, already wired and verified |
| Wallhaven API key | yes | optional | `sketchy`/`nsfw` purity, higher rate limit |
| Wikimedia Commons | yes | no, but a `User-Agent` is required | huge CC-licensed photo/art catalog |
| Unsplash | 50 req/hr demo | yes (`Access-Key`) | high-resolution photography |
| Pexels / Pixabay | yes | yes (free) | stock photography, no attribution |
| Giphy / Tenor | yes | yes (free) | animated GIF wallpapers — the one engine gap today |
| NASA APOD | DEMO_KEY ok | yes (free) | astronomy imagery |

---

## Data and privacy

Everything lives under `%LOCALAPPDATA%\DeskVerse`: the database, the wallpaper cache,
thumbnails, and logs. The API token is stored DPAPI-encrypted to your Windows user and is
never transmitted anywhere. The only outbound traffic is the provider you explicitly query,
and Wallhaven receives only your search text and category filter — never your library,
history, or taste profile.
