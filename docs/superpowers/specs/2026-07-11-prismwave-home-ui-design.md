# PrismWave WinUI 3 Home UI Design

## Direction

The Flutter home screen is the visual source of truth. The WinUI shell uses a broad, independent navigation surface, a single-column music feed, an immersive trending banner, horizontal media cards, and a detached bottom player. The previous dashboard composition, top search strip, compact rail, and Private Radar column are removed.

## Structure

- `Sidebar`: expanded by default at 288 px and collapsible to 76 px. It owns primary navigation, live library counts, and Settings.
- `HomePage`: page heading, refresh action, one full-width `TrendingBanner`, then horizontally scrolling recommendation sections.
- `TrendingBanner`: uses recommendation artwork as a subdued backdrop and a four-cover collage. Copy is limited to a small category label, the Chinese title, one subtitle, and playback/detail actions.
- `SongCard`: fixed-format cover, title, and artist card sized for a five-card desktop row.
- `BottomPlayerBar`: detached rounded surface with track identity, centered transport and seek controls, and volume at the right.

## Behavior

Existing Shell navigation, home refresh, online track playback, queue, seek, volume, and full-player commands remain connected. Sidebar selection follows nested Home routes. At narrower window widths the sidebar can collapse manually, the banner collage hides, and secondary player controls reduce without overlapping the required content.

## Visual Tokens

The palette is neutral charcoal with translucent gray surfaces. Blue is reserved for selected and primary playback actions. Large shell surfaces use moderate radii, while repeated song cards and buttons stay tighter. There are no decorative side columns, neon glows, or large synthetic gradients.
