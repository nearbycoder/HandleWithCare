"""Caption, title and end cards for the trailer, drawn with ImageMagick in the game's own style:
cream order-card panels, postal-red tags and ink lettering in Fira Sans (the game's UI fonts)."""
import os
import subprocess

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONTS = os.path.join(ROOT, "Assets", "Resources", "Fonts")

CREAM = "#F3E9D2"
PAPER = "#FBF6EA"
RED = "#D9483B"
RED_DARK = "#A8322A"
INK = "#1F2A44"
INK_SOFT = "#3B4766"
TEAL = "#2F8F8B"
STICKY = "#F6D55C"

HEAVY = os.path.join(FONTS, "FiraSansCompressed-Heavy.ttf")
XBOLD = os.path.join(FONTS, "FiraSansCompressed-ExtraBold.ttf")
BOLD = os.path.join(FONTS, "FiraSans-Bold.ttf")
ITALIC = os.path.join(FONTS, "FiraSans-MediumItalic.ttf")
SEMI = os.path.join(FONTS, "FiraSans-SemiBold.ttf")


def magick(*args):
    subprocess.run(["magick", *map(str, args)], check=True)


def size(path):
    out = subprocess.run(["magick", "identify", "-format", "%w %h", path], check=True, capture_output=True, text=True).stdout
    w, h = out.split()
    return int(w), int(h)


def text(path, s, font, pt, color, kerning=0):
    """Tight text image. Trimmed vertically to the font box, not the ink, so baselines line up."""
    magick("-background", "none", "-fill", color, "-font", font, "-pointsize", pt, "-kerning", kerning,
           f"label:{s}", path)
    return size(path)


def caption(out, kicker, headline, sub, tmp, tilt=-1.0):
    """Order-card caption: red tag, big ink headline, italic line. Returns (w, h) of the PNG."""
    os.makedirs(tmp, exist_ok=True)
    k, h, s = (os.path.join(tmp, n) for n in ("k.png", "h.png", "s.png"))
    kw, kh = text(k, kicker, XBOLD, 34, CREAM, 1)
    hw, hh = text(h, headline, HEAVY, 88, INK, 0.5)
    sw, sh = (0, 0)
    if sub:
        sw, sh = text(s, sub, ITALIC, 35, INK_SOFT)
    pad_x, pad_top, pad_bot = 44, 30, 34
    tag_w, tag_h = kw + 30, kh + 6
    w = max(tag_w, hw, sw) + pad_x * 2
    y_tag = pad_top
    y_head = y_tag + tag_h - 4
    y_sub = y_head + hh - 12
    hgt = (y_sub + sh if sub else y_head + hh) + pad_bot - (6 if sub else 14)
    card = os.path.join(tmp, "card.png")
    magick("-size", f"{w}x{hgt}", "xc:none",
           "-fill", CREAM, "-draw", f"roundrectangle 0,0 {w - 1},{hgt - 1} 22,22",
           "-fill", RED, "-draw", f"roundrectangle {pad_x - 2},{y_tag} {pad_x - 2 + tag_w},{y_tag + tag_h} 10,10",
           k, "-geometry", f"+{pad_x + 13}+{y_tag + 3}", "-composite",
           h, "-geometry", f"+{pad_x}+{y_head}", "-composite",
           *([s, "-geometry", f"+{pad_x + 2}+{y_sub}", "-composite"] if sub else []),
           card)
    shadowed(card, out, tilt)
    return size(out)


def shadowed(src, out, tilt=0.0, opacity=45, blur=12, dx=8, dy=12):
    magick(src, "-background", "none", "-rotate", tilt,
           "(", "+clone", "-background", "black", "-shadow", f"{opacity}x{blur}+{dx}+{dy}", ")",
           "+swap", "-background", "none", "-layers", "merge", "+repage", out)


