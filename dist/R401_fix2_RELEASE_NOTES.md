# PrismWave R401_fix2

Pre-release build for the online-first PrismWave experience.

## Highlights

- The app now opens directly to the Home page.
- Online mode is enabled by default for first-time users.
- Online Home remains the main entry point for recommendations, new albums, hot songs, and unified search.
- Online playback queues now resolve placeholder tracks on demand when the user clicks them or when auto-play reaches them.

## Fixes

- Fixed a queue edge case where an unresolved online track could show as paused and could not be started with the play button.
- Added playback-failure invalidation for online queue sources, so a failed resolved URL is cleared and retried through the resolver.
- Improved NetEase stream validation: PrismWave now checks response type and initial bytes instead of trusting HTTP status alone, preventing HTML or empty responses from being cached as playable audio.
- Added developer logs for online queue recovery and resolver validation, including `queue.resolve-on-demand.*`, `queue.playback-failure.*`, `resolver.cache.invalidate`, and `resolver.netease.stream.*`.

## Notes

- This is a pre-release because the online provider set is still being tuned against real-world availability.
- If an online source fails, enable Developer Mode and check the playback log for the queue and resolver events listed above.
