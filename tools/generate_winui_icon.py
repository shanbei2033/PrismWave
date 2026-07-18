from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "PrismWave.WinUI" / "Assets"
SCALE = 4


def scaled(value: int) -> int:
    return value * SCALE


def draw_master() -> Image.Image:
    image = Image.new("RGBA", (scaled(1024), scaled(1024)), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle(
        (scaled(28), scaled(28), scaled(996), scaled(996)),
        radius=scaled(220),
        fill=(255, 255, 255, 255),
        outline=(228, 231, 233, 255),
        width=scaled(4),
    )

    ink = (30, 41, 47, 255)
    draw.ellipse(
        (scaled(128), scaled(494), scaled(164), scaled(530)),
        fill=ink,
    )
    bars = (
        (196, 442, 244, 582),
        (268, 392, 316, 632),
        (340, 332, 388, 692),
        (412, 272, 460, 752),
        (488, 202, 536, 822),
        (564, 272, 612, 752),
        (636, 332, 684, 692),
        (708, 392, 756, 632),
        (780, 442, 828, 582),
    )
    for left, top, right, bottom in bars:
        draw.rounded_rectangle(
            (scaled(left), scaled(top), scaled(right), scaled(bottom)),
            radius=scaled(24),
            fill=ink,
        )
    draw.ellipse(
        (scaled(860), scaled(494), scaled(896), scaled(530)),
        fill=ink,
    )

    return image.resize((1024, 1024), Image.Resampling.LANCZOS)


def contain_icon(master: Image.Image, width: int, height: int, icon_size: int) -> Image.Image:
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    icon = master.resize((icon_size, icon_size), Image.Resampling.LANCZOS)
    canvas.alpha_composite(icon, ((width - icon_size) // 2, (height - icon_size) // 2))
    return canvas


def main() -> None:
    master = draw_master()
    master.save(ASSETS / "AppIconMaster.png", optimize=True)
    master.save(
        ASSETS / "AppIcon.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    outputs = {
        "Square150x150Logo.scale-200.png": (300, 300, 300),
        "Square44x44Logo.scale-200.png": (88, 88, 88),
        "Square44x44Logo.targetsize-24_altform-unplated.png": (24, 24, 24),
        "Square44x44Logo.targetsize-48_altform-lightunplated.png": (48, 48, 48),
        "StoreLogo.png": (50, 50, 50),
        "LockScreenLogo.scale-200.png": (48, 48, 48),
        "Wide310x150Logo.scale-200.png": (620, 300, 240),
        "SplashScreen.scale-200.png": (1240, 600, 280),
    }
    for name, (width, height, icon_size) in outputs.items():
        contain_icon(master, width, height, icon_size).save(ASSETS / name, optimize=True)


if __name__ == "__main__":
    main()