def logo(out, tmp, scale=1.0, tilt=-3.0):
    """The title-screen sign: a tilted postal-red card with HANDLE / WITH CARE in cream, and the
    Mossbury Parcel Post strap line under it."""
    os.makedirs(tmp, exist_ok=True)
    a, b, t = (os.path.join(tmp, n) for n in ("l1.png", "l2.png", "lt.png"))
    aw, ah = text(a, "HANDLE", HEAVY, int(190 * scale), CREAM, 8 * scale)
    bw, bh = text(b, "WITH CARE", HEAVY, int(150 * scale), CREAM, 6 * scale)
    w = int(max(aw, bw) + 150 * scale)
    hgt = int(ah + bh + 30 * scale)
    sign = os.path.join(tmp, "sign.png")
    magick("-size", f"{w}x{hgt}", "xc:none",
           "-fill", RED_DARK, "-draw", f"roundrectangle 0,{int(8 * scale)} {w - 1},{hgt - 1} {int(30 * scale)},{int(30 * scale)}",
           "-fill", RED, "-draw", f"roundrectangle 0,0 {w - 1},{hgt - 1 - int(8 * scale)} {int(30 * scale)},{int(30 * scale)}",
           a, "-geometry", f"+{(w - aw) // 2}+{int(4 * scale)}", "-composite",
           b, "-geometry", f"+{(w - bw) // 2}+{int(ah - 18 * scale)}", "-composite",
           sign)
    tw, th = text(t, "MOSSBURY PARCEL POST  ·  WE SHIP ANYTHING. CAREFULLY.", XBOLD, int(40 * scale), INK, 1)
    strip_w, strip_h = int(tw + 70 * scale), int(th + 26 * scale)
    strip = os.path.join(tmp, "strip.png")
    magick("-size", f"{strip_w}x{strip_h}", "xc:none", "-fill", PAPER,
           "-draw", f"roundrectangle 0,0 {strip_w - 1},{strip_h - 1} {int(12 * scale)},{int(12 * scale)}",
           t, "-geometry", f"+{int(35 * scale)}+{int(13 * scale)}", "-composite", strip)
    full_w = max(w, strip_w)
    full_h = hgt + int(26 * scale) + strip_h
    both = os.path.join(tmp, "both.png")
    magick("-size", f"{full_w}x{full_h}", "xc:none",
           sign, "-geometry", f"+{(full_w - w) // 2}+0", "-composite",
           strip, "-geometry", f"+{(full_w - strip_w) // 2}+{hgt + int(26 * scale)}", "-composite", both)
    shadowed(both, out, tilt, opacity=50, blur=18, dx=10, dy=16)
    return size(out)


def pill(out, s, tmp, fg=INK, bg=PAPER, font=XBOLD, pt=46, pad=(34, 14), radius=14, tilt=0.0):
    os.makedirs(tmp, exist_ok=True)
    t = os.path.join(tmp, "p.png")
    tw, th = text(t, s, font, pt, fg, 1)
    w, h = tw + pad[0] * 2, th + pad[1] * 2
    raw = os.path.join(tmp, "pill.png")
    magick("-size", f"{w}x{h}", "xc:none", "-fill", bg, "-draw", f"roundrectangle 0,0 {w - 1},{h - 1} {radius},{radius}",
           t, "-geometry", f"+{pad[0]}+{pad[1]}", "-composite", raw)
    shadowed(raw, out, tilt, opacity=40, blur=10, dx=6, dy=9)
    return size(out)


def word(out, s, tmp, color=STICKY, pt=150, stroke=INK, stroke_w=10, tilt=-4.0):
    """Big outlined word like the game's in-journey captions (ACHOO!, SMASH!)."""
    os.makedirs(tmp, exist_ok=True)
    raw = os.path.join(tmp, "w.png")
    magick("-background", "none", "-font", HEAVY, "-pointsize", pt, "-kerning", 2,
           "-stroke", stroke, "-strokewidth", stroke_w, "-fill", color, f"label:{s}",
           "-stroke", "none", "-fill", color, "-font", HEAVY, "-pointsize", pt, "-kerning", 2,
           "-gravity", "center", "-annotate", "+0+0", s, raw)
    shadowed(raw, out, tilt, opacity=45, blur=10, dx=6, dy=10)
    return size(out)
