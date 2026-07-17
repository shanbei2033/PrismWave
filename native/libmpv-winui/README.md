# WinUI libmpv binary

`libmpv-2.dll` is the x86-64 LGPL development build from:

- Project: `zhongfly/mpv-winbuild`
- Release: `2026-07-15-94335ab87a`
- Asset: `mpv-dev-lgpl-x86_64-20260715-git-94335ab87a.7z`
- Release URL: https://github.com/zhongfly/mpv-winbuild/releases/tag/2026-07-15-94335ab87a

Checksums:

- Release archive SHA-256: `991bb562f60448a54e3068f1767300765e9fad836e11fe76b5391eabb6760af8`
- Extracted `libmpv-2.dll` SHA-256: `1da33af5ce066e4df3ad6eb71bd9cdf6f32e49449286aeddb6f86faae438bcad`

The WinUI application uses this full decoder build because the smaller legacy
binary in `native/libmpv/` cannot decode E-AC-3 audio contained in local M4A
files. The legacy binary remains unchanged for the Flutter build because it
contains the existing WASAPI exclusive-buffer patch.

The bundled binary is below GitHub's 100 MiB per-file limit. Before replacing
it, verify the upstream checksum and run `BundledLibMpvCodecTests`.
