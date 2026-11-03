import sys
# Regenerates app.ico:  python tools/make_icon.py app.ico preview.png
from PIL import Image, ImageDraw

out_ico, out_png = sys.argv[1], sys.argv[2]

def render(size):
    ss = 8  # supersample for smooth edges
    S = size * ss
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = S * 0.04
    d.rounded_rectangle([pad, pad, S - pad, S - pad], radius=S * 0.22, fill=(14, 17, 22, 255),
                        outline=(44, 50, 58, 255), width=max(1, int(S * 0.025)))
    # three rising bars: red, green, blue (same hues as the overlay's labels)
    colors = [(255, 92, 92), (74, 222, 128), (77, 163, 255)]
    heights = [0.30, 0.48, 0.66]
    left, right = S * 0.20, S * 0.80
    bottom = S * 0.80
    gap = S * 0.07
    bw = (right - left - 2 * gap) / 3
    for i, (c, h) in enumerate(zip(colors, heights)):
        x0 = left + i * (bw + gap)
        d.rounded_rectangle([x0, bottom - S * h, x0 + bw, bottom], radius=bw * 0.28, fill=c + (255,))
    return img.resize((size, size), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
imgs = [render(s) for s in sizes]
imgs[-1].save(out_ico, format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
# preview strip
strip = Image.new("RGBA", (sum(sizes[3:]) + 20 * len(sizes[3:]), 276), (240, 240, 240, 255))
x = 10
for s, im in zip(sizes[3:], imgs[3:]):
    strip.paste(im, (x, 266 - s), im); x += s + 20
strip.save(out_png)
