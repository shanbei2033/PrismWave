# PrismWave R503

## Highlights

- Added a new BETA / Experimental Features gate for PrismWave's online and DSD-related capabilities.
- Added a required risk acknowledgement dialog before enabling BETA features, with explicit language about third-party services, unofficial APIs, content rights, and user responsibility.
- Replaced the online search page's popular tag area with persistent local search history.
- Refined the online Home experience with schema 8 recommendations, stronger Top 100 diversity, improved artwork fallback behavior, and a cleaner Trending presentation.
- Updated the app and installer version to `R503`.

## BETA Feature Gate

- Added an Experimental Features setting:
  - Simplified Chinese: `实验性功能`
  - Traditional Chinese: `實驗性功能`
  - English: `BETA`
- When BETA is disabled, the online navigation entries and DSD output/status options are hidden.
- When the user tries to enable BETA, PrismWave now shows a serious legal and service-risk notice before the setting can be turned on.
- The dialog uses the existing PrismWave glass-style UI and provides two explicit choices: Disagree and Agree. The Agree action is highlighted in red.

## Online Search History

- Removed popular tags from the online search page.
- Added local search history below the search field when the query is empty.
- Search history is saved with `SharedPreferences` and persists across app restarts.
- History entries are normalized, deduplicated case-insensitively, ordered by recent use, and capped at 15 items.
- Pressing Enter commits the current search query to history.
- Playing a search result also commits the active query, so searches are remembered even when the user does not press Enter.
- Clicking a history item fills the search box and starts a search without creating a duplicate history entry.
- Each history item can be removed individually.

## Online Home And Trending

- Updated the bundled online Home fallback data to schema 8.
- Restored richer multi-style recommendation sections such as Pop, Rock, Electronic, Indie, Hip-Hop, R&B, Jazz, and Ambient.
- Improved daily Top 100 generation diversity so repeated lead artists are limited more aggressively.
- Refined the Home Trending card into a cleaner visual treatment with blurred multi-cover background artwork and a clear cover collage.
- Moved chart generation time formatting to the client so Simplified Chinese, Traditional Chinese, and English views consistently show the generated time in UTC.
- Improved artwork fallback behavior for users in mainland China by attempting NetEase artwork replacement for cover URLs that may be unreliable from that network environment.
- Added more detailed online cover and Home fallback logs for developer diagnostics.

## UI And Typography

- Updated the global font stack to Inter with Noto Sans SC / TC for Chinese text.
- Kept the glass-style PrismWave interface consistent across the new BETA notice and search history UI.
- Continued cleanup of online Home visual noise by keeping status details in deeper views instead of the main Home card.

## Validation

- `dart analyze` was run on the online search history changes with no issues found.
- `flutter build windows --release` was run successfully for the R503 build.
- The Windows installer was built with Inno Setup as `PrismWave-Setup-R503.exe`.
