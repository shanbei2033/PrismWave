# Home Playlist Cover Consistency Design

## Goal

Keep the cover shown for an online song consistent across the playback bar and every Home online playlist entry representing the same song by the same artist.

## Scope

The synchronization applies to Home online catalog data:

- the top playlist;
- generated Home sections, including channel and genre playlists;
- derived Home collections that display those tracks;
- online album track lists loaded through Home.

Local library, favorites, search results, and user-managed local covers remain outside this synchronization. An online cover must not overwrite a local file's manually selected cover.

## Identity Rule

Tracks match only when both normalized title and normalized artist match. Normalization trims leading and trailing whitespace, collapses repeated whitespace, and compares case-insensitively.

Matching by title alone is forbidden. Provider, album, playlist position, and synthetic queue ID do not prevent a match when title and artist match.

## Source of Truth

The playback bar's resolved cover is authoritative. Home must use the same cover resolution path as `PlaybackViewModel.CurrentCoverPath`, including any resolved online cover or cover-service override.

When the playback service publishes a state change for a remote track, Home records the final display cover under the normalized title-and-artist key and applies it to all matching Home online tracks. A later cover change for the current track replaces the previous value for that key.

## Data Flow

1. Playback starts from a Home online playlist.
2. The provider resolver may replace the track's original cover with a resolved cover.
3. Playback state changes notify Home.
4. Home resolves the current display cover through the shared cover service.
5. Home stores the cover override by normalized title and artist.
6. Home rebuilds affected immutable playlist records and derived collections so existing XAML bindings receive property-change notifications.
7. A later Home refresh reapplies stored overrides before publishing refreshed playlist data.

## UI Behavior

- Every online Home row with the same normalized title and artist displays the playback bar cover.
- A same-title track by another artist keeps its original cover.
- Changing tracks does not restore a previously synchronized song to an older cover; its latest resolved cover remains consistent for the session.
- When no usable resolved display cover exists, Home does not erase an existing playlist cover.

## Implementation Boundaries

- Inject `ICoverService` into `HomeViewModel` so Home and the playback bar resolve the same display cover.
- Keep `HomeTrackModel` immutable.
- Store session cover overrides in `HomeViewModel` using a dedicated title-and-artist key.
- Rebuild only Home playlist model graphs and derived Home collections; do not mutate online-service cache documents or local library records.
- Subscribe to playback state and cover-change notifications. Ignore local tracks and empty cover paths.

## Error Handling

- Empty title, artist, or cover values do not create an override.
- A cover resolution error leaves existing playlist covers unchanged.
- Synchronization must not initiate network requests; it consumes cover information already available to playback and the cover service.

## Verification

Automated tests must prove:

1. matching title and artist entries across multiple sections receive the resolved playback cover;
2. same-title entries with a different artist remain unchanged;
3. a Home refresh reapplies the session override;
4. the selected playlist and its derived collections reference the synchronized cover;
5. local tracks do not create online Home cover overrides.

Manual verification must play `Mr. Brightside` from the Rock playlist and confirm that the first Rock row and the playback bar render the same cover after provider resolution.
