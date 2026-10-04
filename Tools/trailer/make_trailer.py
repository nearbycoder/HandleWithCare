#!/usr/bin/env python3
"""Cuts the Steam-style feature trailer from the clips captured by the game's trailer mode.

    Tools/trailer/capture.sh                     # 1. play the shot list, ~6 min: Recordings/clips/<clip>/
    python3 Tools/trailer/make_trailer.py        # 2. cards, audio mix, picture, encode -> docs/media/trailer.mp4

The edit is the EDL below: segments of captured clips (in/out in clip seconds), cut on the 128 bpm
grid of the game's journey theme, with caption cards in the game's order-card style. Audio is the
game's own mix captured with each clip (sound effects only; in-game music is muted while capturing)
over a music bed built from the game's music, ducked under the effects and loudness-normalised.
Needs ffmpeg and ImageMagick.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cards  # noqa: E402

ROOT = cards.ROOT
FPS = 30
BEAT = 60.0 / 128.0          # journey.wav tempo
BAR = BEAT * 4
W, H = 1920, 1080
COLOR = ["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv"]
MUSIC = os.path.join(ROOT, "Assets", "Resources", "Audio", "Music")
SFX = os.path.join(ROOT, "Assets", "Resources", "Audio", "Sfx")


def seg(clip, t_in, dur, cap=None, trans="cut", td=0.0, gain=0.0, speed=1.0, zoom=None):
    return dict(clip=clip, t_in=t_in, dur=dur, cap=cap, trans=trans, td=td, gain=gain, speed=speed, zoom=zoom)


def C(kicker, headline, sub=None, span=1, pos="left", delay=0.2):
    return dict(kind="caption", kicker=kicker, headline=headline, sub=sub, span=span, pos=pos, delay=delay)


def P(text, span=1, delay=0.15, pos="low"):
    return dict(kind="pill", text=text, span=span, delay=delay, pos=pos)


# ----------------------------------------------------------------------------------------- edit
# Cold open (free timing), then everything is measured in beats from the title hit.
COLD = [
    seg("cold18", 1.85, 1.75, P("ONE PRICELESS VASE.", delay=0.1)),
    seg("cold18", 17.85, 3.05, P("ONE TINY DRAGON WITH A COLD.", delay=0.25)),
    seg("cold18", 40.05, 1.45),
    seg("cold18", 43.70, 2.00),
]

b = BEAT
MAIN = [
    # title card over the bench (built separately), then the loop
    seg("titleplain", 1.0, 8 * b, dict(kind="title"), trans="fadewhite", td=0.12, zoom=0.06),
    seg("loop3", 0.30, 5 * b, C("01  ·  PACK", "PACK THE ORDER", "19 kinds of item, each with a quirk on its card.", span=2), trans="fadewhite", td=0.2),
    seg("pack18", 14.30, 4 * b),
    seg("loop3", 2.45, 4 * b, C("02  ·  PAD", "PAINT IN PADDING", "Crumpled paper, bubble wrap and foam soak up the jolts.", span=2)),
    seg("pack18", 2.75, 4 * b),
    seg("depot10", 0.0, 4 * b, C("03  ·  BUILD", "DIVIDERS, SHELVES & STRAPS", "Wall things off, take the weight off the cake, strap the magnets down.", span=2)),
    seg("shelf8", 0.05, 4 * b),
    seg("loop3", 6.95, 6 * b, C("04  ·  SEAL", "SEAL & SHIP", "One long, satisfying screech of tape.")),
    seg("loop3", 9.62, 8 * b, C("05  ·  THE TRIP", "WATCH THE TRIP", "Deterministic physics: the same packing always makes the same journey.")),
    seg("loop3", 15.05, 5 * b, C("06  ·  UNBOX", "THE UNBOXING", "Every item gets a stamp. Earn up to three stars a delivery.", span=2)),
    seg("loop3", 20.35, 3 * b),
    seg("fail3", 7.70, 6 * b, C("07  ·  BREAK", "EVERY ITEM HAS A JOLT LIMIT", "Tall things topple. Fragile things shatter.", span=2)),
    seg("fail3", 13.95, 3 * b),
    seg("fail3", 25.45, 5 * b, C("08  ·  LEARN", "REPLAY, THEN REPACK", "Close-up replays and last-trip trails show what went wrong.", span=2)),
    seg("fail3", 30.30, 4 * b, zoom=0.07),
    # quirks, 6 beats each
    seg("mag6", 3.55, 6 * b, C("QUIRK", "MAGNETS ATTRACT", "From four cells away. Once they touch, they're stuck."), trans="smoothleft", td=0.25),
    seg("sleep5", 9.95, 6 * b, C("QUIRK", "SLEEPERS WAKE UP", "Snoozles the armadillo does not like a hard brake.")),
    seg("potion7", 5.10, 6 * b, C("QUIRK", "KEEP POTIONS UPRIGHT", "Tip one over and it spills.")),
    seg("cake8", 5.25, 6 * b, C("QUIRK", "CAKES SQUISH", "Nothing heavier than padding goes on top.")),
    seg("cactus9", 3.10, 6 * b, C("QUIRK", "BALLOONS FLOAT. CACTI POP THEM.", "Spikes pop bubble wrap too, and wake sleepers.")),
    seg("robot11", 4.35, 6 * b, C("QUIRK", "CLANK MARCHES ON", "The wind-up robot walks until something stops him.")),
    seg("swan12", 4.55, 6 * b, C("QUIRK", "ICE MELTS NEAR HEAT", "Keep the swan away from the lava lamp.")),
    seg("frog14", 3.05, 6 * b, C("QUIRK", "FROGS HOP", "Every few seconds, wherever they like.")),
    seg("boing13", 4.45, 6 * b, C("QUIRK", "BOUNCY BALLS NEVER SETTLE", "Especially when Dash the courier throws the box.")),
    seg("fire15", 5.55, 6 * b, C("QUIRK", "EMBER SNEEZES FIRE", "Paper burns, bubble wrap melts, and the box can catch.")),
    # journeys, one bar each
    seg("depot10", 9.60, 4 * b, C("ON THE ROAD", "SIX KINDS OF JOURNEY", "Truck, sorting depot, doorstep, ferry, cargo plane... and catapult.", span=5), trans="smoothleft", td=0.25),
    seg("door13", 4.55, 4 * b),
    seg("seas16", 9.70, 4 * b),
    seg("air17", 4.45, 4 * b),
    seg("egg20", 3.65, 4 * b),
    # progression
    seg("shift4", 0.15, 4 * b, zoom=0.05, cap=C("CAREER", "20 DELIVERIES, 4 SHIFTS", "60 stars to earn, and new tape designs to unlock.", span=2), trans="fadewhite", td=0.2),
    seg("log", 0.40, 4 * b, zoom=0.07),
    # escalation: two beats, then single beats
    seg("seas16", 10.55, 2 * b, trans="fadewhite", td=0.12),
    seg("egg20", 4.15, 2 * b),
    seg("depot10", 10.70, 2 * b),
    seg("cactus9", 4.30, 1 * b),
    seg("boing13", 6.15, 1 * b),
    seg("sleep5", 11.20, 1 * b),
    seg("fire15", 6.75, 1 * b),
    seg("cold18", 19.95, 2 * b),
    # finale: the egg hatches
    seg("egg20", 28.85, 7 * b, P("KEEP THE DRAGON EGG WARM.", delay=0.1), trans="fadewhite", td=0.15),
    seg("titleplain", 1.5, 13 * b, dict(kind="end"), trans="fadeblack", td=0.4, zoom=0.05),
]


# ---------------------------------------------------------------------------------- helpers

def run(cmd, **kw):
    print("+", " ".join(str(c) for c in cmd[:12]), "..." if len(cmd) > 12 else "", flush=True)
    subprocess.run(["nice", "-n", "10", *map(str, cmd)], check=True, **kw)


def clip_dir(args, name):
    return os.path.join(args.clips, name)


def frames_in(args, name):
    return len([f for f in os.listdir(clip_dir(args, name)) if f.endswith(".jpg")])


def layout(segments, t0=0.0):
    """Start time of every segment on the output timeline (transitions overlap the previous one)."""
    t = t0
    out = []
    for i, s in enumerate(segments):
        if i > 0 or t0 > 0:
            t -= s["td"]
        out.append(t)
        t += s["dur"]
    return out, t


def build_cards(args, segs, starts):
    """Renders every card PNG and returns overlay specs with absolute times."""
    tmp = os.path.join(args.work, "cardtmp")
    overlays = []
    for i, s in enumerate(segs):
        cap = s["cap"]
        if not cap:
            continue
        span = cap.get("span", 1)
        last = segs[min(len(segs) - 1, i + span - 1)]
        t_end = starts[min(len(segs) - 1, i + span - 1)] + last["dur"]
        kind = cap["kind"]
        if kind == "caption":
            png = os.path.join(args.work, f"cap_{i:02d}.png")
            cards.caption(png, cap["kicker"], cap["headline"], cap["sub"], tmp)
            t0 = starts[i] + cap["delay"]
            overlays.append(dict(kind="caption", png=png, t0=t0, t1=t_end - 0.12, scale=0.8, x=58, y=None, ybottom=46))
        elif kind == "pill":
            png = os.path.join(args.work, f"pill_{i:02d}.png")
            cards.pill(png, cap["text"], tmp, fg=cards.INK, bg=cards.PAPER, pt=54, pad=(40, 18))
            t0 = starts[i] + cap["delay"]
            overlays.append(dict(kind="pill", png=png, t0=t0, t1=t_end - 0.08, scale=1.0, x=None, y=None, ybottom=86))
        elif kind == "title":
            logo = os.path.join(args.work, "logo.png")
            cards.logo(logo, tmp)
            tag = os.path.join(args.work, "tagline.png")
            cards.pill(tag, "Pack bizarre deliveries. Then watch them survive the trip.", tmp, fg=cards.CREAM, bg=cards.INK,
                       font=cards.ITALIC, pt=44, pad=(34, 16), tilt=-1.5)
            hit = starts[i] + s["td"]
            overlays.append(dict(kind="logo", png=logo, t0=hit, t1=starts[i] + s["dur"] - 0.05, scale=0.82, cx=760, cy=430))
            overlays.append(dict(kind="fade", png=tag, t0=hit + 4 * BEAT, t1=starts[i] + s["dur"] - 0.05, scale=1.0, cx=760, cy=880))
        elif kind == "end":
            logo = os.path.join(args.work, "logo.png")
            if not os.path.exists(logo):
                cards.logo(logo, tmp)
            tag = os.path.join(args.work, "end_tag.png")
            cards.pill(tag, "Pack it. Ship it. Watch it survive.", tmp, fg=cards.CREAM, bg=cards.INK, font=cards.ITALIC,
                       pt=46, pad=(36, 16), tilt=-1.5)
            url = os.path.join(args.work, "end_url.png")
            cards.pill(url, "github.com/nearbycoder/HandleWithCare", tmp, fg=cards.INK, bg=cards.PAPER, pt=60, pad=(44, 20))
            small = os.path.join(args.work, "end_small.png")
            cards.pill(small, "Source code and Linux build on GitHub   ·   Made with Unity 6 and Blender", tmp,
                       fg=cards.CREAM, bg=cards.RED, font=cards.XBOLD, pt=32, pad=(28, 12))
            a = starts[i] + s["td"]
            e = starts[i] + s["dur"]
            overlays.append(dict(kind="logo", png=logo, t0=a + 0.15, t1=e, scale=0.62, cx=960, cy=330))
            overlays.append(dict(kind="fade", png=tag, t0=a + 0.15 + 2 * BEAT, t1=e, scale=1.0, cx=960, cy=640))
            overlays.append(dict(kind="fade", png=url, t0=a + 0.15 + 4 * BEAT, t1=e, scale=1.0, cx=960, cy=790))
            overlays.append(dict(kind="fade", png=small, t0=a + 0.15 + 6 * BEAT, t1=e, scale=1.0, cx=960, cy=930))
    return overlays


def png_size(path, scale):
    w, h = cards.size(path)
    return int(round(w * scale / 2) * 2), int(round(h * scale / 2) * 2)


# ------------------------------------------------------------------------------------ picture

def picture_graph(args, segs, starts, overlays, total):
    """Builds the whole picture graph. Transitions are applied pairwise along the timeline."""
    inputs, chains = [], []
    for i, s in enumerate(segs):
        src_dur = s["dur"] * s["speed"]
        first = int(round(s["t_in"] * FPS))
        need = int(round(src_dur * FPS)) + 1
        avail = frames_in(args, s["clip"]) - first
        if avail < need:
            raise SystemExit(f"segment {i} ({s['clip']} @{s['t_in']}) needs {need} frames, clip has {avail} after the in point")
        inputs += ["-framerate", FPS, "-start_number", first, "-t", f"{src_dur + 0.2:.3f}",
                   "-i", os.path.join(clip_dir(args, s["clip"]), "%05d.jpg")]
        nf = int(round(s["dur"] * FPS))
        f = f"[{i}:v]setpts=PTS-STARTPTS"
        if s["speed"] != 1.0:
            f += f",setpts=PTS/{s['speed']}"
        f += f",fps={FPS},trim=end_frame={nf},setpts=PTS-STARTPTS"
        if s["zoom"]:
            f += f",scale=3840:2160,zoompan=z='1+{s['zoom']}*on/{nf}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=1:s={W}x{H}:fps={FPS}"
        if s["cap"] and s["cap"]["kind"] in ("title", "end"):
            f += ",gblur=sigma=7,eq=brightness=-0.05:saturation=0.85"
        f += f",scale=out_range=tv:out_color_matrix=bt709,format=yuv420p,setsar=1,settb=1/{FPS}[s{i}]"
        chains.append(f)

    # timeline: a running stream plus its length; cuts concat, transitions xfade at (length - td)
    run_lab, run_len = "s0", segs[0]["dur"]
    group = ["s0"]
    k = 0
    for i in range(1, len(segs)):
        s = segs[i]
        if s["trans"] == "cut":
            group.append(f"s{i}")
            run_len += s["dur"]
            continue
        if len(group) > 1:
            k += 1
            chains.append("".join(f"[{g}]" for g in group) + f"concat=n={len(group)}:v=1:a=0,settb=1/{FPS}[g{k}]")
            run_lab = f"g{k}"
        else:
            run_lab = group[0]
        k += 1
        chains.append(f"[{run_lab}][s{i}]xfade=transition={s['trans']}:duration={s['td']}:offset={run_len - s['td']:.4f}[g{k}]")
        run_lab = f"g{k}"
        group = [run_lab]
        run_len += s["dur"] - s["td"]
    if len(group) > 1:
        k += 1
        chains.append("".join(f"[{g}]" for g in group) + f"concat=n={len(group)}:v=1:a=0,settb=1/{FPS}[g{k}]")
        run_lab = f"g{k}"
    assert abs(run_len - total) < 0.01, (run_len, total)

    # overlays
    base = len(segs)
    v = run_lab
    for j, o in enumerate(overlays):
        idx = base + j
        d = o["t1"] - o["t0"]
        inputs += ["-loop", 1, "-framerate", FPS, "-t", f"{d:.3f}", "-i", o["png"]]
        w, h = png_size(o["png"], o["scale"])
        t0 = o["t0"]
        lab = f"o{j}"
        if o["kind"] == "logo":
            # stamp in: big and quick, a little overshoot, then settle
            u1 = f"clip((t-{t0:.3f})/0.16,0,1)"
            u2 = f"clip((t-{t0:.3f}-0.16)/0.22,0,1)"
            sc = f"if(isnan(t),{o['scale']},{o['scale']}*(1+0.55*pow(1-{u1},2)-0.04*sin({u2}*PI)))"
            chains.append(f"[{idx}:v]format=rgba,fade=t=out:st={d - 0.25:.3f}:d=0.25:alpha=1,setpts=PTS-STARTPTS+{t0:.3f}/TB,"
                          f"scale=w='trunc(iw*{sc}/2)*2':h=-2:eval=frame,scale=out_range=tv:out_color_matrix=bt709,format=yuva420p[{lab}]")
            x, y = f"{o['cx']}-w/2", f"{o['cy']}-h/2"
        elif o["kind"] == "fade":
            chains.append(f"[{idx}:v]format=rgba,scale={w}:{h},fade=t=in:st=0:d=0.3:alpha=1,fade=t=out:st={d - 0.25:.3f}:d=0.25:alpha=1,"
                          f"setpts=PTS-STARTPTS+{t0:.3f}/TB,scale=out_range=tv:out_color_matrix=bt709,format=yuva420p[{lab}]")
            x = f"{o['cx'] - w // 2}"
            y = f"{o['cy'] - h // 2}+18*pow(max(0,1-(t-{t0:.3f})/0.4),3)"
        elif o["kind"] == "pill":
            chains.append(f"[{idx}:v]format=rgba,scale={w}:{h},fade=t=in:st=0:d=0.22:alpha=1,fade=t=out:st={d - 0.22:.3f}:d=0.22:alpha=1,"
                          f"setpts=PTS-STARTPTS+{t0:.3f}/TB,scale=out_range=tv:out_color_matrix=bt709,format=yuva420p[{lab}]")
            x = f"{(W - w) // 2}"
            y = f"{H - o['ybottom'] - h}+26*pow(max(0,1-(t-{t0:.3f})/0.35),3)"
        else:   # caption: slides in from the left while fading in
            chains.append(f"[{idx}:v]format=rgba,scale={w}:{h},fade=t=in:st=0:d=0.25:alpha=1,fade=t=out:st={d - 0.25:.3f}:d=0.25:alpha=1,"
                          f"setpts=PTS-STARTPTS+{t0:.3f}/TB,scale=out_range=tv:out_color_matrix=bt709,format=yuva420p[{lab}]")
            x = f"{o['x']}-90*pow(max(0,1-(t-{t0:.3f})/0.45),3)"
            y = f"{H - o['ybottom'] - h}"
        k += 1
        chains.append(f"[{v}][{lab}]overlay=x='{x}':y='{y}':eval=frame:eof_action=pass:enable='between(t,{t0:.3f},{o['t1']:.3f})'[v{k}]")
        v = f"v{k}"
    # fade in from black at the top, out to black at the end
    chains.append(f"[{v}]fade=t=in:st=0:d=0.35,fade=t=out:st={total - 0.8:.3f}:d=0.8,format=yuv420p,"
                  "setparams=range=tv:colorspace=bt709:color_primaries=bt709:color_trc=bt709[vout]")
    return inputs, chains


# --------------------------------------------------------------------------------------- audio

def audio(args, segs, starts, total, out):
    inputs, chains, mix = [], [], []
    for i, s in enumerate(segs):
        src = os.path.join(clip_dir(args, s["clip"]), "audio.wav")
        if not os.path.exists(src):
            continue
        src_dur = s["dur"] * s["speed"]
        idx = len(inputs) // 6
        inputs += ["-ss", f"{s['t_in']:.3f}", "-t", f"{src_dur:.3f}", "-i", src]
        fin = max(0.01, s["td"])
        f = f"[{idx}:a]asetpts=PTS-STARTPTS,aresample=48000"
        if s["speed"] != 1.0:
            f += f",atempo={s['speed']}"
        f += (f",volume={s['gain']}dB,afade=t=in:st=0:d={fin:.3f},afade=t=out:st={max(0, s['dur'] - 0.04):.3f}:d=0.04,"
              f"adelay={int(starts[i] * 1000)}:all=1,apad=whole_dur={total:.3f}[a{i}]")
        chains.append(f)
        mix.append(f"[a{i}]")
    chains.append("".join(mix) + f"amix=inputs={len(mix)}:normalize=0:dropout_transition=0,atrim=0:{total:.3f}[sfx]")

    # music bed: the reveal sting under the cold open's unboxing, the journey theme from the title hit,
    # the title theme under the end card; extra stamps and whooshes on the big moments
    n = len(inputs) // 6

    def add(path, at, gain, src_in=0.0, dur=None, fade_in=0.0, fade_out=0.0, loop=False):
        nonlocal n
        if loop:
            inputs.extend(["-stream_loop", -1])
        inputs.extend(["-i", path])
        idx = n
        n += 1
        f = f"[{idx}:a]aresample=48000,aformat=channel_layouts=stereo"
        if dur is not None:
            f += f",atrim={src_in:.3f}:{src_in + dur:.3f}"
        f += f",asetpts=PTS-STARTPTS,volume={gain}dB"
        if fade_in:
            f += f",afade=t=in:st=0:d={fade_in}"
        if fade_out and dur:
            f += f",afade=t=out:st={dur - fade_out:.3f}:d={fade_out}"
        f += f",adelay={int(at * 1000)}:all=1,apad=whole_dur={total:.3f},atrim=0:{total:.3f}"
        return idx, f

    music_parts, extra_parts = [], []
    cold_end = starts[len(COLD)]
    title_hit = cold_end + MAIN[0]["td"]
    end_seg = len(segs) - 1
    end_start = starts[end_seg] + segs[end_seg]["td"]
    # cold open: the calm title theme, then the game's own unboxing music under the reveal
    i, f = add(os.path.join(MUSIC, "title.wav"), 0.0, -11, 0.0, starts[2] + 0.2, fade_in=0.3, fade_out=0.6)
    chains.append(f + f"[m{i}]"); music_parts.append(f"[m{i}]")
    i, f = add(os.path.join(MUSIC, "reveal.wav"), starts[2] - 0.3, -9, 0.0, cold_end - starts[2] + 0.3, fade_in=0.4, fade_out=0.25)
    chains.append(f + f"[m{i}]"); music_parts.append(f"[m{i}]")
    bed = end_start - title_hit + 1.2   # the theme runs under the whole feature section
    i, f = add(os.path.join(MUSIC, "journey.wav"), title_hit, -5, 0.0, bed, fade_out=1.6, loop=True)
    chains.append(f + f"[m{i}]"); music_parts.append(f"[m{i}]")
    i, f = add(os.path.join(MUSIC, "title.wav"), end_start - 0.4, -6, 0.0, total - end_start + 0.4, fade_in=0.8, fade_out=1.0)
    chains.append(f + f"[m{i}]"); music_parts.append(f"[m{i}]")
    chains.append("".join(music_parts) + f"amix=inputs={len(music_parts)}:normalize=0:dropout_transition=0[music]")

    i, f = add(os.path.join(SFX, "stamp_good.wav"), title_hit - 0.02, -2)
    chains.append(f + f"[e{i}]"); extra_parts.append(f"[e{i}]")
    i, f = add(os.path.join(SFX, "whoosh.wav"), title_hit - 0.25, -8)
    chains.append(f + f"[e{i}]"); extra_parts.append(f"[e{i}]")
    i, f = add(os.path.join(SFX, "stamp_good.wav"), end_start + 0.13, -4)
    chains.append(f + f"[e{i}]"); extra_parts.append(f"[e{i}]")
    i, f = add(os.path.join(SFX, "fanfare.wav"), end_start + 0.15 + 4 * BEAT, -9)
    chains.append(f + f"[e{i}]"); extra_parts.append(f"[e{i}]")
    for j, s in enumerate(segs):
        if s["trans"] == "smoothleft":
            i, f = add(os.path.join(SFX, "whoosh.wav"), starts[j] - 0.1, -10)
            chains.append(f + f"[e{i}]"); extra_parts.append(f"[e{i}]")
    chains.append("".join(extra_parts) + f"amix=inputs={len(extra_parts)}:normalize=0:dropout_transition=0[extra]")

    # duck the music under the game's sound effects
    chains.append("[sfx]asplit=2[sfxa][key]")
    chains.append("[music][key]sidechaincompress=threshold=0.06:ratio=4:attack=8:release=320:makeup=1[ducked]")
    chains.append("[sfxa][ducked][extra]amix=inputs=3:normalize=0:dropout_transition=0,"
                  f"afade=t=out:st={total - 0.9:.3f}:d=0.9,alimiter=limit=0.9:level=false[aout]")
    raw = out + ".raw.wav"
    graph = os.path.join(args.work, "audio_graph.txt")
    open(graph, "w").write(";\n".join(chains))
    run(["ffmpeg", "-y", "-v", "error", *inputs, "-/filter_complex", graph, "-map", "[aout]", "-c:a", "pcm_s16le", "-ar", 48000, raw])
    # two-pass loudness normalisation to -16 LUFS, -1.5 dBTP
    meas = subprocess.run(["ffmpeg", "-hide_banner", "-i", raw, "-af", "loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json", "-f", "null", "-"],
                          capture_output=True, text=True).stderr
    js = json.loads(meas[meas.rindex("{"):meas.rindex("}") + 1])
    ln = (f"loudnorm=I=-16:TP=-1.5:LRA=11:measured_I={js['input_i']}:measured_TP={js['input_tp']}:"
          f"measured_LRA={js['input_lra']}:measured_thresh={js['input_thresh']}:offset={js['target_offset']}:linear=true")
    run(["ffmpeg", "-y", "-v", "error", "-i", raw, "-af", ln + ",aresample=48000", "-c:a", "pcm_s16le", out])
    os.remove(raw)


# ----------------------------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--clips", default=os.path.join(ROOT, "Recordings", "clips"))
    ap.add_argument("--work", default=os.path.join(ROOT, "Recordings", "trailer_build"))
    ap.add_argument("--out", default=os.path.join(ROOT, "docs", "media", "trailer.mp4"))
    ap.add_argument("--bitrate", default="2600k", help="video bitrate for the final two-pass encode")
    ap.add_argument("--skip-final", action="store_true", help="stop after the high-quality intermediate")
    ap.add_argument("--reuse-audio", action="store_true", help="keep the previous mix.wav")
    args = ap.parse_args()
    os.makedirs(args.work, exist_ok=True)
    os.makedirs(os.path.dirname(args.out), exist_ok=True)

    # a transition overlaps the end of the previous segment: lengthen the incoming segment by the
    # overlap so every cut after the title hit still lands on the beat grid
    for s in MAIN:
        s["dur"] += s["td"]
    segs = COLD + MAIN
    starts, total = layout(segs)
    print(f"{len(segs)} segments, {total:.2f} s; title hit at {starts[len(COLD)] + MAIN[0]['td']:.2f} s")
    with open(os.path.join(args.work, "timeline.txt"), "w") as fh:
        for i, (s, t) in enumerate(zip(segs, starts)):
            cap = s["cap"] or {}
            fh.write(f"{t:7.3f}  {s['dur']:5.2f}s  {s['clip']:<10} in {s['t_in']:6.2f}  {s['trans']:<10} "
                     f"{cap.get('headline') or cap.get('text') or cap.get('kind') or ''}\n")
    overlays = build_cards(args, segs, starts)

    wav = os.path.join(args.work, "mix.wav")
    if not (args.reuse_audio and os.path.exists(wav)):
        audio(args, segs, starts, total, wav)

    inputs, chains = picture_graph(args, segs, starts, overlays, total)
    graph = os.path.join(args.work, "video_graph.txt")
    open(graph, "w").write(";\n".join(chains))
    master = os.path.join(args.work, "master.mp4")
    run(["ffmpeg", "-y", "-v", "error", "-stats", *inputs, "-i", wav, "-/filter_complex", graph,
         "-map", "[vout]", "-map", f"{len(segs) + len(overlays)}:a", "-r", FPS,
         "-c:v", "libx264", "-preset", "medium", "-crf", "12", "-pix_fmt", "yuv420p", *COLOR,
         "-c:a", "pcm_s16le", "-t", f"{total:.3f}", master])
    if args.skip_final:
        return
    # final: two-pass H.264 at a bitrate that keeps the file under 40 MB, AAC audio
    passlog = os.path.join(args.work, "x264pass")
    common = ["-c:v", "libx264", "-preset", "slower", "-b:v", args.bitrate, "-maxrate", "6000k", "-bufsize", "8000k",
              "-pix_fmt", "yuv420p", *COLOR, "-profile:v", "high", "-level", "4.1", "-g", 60, "-passlogfile", passlog]
    run(["ffmpeg", "-y", "-v", "error", "-i", master, *common, "-pass", 1, "-an", "-f", "mp4", "/dev/null"])
    run(["ffmpeg", "-y", "-v", "error", "-i", master, *common, "-pass", 2, "-c:a", "aac", "-b:a", "192k", "-ar", 48000,
         "-movflags", "+faststart", args.out])
    for f in os.listdir(args.work):
        if f.startswith("x264pass"):
            os.remove(os.path.join(args.work, f))
    print(f"wrote {args.out} ({os.path.getsize(args.out) / 1e6:.1f} MB, {total:.1f} s)")


if __name__ == "__main__":
    main()
