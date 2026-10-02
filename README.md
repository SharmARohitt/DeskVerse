# DeskVerse

A Windows desktop wallpaper manager built with WinUI 3 (.NET 10). It aims to be a smart, privacy-first hub for discovering, curating, and rotating wallpapers — with AI-powered recommendations and full multi-monitor support.

---

## Aim

DeskVerse wants to be the one app you need for desktop wallpapers:

- Browse and safely download wallpapers from open online catalogs.
- Import your own images and videos from local storage.
- Let an on-device recommendation engine learn your taste over time.
- Apply wallpapers per-monitor, rotate on a schedule, and manage a managed cache with configurable storage limits.
- Expose a local API so other tools and scripts can interact with the wallpaper engine.

---

## Current Capabilities (what actually works today)

| Area | Status |
|---|---|
| **Library** | Import local images/videos, search, sort, filter by kind/favorites/pinned/cached, paginate, delete |
| **Wallpaper application** | Apply via both Win32 `SystemParametersInfo` and the `IDesktopWallpaper` COM API; per-monitor support scaffolded |
| **Video wallpapers** | Video rendered into the desktop `WorkerW` window via a WinUI `MediaPlayerElement` host |
| **Engine controls** | Apply, Pause, Resume, Stop, Restore previous — wired end-to-end in the UI |
| **Discover page** | Provider aggregation with search and trending; a `MockWallpaperProvider` is included; no real online provider yet |
| **Studio page** | Select a wallpaper, edit metadata (title, description, categories), trigger visual re-analysis, choose display target |
| **Recommendation engine** | Scores wallpapers by preference match, usage history, and visual features; explainable factors surfaced in the UI |
| **Duplicate detection** | Exact-hash and visual-similarity duplicate finder |
| **Cache / storage management** | Configurable cache path, size limit, health reporting, LRU-style cleanup policy |
| **Security layer** | File-signature validation, MIME/extension checks, URL allowlist policy, safe network download service |
| **Local REST API** | Embedded ASP.NET Core host with token authentication; endpoint scaffolding present |
| **Settings** | Cache path, storage limit, rotation interval, placement mode, per-display preferences |
| **Persistence** | SQLite via EF Core; initial migration present |
| **Notifications** | In-app toast/snackbar notification service |

---

## What Is Partial or Missing

- **Real online provider** — only a mock provider exists; no live catalog (Unsplash, Wallhaven, etc.) is connected.
- **Collections view** — `CollectionsViewModel` and `CollectionsService` are written but no `CollectionsView.xaml` exists in the Views folder.
- **Wallpaper rotation / scheduler** — settings fields for rotation interval are present but no background scheduler is wired up.
- **Local API endpoints** — `ApiEndpoints.cs` is scaffolded; actual route handlers need filling out.
- **First-run / onboarding flow** — `IsFirstRunComplete` is tracked in the preferences store but no onboarding UI exists.
- **Per-monitor independent wallpapers** — the COM API supports it and display selection is in Studio, but full per-monitor rotation is not implemented.
- **Animated/GIF wallpapers** — `WallpaperKind` includes `AnimatedGif` but no playback path is implemented.
- **Tests** — no test projects are present.

---

## Tech Stack

| Layer | Technology |
|---|---|
| UI framework | WinUI 3 (Windows App SDK), XAML |
| Language / runtime | C# 13, .NET 10 |
| MVVM | CommunityToolkit.Mvvm |
| Database | SQLite + Entity Framework Core |
| Imaging | Windows Imaging Component (WIC) via P/Invoke |
| Video playback | `Windows.Media.Playback.MediaPlayer` embedded in desktop `WorkerW` |
| Local API | ASP.NET Core (minimal API, embedded host) |
| DI | `Microsoft.Extensions.DependencyInjection` |
| Architecture | Clean-ish layered: Core → Infrastructure / WallpaperEngine / Storage / Security / Intelligence / Providers → Application → App |

---

## Current Status

> **~60 % complete — solid foundation, key integrations still missing.**

The architecture is well-structured and the core loop (import → library → apply → recommend) is functional end-to-end. The main gaps are the live online provider, wallpaper rotation scheduling, the Collections view, and API endpoint handlers. The app can be run and used for basic wallpaper management today, but the "smart" and "discover" features are limited to mock/local data.
