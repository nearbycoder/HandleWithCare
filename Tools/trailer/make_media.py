#!/usr/bin/env python3
"""README media from the captured clips (Tools/trailer/capture.sh):

    docs/media/teaser.webp          ~7 s seamless loop for the top of the README
    docs/media/trailer-poster.jpg   clickable poster for the trailer (play button over a key frame)
"""
import argparse
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cards  # noqa: E402

ROOT = cards.ROOT
CLIPS = os.path.join(ROOT, "Recordings", "clips")   # --clips DIR to use another capture
WORK = os.path.join(ROOT, "Recordings", "media_build")
MEDIA = os.path.join(ROOT, "docs", "media")
FPS = 30

# Ember is packed beside the vase, the box is taped, he sneezes at the vase in the depot,
# the vase comes out PERFECT, and Ember sneezes at the camera; then it loops.
TEASER = [
    ("pack18", 15.30, 1.05),
    ("cold18", 3.55, 1.10),
    ("cold18", 19.20, 1.75),
    ("cold18", 40.05, 1.25),
    ("cold18", 43.75, 2.00),
]


def run(cmd):
    print("+", " ".join(map(str, cmd[:10])), "...")
    subprocess.run(["nice", "-n", "10", *map(str, cmd)], check=True)


def teaser(out, width=960, fps=15, quality=72):
    inputs, chains = [], []
    for i, (clip, t_in, dur) in enumerate(TEASER):
        inputs += ["-framerate", FPS, "-start_number", int(round(t_in * FPS)), "-t", f"{dur + 0.1:.2f}",
                   "-i", os.path.join(CLIPS, clip, "%05d.jpg")]
        chains.append(f"[{i}:v]trim=end_frame={int(round(dur * FPS))},setpts=PTS-STARTPTS,"
                      f"fps={fps},scale={width}:-2:flags=lanczos,format=yuv420p,setsar=1[v{i}]")
    chains.append("".join(f"[v{i}]" for i in range(len(TEASER))) + f"concat=n={len(TEASER)}:v=1:a=0[out]")
    run(["ffmpeg", "-y", "-v", "error", *inputs, "-filter_complex", ";".join(chains), "-map", "[out]",
         "-c:v", "libwebp_anim", "-lossless", 0, "-quality", quality, "-compression_level", 6, "-loop", 0,
         "-preset", "picture", out])


def poster(out, minutes_seconds):
    os.makedirs(WORK, exist_ok=True)
    tmp = os.path.join(WORK, "cardtmp")
    base = os.path.join(CLIPS, "cold18", f"{int(round(20.45 * FPS)):05d}.jpg")
    logo = os.path.join(WORK, "poster_logo.png")
    cards.logo(logo, tmp, scale=0.62)
    pill = os.path.join(WORK, "poster_pill.png")
    cards.pill(pill, f"WATCH THE TRAILER  ·  {minutes_seconds}", tmp, fg=cards.CREAM, bg=cards.INK, pt=50, pad=(40, 18))
    # play button: cream disc with a postal-red triangle and a soft shadow
    button = os.path.join(WORK, "poster_button.png")
    cards.magick("-size", "300x300", "xc:none", "-fill", cards.PAPER, "-draw", "circle 150,150 150,12",
                 "-fill", cards.RED, "-draw", "polygon 118,88 118,212 222,150", os.path.join(WORK, "btn_raw.png"))
    cards.shadowed(os.path.join(WORK, "btn_raw.png"), button, 0, opacity=55, blur=14, dx=0, dy=10)
    lw, lh = cards.size(logo)
    bw, bh = cards.size(button)
    pw, ph = cards.size(pill)
    cards.magick(base,
                 logo, "-geometry", "+40+30", "-composite",
                 button, "-geometry", f"+{60 + lw // 2 - bw // 2}+{lh + 90}", "-composite",
                 pill, "-geometry", f"+{60 + lw // 2 - pw // 2}+{lh + 90 + bh + 10}", "-composite",
                 "-quality", 90, out)


def main():
    global CLIPS
    ap = argparse.ArgumentParser()
    ap.add_argument("--clips", default=CLIPS)
    CLIPS = ap.parse_args().clips
    os.makedirs(MEDIA, exist_ok=True)
    teaser(os.path.join(MEDIA, "teaser.webp"))
    dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0",
                                os.path.join(MEDIA, "trailer.mp4")], capture_output=True, text=True).stdout or 0)
    poster(os.path.join(MEDIA, "trailer-poster.jpg"), f"{int(dur // 60)}:{int(round(dur % 60)):02d}")
    for f in ("teaser.webp", "trailer-poster.jpg"):
        p = os.path.join(MEDIA, f)
        print(f"{f}: {os.path.getsize(p) / 1e6:.2f} MB")


if __name__ == "__main__":
    main()
