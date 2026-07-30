# PrismWave Usage Guide

[中文使用指南](./usage-guide.zh.md)

PrismWave is a native Windows 11 music player built with WinUI 3 and .NET 10, featuring local library management, online search and playback, word-by-word lyrics, and HITS radio.

---

## Table of Contents

- [Getting Started](#getting-started)
- [Playback Controls](#playback-controls)
- [Replacing Cover Art](#replacing-cover-art)
- [Lyrics](#lyrics)
- [Playback Queue](#playback-queue)
- [Context Menus](#context-menus)
- [Favorites](#favorites)
- [Local Library](#local-library)
- [Online Features](#online-features)
- [HITS Radio](#hits-radio)
- [Settings](#settings)
- [Developer Mode](#developer-mode)

---

## Getting Started

### Adding a Music Folder

1. Go to **Settings > Basic**
2. Under "Music Folders", click **Add Folder**
3. Select a directory containing your music files and confirm
4. The app will automatically scan; once finished, tracks appear in "Library"

A folder may contain subdirectories at any depth — the scanner recurses into all supported audio formats (MP3, FLAC, WAV, OGG, M4A, DSF, DFF, etc.).

### Rescan

When your local music changes (files added, removed, or moved), click **Rescan** to refresh the library. A progress ring appears during scanning, and the track count is shown when complete.

### Enabling Online Features

Online features are off by default. To enable:

1. Go to **Settings > Online**
2. First toggle on **BETA / Experimental features**
3. Then toggle on **Online mode**

Once enabled, you can use online search, online home recommendations, and automatic lyrics matching.

---

## Playback Controls

### Bottom Player Bar

The bottom player bar is always visible and includes:

- Current track cover, title, and artist
- Play/pause button
- Previous / Next
- Playback mode toggle (List loop → Single repeat → Shuffle)
- Volume slider
- Progress bar (drag to seek)
- Favorite button (heart icon)
- Queue button

### FullPlay Page

Click the cover area in the bottom player bar to expand into the FullPlay page. The FullPlay page has two columns:

- **Left column**: large cover art, track info, playback controls, progress bar, volume, queue
- **Right column**: lyrics stage (word-by-word or line-by-line scrolling lyrics)

Press **Esc** or click the back button in the top-left to exit FullPlay.

### Playback Modes

Click the mode button to cycle through three modes:

| Icon | Mode | Description |
|------|------|-------------|
| List | List loop | Restarts from the beginning after the list ends |
| Single arrow | Single repeat | Loops the current track |
| Shuffle | Shuffle | Picks the next track randomly |

---

## Replacing Cover Art

If a track's cover art is incorrect or missing, you can manually search and replace it.

### How to Replace

1. Enter the FullPlay page (click the cover in the bottom player bar)
2. **Double-click the large cover** on the left — a cover search dialog appears
3. The dialog automatically searches using the current track's title and artist
4. You can also type custom keywords (album name, artist name) and press **Enter** to search again
5. Browse the results and **click the desired cover** to apply it

### Search Sources

Cover search queries three sources simultaneously, merges and deduplicates results ranked by relevance:

- **Apple Music** — high-quality official artwork
- **Deezer** — global music database
- **MusicBrainz** — open-source music metadata

### Notes

- Replaced covers are persisted — the custom cover displays automatically the next time you play the same track
- Custom covers are associated by track identity (title + artist), so different files of the same track share the same cover
- After replacing, the cover updates everywhere: player bar, queue, album detail, etc.

---

## Lyrics

### Switching Lyrics Source

In the bottom-right corner of the FullPlay page there is a circular tool button. Click it to expand the lyrics toolbar with three buttons:

| Button | Function |
|--------|----------|
| Source toggle (Local/Online) | Switch between local and online lyrics |
| Search online lyrics | Open the lyrics search dialog |
| Adjust lyrics offset | Fine-tune lyrics timing |

Click the **Source toggle** button (labeled "Local" or "Online") to switch between local and online lyrics. If the selected source has no available lyrics, it automatically falls back to the other source.

### Online Lyrics Search & Replacement

1. Click the **Search online lyrics** button in the lyrics toolbar
2. A search dialog appears and automatically searches using the current track info
3. Type custom keywords and press **Enter** to search again
4. **Click a search result** to apply it as the current track's lyrics

Lyrics search prioritizes NetEase **YRC word-by-word lyrics** (with per-character highlighting), then falls back to standard line-by-line lyrics. The lyrics stage updates immediately after applying.

### Lyrics Offset Adjustment

When lyrics are out of sync with the music, you can adjust the offset:

1. Click the **Adjust lyrics offset** button in the lyrics toolbar
2. Enter the offset in seconds in the input box
   - **Positive** (e.g. `+0.5`): lyrics appear later
   - **Negative** (e.g. `-1.0`): lyrics appear earlier
3. Press **Enter** or click **Apply**

The offset is saved per-track and automatically applied the next time you play the same track.

### Click-to-Seek on Lyrics

**Click any line of lyrics** on the lyrics stage to jump playback to that line's timestamp.

---

## Playback Queue

### Opening the Queue

- **Bottom player bar**: click the queue icon button
- **FullPlay page**: click the queue icon in the playback controls area

### Operations

| Action | Method |
|--------|--------|
| Play a track in the queue | Click the track |
| Reorder | Press and drag to the desired position |
| Remove a single track | Right-click → "Remove from queue" |
| Clear the entire queue | Click the trash button at the bottom of the queue |

The currently playing track is highlighted with a background tint and a play icon in the queue.

---

## Context Menus

In the Library, Album Detail, Artist Detail, and Favorites pages, **right-click any track** to open a context menu:

| Menu item | Description |
|-----------|-------------|
| Play now | Stops current playback and plays this track immediately |
| Add to queue | Appends the track to the end of the queue |
| Play next | Inserts the track right after the currently playing track |
| Favorite | Adds to "Favorites" (click again to unfavorite) |
| View artist | Navigates to this artist's detail page |

The Library page has additional menu items:

| Menu item | Description |
|-----------|-------------|
| Track details | View the track's metadata information |
| Open file location | Locates the file in File Explorer |
| Remove from library | Removes from the library (does not delete the source file) |

---

## Favorites

### Favoriting a Track

- **In a list**: right-click the track → "Favorite"
- **In FullPlay**: click the heart button
- **In search results or Home**: right-click → "Favorite"
- Online songs can also be favorited — they are automatically added to the local library

### Managing Favorites

Go to the **"Favorites"** page in the navigation bar:

- View all favorited tracks
- **Drag to reorder** your custom sort order
- Right-click a track to play now, add to queue, or play next
- Click the favorite button again to unfavorite

---

## Local Library

### View Switching

The navigation bar offers three ways to browse local music:

- **Library**: list view of all tracks, supports drag-to-reorder
- **Albums**: grid view grouped by album, click to enter album detail
- **Artists**: grouped by artist, click to enter artist detail

### Album Detail Page

Click any album to enter its detail page:

- Top section shows large cover, album name, and artist
- Below is the track list
- Double-click a track or right-click "Play now" to start playback
- Cover area is enlarged with top-aligned display for a more complete view

### Folder Management

Besides the Settings page, you can also quickly open the folder management dialog from the **Library** page.

---

## Online Features

### Online Search

Type keywords in the top search bar. Results include both local tracks and online songs. Online songs can be:

- Played directly (audio source is resolved automatically)
- Right-click "Add to library"
- Right-click "Favorite"

Search history is saved — right-click a history entry to delete it.

### Online Home

When online mode is enabled, the Home page displays:

- **Today's Trending**: TOP100 trending banner
- **Recommended tracks**: refreshed daily
- **New albums**: latest releases
- **Hot songs**: currently popular tracks

Click the refresh button in the top-right of the Home page to manually refresh recommendations.

### Account Login

In **Settings > Online**, you can log in to music platforms for higher quality playback:

- **NetEase Cloud Music**: click "Scan login" and scan the QR code with the mobile app
- **QQ Music**: click "Scan login" and scan the QR code with the mobile app

After login, your nickname and avatar are displayed, and lossless quality options are unlocked. Click "Sign out" to log out.

### Online Cache

Audio from online playback is cached locally to save bandwidth:

- In **Settings > Online**, set the cache limit (0.5 GB ~ 1024 GB)
- Change the cache directory location
- Manually clear the cache (only PrismWave-owned cache files are removed)

---

## HITS Radio

HITS is a schedule-based online radio mode. Schedules are generated daily by the separate [prismwave-hits](https://github.com/shanbei2033/prismwave-hits) repository.

### How to Use

1. Make sure online mode is enabled
2. Click **HITS** (radio icon) in the navigation bar
3. Enter the immersive HITS playback view:
   - Extra-large cover art centered on screen
   - Background uses a blurred cover effect
   - Click the cover to play/pause
4. HITS plays automatically according to the schedule, with seamless transitions between tracks

### Notes

- HITS mode forces WASAPI Shared output
- HITS includes 10 audio providers (including bilibili and YouTube as fallbacks)
- The schedule updates daily at 10:00 AM Beijing time

---

## Settings

### Basic

| Setting | Description |
|---------|-------------|
| Music folders | Manage local scan directories |
| Language | Switch interface language |
| Appearance style | **Dark** (classic solid) / **Light (Beta)** (Windows 11 Mica) |
| Version check | Manually check for updates, or enable auto-check |
| Project URL | GitHub repository link |

### Online

| Setting | Description |
|---------|-------------|
| Experimental features | Master switch — must be enabled before online features |
| Online mode | Enables online search, home recommendations, and online playback |
| Streaming quality | Select online playback quality (requires login to the corresponding account) |
| Online cache | Set cache limit, directory, and clear cache |
| Account management | NetEase / QQ Music scan-to-login and sign out |

### Playback

| Setting | Description |
|---------|-------------|
| Output mode | Compatibility (MPV auto) / WASAPI Shared / WASAPI Exclusive |
| Output device | Select audio output device |
| Fade in/out | Enable volume fade between track switches |
| Fade duration | Adjustable from 0 to 2000 ms |

**Output mode details**:

- **Compatibility**: uses mpv's default audio output — best compatibility
- **WASAPI Shared**: Windows Audio Session API shared mode — low latency
- **WASAPI Exclusive**: exclusive audio device access — highest quality, but other apps cannot play audio simultaneously

If the selected mode fails to initialize, the app automatically falls back to an available mode, and the Settings page shows the fallback reason.

---

## Developer Mode

In the **Settings > Developer** tab:

| Feature | Description |
|---------|-------------|
| Developer logs | Real-time display of playback engine, online resolver, lyrics, and other internal logs |
| Open | Launches a PowerShell window that tails the latest log output in real time (`Get-Content -Wait`) |
| Clear | Clears the current log buffer |
| Log path | Shows the full path to the log file |

Developer logs are useful for diagnosing playback issues, online resolution failures, lyrics matching anomalies, and more.
