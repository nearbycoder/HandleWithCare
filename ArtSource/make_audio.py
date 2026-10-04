"""Synthesized sound effects and music for Handle With Care (numpy only).

Run with Blender's bundled Python (it ships numpy):
    ~/.local/opt/blender-4.5.9-linux-x64/4.5/python/bin/python3.11 ArtSource/make_audio.py [sfx] [music] [--only name,...]

Writes 16-bit mono WAVs to Assets/Resources/Audio/Sfx and stereo music loops to .../Music.
Everything is generated from oscillators, filtered noise, envelopes and simple physical models.
"""
import math
import os
import struct
import sys
import wave

import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SFX_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio", "Sfx")
MUSIC_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio", "Music")
rng = np.random.default_rng(42)


# ============================================================================ core DSP

def t_axis(dur):
    return np.arange(int(dur * SR)) / SR


def noise(dur, seed=None):
    r = rng if seed is None else np.random.default_rng(seed)
    return r.uniform(-1, 1, int(dur * SR))


def env_ad(n, attack, decay, curve=4.0):
    """Attack (s) then exponential-ish decay over the rest; n samples."""
    a = max(1, int(attack * SR))
    e = np.ones(n)
    e[:a] = np.linspace(0, 1, a) if a < n else np.linspace(0, 1, n)[:a]
    rest = n - a
    if rest > 0:
        x = np.linspace(0, 1, rest)
        e[a:] = np.exp(-curve * x * (1.0 / max(decay, 1e-3)) * (rest / SR))
    return e


def env_exp(n, tau):
    return np.exp(-np.arange(n) / (tau * SR))


def adsr(n, a, d, s, r):
    e = np.zeros(n)
    ai, di, ri = int(a * SR), int(d * SR), int(r * SR)
    si = max(0, n - ai - di - ri)
    idx = 0
    e[idx:idx + ai] = np.linspace(0, 1, ai); idx += ai
    e[idx:idx + di] = np.linspace(1, s, di); idx += di
    e[idx:idx + si] = s; idx += si
    e[idx:idx + ri] = np.linspace(s, 0, min(ri, n - idx))
    return e


def onepole_lp(x, cutoff):
    """One-pole low-pass, cutoff may be an array (Hz). Vectorised via cumulative trick in blocks."""
    cutoff = np.broadcast_to(np.asarray(cutoff, dtype=float), x.shape)
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    # pure python loop is fine for SFX lengths; music uses FFT filters
    for i in range(len(x)):
        acc = (1 - a[i]) * x[i] + a[i] * acc
        y[i] = acc
    return y


def fft_filter(x, lo=None, hi=None, order=2.0):
    """Zero-phase band filter in the frequency domain (smooth Butterworth-like magnitude)."""
    n = len(x)
    N = 1 << int(np.ceil(np.log2(n + 1)))
    X = np.fft.rfft(x, N)
    f = np.fft.rfftfreq(N, 1 / SR)
    H = np.ones_like(f)
    if hi is not None:
        H *= 1 / np.sqrt(1 + (f / hi) ** (2 * order))
    if lo is not None:
        with np.errstate(divide="ignore"):
            H *= 1 / np.sqrt(1 + (lo / np.maximum(f, 1e-6)) ** (2 * order))
    return np.fft.irfft(X * H, N)[:n]


def resonator(x, freq, q):
    """Two-pole resonant band-pass (biquad, direct loop)."""
    w = 2 * np.pi * freq / SR
    alpha = np.sin(w) / (2 * q)
    b0, b2 = alpha, -alpha
    a0, a1, a2 = 1 + alpha, -2 * np.cos(w), 1 - alpha
    b0, b2, a1, a2 = b0 / a0, b2 / a0, a1 / a0, a2 / a0
    y = np.zeros_like(x)
    x1 = x2 = y1 = y2 = 0.0
    for i in range(len(x)):
        xi = x[i]
        yi = b0 * xi + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1 = x1, xi
        y2, y1 = y1, yi
        y[i] = yi
    return y


def sine(freq, dur, phase=0.0):
    t = t_axis(dur)
    if np.ndim(freq):
        ph = 2 * np.pi * np.cumsum(freq) / SR
        return np.sin(ph + phase)
    return np.sin(2 * np.pi * freq * t + phase)


def chirp(f0, f1, dur, curve=1.0):
    n = int(dur * SR)
    u = np.linspace(0, 1, n) ** curve
    f = f0 + (f1 - f0) * u
    return sine(f, dur)


def fm(carrier, mod_ratio, index, dur, index_env=None):
    t = t_axis(dur)
    m = np.sin(2 * np.pi * carrier * mod_ratio * t)
    idx = index if index_env is None else index * index_env
    return np.sin(2 * np.pi * carrier * t + idx * m)


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[:len(p)] += p
    return out


def place(dst, src, at):
    i = int(at * SR)
    if i >= len(dst):
        return dst
    m = min(len(src), len(dst) - i)
    dst[i:i + m] += src[:m]
    return dst


def silence(dur):
    return np.zeros(int(dur * SR))


def normalize(x, peak=0.89):
    m = np.max(np.abs(x)) + 1e-9
    return x * (peak / m)


def fade(x, fin=0.002, fout=0.01):
    n = len(x)
    a, b = int(fin * SR), int(fout * SR)
    if a > 0:
        x[:a] *= np.linspace(0, 1, a)
    if b > 0:
        x[-b:] *= np.linspace(1, 0, b)
    return x


def softclip(x, drive=1.5):
    return np.tanh(x * drive) / np.tanh(drive)


def reverb(x, size=1.2, mix_amt=0.25, damp=6000, seed=7, stereo=False):
    """Convolution with synthetic decaying-noise IR."""
    n = int(size * SR)
    r = np.random.default_rng(seed)
    ir = r.standard_normal(n) * np.exp(-np.arange(n) / (size * SR / 6.5))
    ir = fft_filter(ir, lo=180, hi=damp)
    ir[: int(0.012 * SR)] *= np.linspace(0, 1, int(0.012 * SR))
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    L = len(x) + n
    N = 1 << int(np.ceil(np.log2(L)))
    wet = np.fft.irfft(np.fft.rfft(x, N) * np.fft.rfft(ir, N), N)[: len(x) + n]
    out = np.zeros(len(x) + n)
    out[: len(x)] += x * (1 - mix_amt)
    out += wet * mix_amt * 2.2
    return out


def write_wav(path, x, stereo_right=None):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(2 if stereo_right is not None else 1)
        w.setsampwidth(2)
        w.setframerate(SR)
        if stereo_right is None:
            data = (np.clip(x, -1, 1) * 32767).astype("<i2").tobytes()
        else:
            inter = np.empty(len(x) * 2)
            inter[0::2] = x
            inter[1::2] = stereo_right[: len(x)]
            data = (np.clip(inter, -1, 1) * 32767).astype("<i2").tobytes()
        w.writeframes(data)


def sfx(name, x, peak=0.89):
    x = normalize(fade(np.asarray(x, dtype=float)), peak)
    write_wav(os.path.join(SFX_DIR, name + ".wav"), x)
    print(f"[sfx] {name} {len(x) / SR:.2f}s")


# ============================================================================ sound designs

def thud(size=1.0, seed=0, bright=1.0):
    """Cardboard / soft body impact: low sine bump + filtered noise click."""
    d = 0.18 + 0.12 * size
    n = int(d * SR)
    f0 = 140 / (0.6 + size * 0.6)
    body = sine(np.linspace(f0 * 1.6, f0, n), d) * env_exp(n, 0.05 + 0.03 * size)
    click = fft_filter(noise(d, seed), lo=200, hi=2500 * bright) * env_exp(n, 0.012)
    box = resonator(noise(d, seed + 1) * env_exp(n, 0.02), 320 + 80 * seed % 3, 4) * 0.6
    return mix(body * 0.9, click * 0.5, box)


def make_sfx():
    # ---- cardboard and handling --------------------------------------------------------
    for i in range(3):
        sfx(f"thud_{i + 1}", thud(1.0 + i * 0.25, seed=i))
    sfx("thud", thud(1.1, seed=9))
    for i in range(3):
        sfx(f"place_{i + 1}", thud(0.6 + 0.15 * i, seed=20 + i, bright=1.3) * 0.9)
    sfx("place", thud(0.7, seed=30, bright=1.2))
    # soft padding bumps
    for i in range(3):
        n = int(0.16 * SR)
        x = fft_filter(noise(0.16, 40 + i), lo=150, hi=1800) * env_exp(n, 0.035)
        x += sine(np.linspace(110, 80, n), 0.16) * env_exp(n, 0.04) * 0.6
        sfx(f"soft_{i + 1}", x)
    # paper crinkle: dense random clicks through a bright band
    for i in range(3):
        d = 0.32
        n = int(d * SR)
        r = np.random.default_rng(100 + i)
        clicks = np.zeros(n)
        for k in range(55):
            p = int(r.uniform(0, 0.75) ** 1.5 * n)
            clicks[p:p + 40] += r.uniform(-1, 1, min(40, n - p)) * r.uniform(0.2, 1.0)
        x = fft_filter(clicks, lo=1500, hi=9000) * env_ad(n, 0.005, 0.4)
        sfx(f"pad_paper_{i + 1}", x)
    # bubble wrap squeak + mini pops
    for i in range(2):
        d = 0.3
        n = int(d * SR)
        sq = sine(np.linspace(900, 1300, n) + 60 * np.sin(np.linspace(0, 30, n)), d) * env_ad(n, 0.01, 0.6) * 0.25
        x = fft_filter(sq, lo=500, hi=4000)
        for k in range(3):
            pop = fft_filter(noise(0.02, 200 + i * 5 + k), lo=1500, hi=8000) * env_exp(int(0.02 * SR), 0.003)
            place(x, pop * 0.8, 0.04 + k * 0.07)
        sfx(f"pad_bubble_{i + 1}", x)
    # foam squish: low filtered noise with a soft pitch bend
    for i in range(2):
        d = 0.25
        n = int(d * SR)
        x = fft_filter(noise(d, 300 + i), lo=80, hi=900) * env_ad(n, 0.03, 0.5)
        x += sine(np.linspace(160, 90, n), d) * env_ad(n, 0.02, 0.4) * 0.4
        sfx(f"pad_foam_{i + 1}", x)
    # pick up: small swish up
    d = 0.18
    n = int(d * SR)
    sfx("pick", fft_filter(noise(d, 7), lo=600, hi=5000) * env_ad(n, 0.04, 0.3) * 0.5 + chirp(500, 900, d) * env_exp(n, 0.05) * 0.15)
    sfx("rotate", fft_filter(noise(0.12, 8), lo=1200, hi=6000) * env_ad(int(0.12 * SR), 0.01, 0.3) + chirp(700, 1100, 0.12) * env_exp(int(0.12 * SR), 0.03) * 0.2)
    sfx("remove", fft_filter(noise(0.2, 9), lo=400, hi=3500) * env_ad(int(0.2 * SR), 0.005, 0.4) * 0.7 + chirp(600, 300, 0.2) * env_exp(int(0.2 * SR), 0.06) * 0.2)
    sfx("nope", (sine(220, 0.09) + sine(233, 0.09)) * env_exp(int(0.09 * SR), 0.03) * 0.5)
    sfx("undo", chirp(900, 600, 0.1) * env_exp(int(0.1 * SR), 0.03))
    sfx("clear", mix(*[thud(0.6, seed=50 + k) * 0.6 for k in range(1)], fft_filter(noise(0.4, 51), lo=300, hi=3000) * env_ad(int(0.4 * SR), 0.01, 0.5) * 0.5))
    # divider slide: cardboard scrape
    d = 0.35
    n = int(d * SR)
    scrape = fft_filter(noise(d, 60), lo=500, hi=3500) * (0.6 + 0.4 * np.sin(np.linspace(0, 40, n))) * env_ad(n, 0.02, 0.6)
    sfx("divider", mix(scrape, thud(0.7, seed=61) * 0.6))
    sfx("shelf", scrape * 0.8 + place(silence(d), thud(0.6, seed=62), 0.2))
    # strap: ratchet clicks + buckle
    x = silence(0.4)
    for k in range(5):
        click = fft_filter(noise(0.015, 70 + k), lo=2000, hi=9000) * env_exp(int(0.015 * SR), 0.002)
        place(x, click, 0.02 + k * 0.05)
    place(x, (sine(1800, 0.08) + sine(2700, 0.08) * 0.5) * env_exp(int(0.08 * SR), 0.015) * 0.6, 0.3)
    sfx("strap", x)
    sfx("snap", mix(fft_filter(noise(0.08, 75), lo=800, hi=9000) * env_exp(int(0.08 * SR), 0.01), sine(np.linspace(300, 60, int(0.25 * SR)), 0.25) * env_exp(int(0.25 * SR), 0.05) * 0.5))

    # ---- sealing -----------------------------------------------------------------------
    # flaps folding: two cardboard whumps
    x = silence(0.8)
    place(x, thud(1.3, seed=80) * 0.8, 0.05)
    place(x, thud(1.2, seed=81) * 0.9, 0.38)
    place(x, fft_filter(noise(0.3, 82), lo=200, hi=1500) * env_ad(int(0.3 * SR), 0.05, 0.4) * 0.3, 0.0)
    sfx("flaps", x)
    x = silence(0.6)
    place(x, fft_filter(noise(0.25, 83), lo=300, hi=2500) * env_ad(int(0.25 * SR), 0.01, 0.4) * 0.8, 0.0)
    place(x, thud(1.0, seed=84), 0.12)
    sfx("flaps_open", x)
    # tape gun screech: band-limited noise with a rising squeal and AM flutter, then snip
    d = 0.95
    n = int(d * SR)
    fl = 1 + 0.35 * np.sin(2 * np.pi * np.linspace(0, d, n) * (38 + 10 * np.linspace(0, 1, n)))
    sq = sine(np.linspace(1700, 2400, n) + 120 * np.sin(np.linspace(0, 25, n)), d) * 0.28
    rasp = fft_filter(noise(d, 90), lo=1200, hi=7000) * fl
    e = np.ones(n)
    e[: int(0.04 * SR)] = np.linspace(0, 1, int(0.04 * SR))
    e[-int(0.08 * SR):] = np.linspace(1, 0.4, int(0.08 * SR))
    tapex = (rasp * 0.7 + sq * fl) * e
    snip = fft_filter(noise(0.05, 91), lo=2500, hi=10000) * env_exp(int(0.05 * SR), 0.006) * 1.4
    x = silence(1.15)
    place(x, tapex, 0.0)
    place(x, snip, 0.97)
    place(x, thud(0.5, seed=92) * 0.5, 1.0)
    sfx("tape", x)
    sfx("slice", fft_filter(noise(0.35, 95), lo=2500, hi=9000) * env_ad(int(0.35 * SR), 0.01, 0.5) * (0.7 + 0.3 * np.sin(np.linspace(0, 60, int(0.35 * SR)))))

    # ---- impacts and breakage ------------------------------------------------------------
    for i in range(3):
        sfx(f"clink_{i + 1}", mix(*[sine(f, 0.4) * env_exp(int(0.4 * SR), 0.08 / (k + 1)) * (0.6 / (k + 1)) for k, f in enumerate((2200 + 150 * i, 3570 + 90 * i, 5200))], fft_filter(noise(0.02, 110 + i), lo=3000, hi=10000) * 0.3))
    for i in range(3):
        x = mix(*[sine(f, 0.3) * env_exp(int(0.3 * SR), 0.05) * a for f, a in ((820 + 40 * i, 0.6), (1460, 0.4), (2310, 0.25))])
        sfx(f"clank_{i + 1}", mix(x, thud(0.6, seed=120 + i) * 0.5))
    # glass/ceramic smash: many tiny pings + noise burst
    for i in range(2):
        d = 1.0
        x = fft_filter(noise(0.12, 130 + i), lo=1500, hi=12000) * env_exp(int(0.12 * SR), 0.03)
        x = place(silence(d), x, 0.0)
        r = np.random.default_rng(140 + i)
        for k in range(26):
            f = r.uniform(2500, 7500)
            ping = sine(f, 0.25) * env_exp(int(0.25 * SR), r.uniform(0.02, 0.07)) * r.uniform(0.2, 0.6)
            place(x, ping, r.uniform(0.0, 0.5) ** 1.6)
        place(x, thud(1.0, seed=150 + i) * 0.7, 0.0)
        sfx(f"smash_{i + 1}", reverb(x, 0.6, 0.15))
    for i in range(2):
        d = 0.6
        x = fft_filter(noise(0.08, 160 + i), lo=400, hi=5000) * env_exp(int(0.08 * SR), 0.02)
        x = place(silence(d), x, 0)
        for k in range(8):
            place(x, thud(0.3, seed=170 + k) * 0.4, 0.03 + k * 0.04 + (k % 3) * 0.01)
        sfx(f"crack_{i + 1}", x)
    sfx("squish", fft_filter(noise(0.4, 180), lo=80, hi=1200) * env_ad(int(0.4 * SR), 0.02, 0.5) + sine(np.linspace(200, 70, int(0.4 * SR)), 0.4) * env_exp(int(0.4 * SR), 0.12) * 0.6)
    sfx("pop", fft_filter(noise(0.03, 190), lo=1500, hi=9000) * env_exp(int(0.03 * SR), 0.004))
    x = fft_filter(noise(0.5, 191), lo=100, hi=8000) * env_exp(int(0.5 * SR), 0.05)
    x += sine(np.linspace(180, 40, int(0.5 * SR)), 0.5) * env_exp(int(0.5 * SR), 0.06) * 0.7
    sfx("bang", reverb(softclip(x, 2.5), 0.8, 0.2))
    sfx("spill", fft_filter(noise(0.7, 195), lo=200, hi=2500) * env_ad(int(0.7 * SR), 0.05, 0.5) * (0.6 + 0.4 * np.sin(np.linspace(0, 70, int(0.7 * SR)))))
    sfx("topple", mix(fft_filter(noise(0.2, 196), lo=300, hi=2000) * env_ad(int(0.2 * SR), 0.05, 0.4) * 0.4, place(silence(0.5), thud(0.8, seed=197), 0.22)))
    sfx("melt", fft_filter(noise(0.8, 198), lo=500, hi=4000) * env_ad(int(0.8 * SR), 0.2, 0.6) * 0.5 + place(silence(0.8), sine(np.linspace(1400, 700, int(0.15 * SR)), 0.15) * env_exp(int(0.15 * SR), 0.04), 0.5))

    # ---- creatures ----------------------------------------------------------------------
    # snore: filtered noise breaths with a low buzz
    d = 1.6
    n = int(d * SR)
    breath = np.sin(np.linspace(0, np.pi, n)) ** 2
    buzz = (sine(70 + 6 * np.sin(np.linspace(0, 40, n)), d) * 0.5 + fft_filter(noise(d, 210), lo=100, hi=900))
    sfx("snore", buzz * breath * np.concatenate([np.ones(n // 2), np.linspace(1, 0.2, n - n // 2)]))
    # armadillo yelp: quick FM chirp up and down
    d = 0.35
    n = int(d * SR)
    f = 600 + 900 * np.sin(np.linspace(0, np.pi, n)) ** 0.6
    sfx("yelp", fm(1, 1, 0, d) * 0 + sine(f, d) * env_ad(n, 0.01, 0.5) * 0.6 + sine(f * 2.01, d) * env_ad(n, 0.01, 0.4) * 0.25)
    # dragon "ah... ah..." (rising vowel) and "CHOO" burst + fire whoosh
    d = 0.9
    n = int(d * SR)
    vowel = np.zeros(n)
    for k, (st, ln) in enumerate(((0.0, 0.32), (0.45, 0.4))):
        seg = int(ln * SR)
        f0 = np.linspace(260 + k * 60, 330 + k * 80, seg)
        src = np.sign(sine(f0, ln)) * 0.5 + sine(f0, ln) * 0.5
        voc = resonator(src, 800, 5) + resonator(src, 1200, 6) * 0.6
        voc *= adsr(seg, 0.05, 0.1, 0.8, 0.12)
        place(vowel, voc, st)
    sfx("ahh", vowel)
    d = 1.1
    n = int(d * SR)
    choo = fft_filter(noise(0.25, 220), lo=500, hi=6000) * env_ad(int(0.25 * SR), 0.005, 0.5) * 1.2
    choo = place(silence(d), choo, 0.0)
    fire = fft_filter(noise(0.9, 221), lo=150, hi=3000) * env_ad(int(0.9 * SR), 0.05, 0.6)
    crackle = np.zeros(int(0.9 * SR))
    r = np.random.default_rng(222)
    for k in range(40):
        p = int(r.uniform(0, 0.85) * SR)
        crackle[p:p + 60] += r.uniform(-1, 1, 60) * r.uniform(0.3, 1)
    place(choo, fire * 0.8 + fft_filter(crackle, lo=1500, hi=8000) * 0.5, 0.05)
    sfx("sneeze", reverb(choo, 0.6, 0.15))
    # frog ribbit: two short FM croaks
    x = silence(0.4)
    for k in range(2):
        d2 = 0.11
        n2 = int(d2 * SR)
        cro = fm(180, 2.3, 4.0, d2, env_exp(n2, 0.04)) * env_ad(n2, 0.005, 0.5)
        place(x, cro, 0.02 + k * 0.15)
    sfx("ribbit", x)
    # robot whirr: buzzing saw with tick
    d = 0.5
    n = int(d * SR)
    saw = 2 * ((np.cumsum(np.full(n, 95 + 20 * np.sin(np.linspace(0, 6, n)))) / SR) % 1) - 1
    sfx("whirr", fft_filter(saw, lo=200, hi=2500) * env_ad(n, 0.05, 0.5) * 0.6)
    sfx("boing", sine(np.linspace(320, 160, int(0.4 * SR)) * (1 + 0.08 * np.sin(np.linspace(0, 60, int(0.4 * SR)))), 0.4) * env_exp(int(0.4 * SR), 0.12))

    # ---- loops for vehicles ----------------------------------------------------------------
    d = 2.0
    n = int(d * SR)
    t = t_axis(d)
    eng = np.sign(np.sin(2 * np.pi * 42 * t)) * 0.4 + np.sin(2 * np.pi * 84 * t) * 0.3
    eng = fft_filter(eng, hi=700) * (0.8 + 0.2 * np.sin(2 * np.pi * 7 * t))
    road = fft_filter(noise(d, 300), lo=60, hi=400) * 0.6
    loop = eng + road
    loop = loop * 0.5 + np.roll(loop, n // 2) * 0.5
    sfx("engine", loop, peak=0.6)
    hum = np.sin(2 * np.pi * 60 * t) * 0.3 + np.sin(2 * np.pi * 120 * t) * 0.15 + fft_filter(noise(d, 301), lo=200, hi=1500) * 0.35 * (0.8 + 0.2 * np.sin(2 * np.pi * 9 * t))
    for k in range(8):
        place(hum, thud(0.3, seed=310 + k) * 0.15, k * 0.25)
    sfx("conveyor", hum, peak=0.5)
    sfx("whoosh", fft_filter(noise(0.5, 320), lo=300, hi=4000) * np.sin(np.linspace(0, np.pi, int(0.5 * SR))) ** 2)

    # ---- UI ----------------------------------------------------------------------------------
    sfx("click", (sine(1400, 0.05) * 0.5 + fft_filter(noise(0.05, 400), lo=2000, hi=8000) * 0.5) * env_exp(int(0.05 * SR), 0.008))
    sfx("hover", sine(2200, 0.03) * env_exp(int(0.03 * SR), 0.006) * 0.4)
    sfx("note", fft_filter(noise(0.18, 401), lo=800, hi=6000) * env_ad(int(0.18 * SR), 0.01, 0.4) * 0.6 + place(silence(0.18), thud(0.3, seed=402) * 0.4, 0.06))
    # rubber stamp: thump + slap
    st = mix(thud(0.9, seed=410), fft_filter(noise(0.06, 411), lo=600, hi=6000) * env_exp(int(0.06 * SR), 0.01) * 0.8)
    sfx("stamp_good", mix(st, place(silence(0.6), mix(*[sine(f, 0.5) * env_exp(int(0.5 * SR), 0.12) * 0.25 for f in (1046.5, 1318.5, 1568)]), 0.03)))
    sfx("stamp_ok", mix(st, place(silence(0.5), mix(*[sine(f, 0.4) * env_exp(int(0.4 * SR), 0.1) * 0.22 for f in (880, 1108.7)]), 0.03)))
    sfx("stamp_bad", mix(st, place(silence(0.6), mix(*[sine(f, 0.5) * env_exp(int(0.5 * SR), 0.12) * 0.25 for f in (311, 293.7)]), 0.03)))
    sfx("lift", chirp(400, 900, 0.5, 0.7) * env_ad(int(0.5 * SR), 0.05, 0.5) * 0.3 + fft_filter(noise(0.5, 420), lo=1500, hi=7000) * env_ad(int(0.5 * SR), 0.2, 0.5) * 0.2)
    for i, f in enumerate((1046.5, 1318.5, 1568.0)):
        x = mix(sine(f, 0.8) * env_exp(int(0.8 * SR), 0.25), sine(f * 2, 0.8) * env_exp(int(0.8 * SR), 0.12) * 0.3, sine(f * 3.01, 0.8) * env_exp(int(0.8 * SR), 0.06) * 0.15)
        sfx(f"star_{i + 1}", reverb(x, 0.8, 0.2))


# ============================================================================ music

NOTE = {n: i for i, n in enumerate(["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"])}


def mtof(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def note(name):
    """'C4' -> midi."""
    pitch = name[:-1]
    octave = int(name[-1])
    return 12 * (octave + 1) + NOTE[pitch]


def epiano(freq, dur, vel=0.8):
    """Rhodes-ish FM electric piano with tine bell and soft decay."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    e = np.exp(-t / (0.9 + 0.4 * (220 / freq)))
    ie = np.exp(-t / 0.18)
    body = np.sin(2 * np.pi * freq * t + 1.2 * ie * np.sin(2 * np.pi * freq * t))
    tine = np.sin(2 * np.pi * freq * 7.0 * t) * np.exp(-t / 0.05) * 0.12
    x = (body + tine) * e
    a = int(0.004 * SR)
    x[:a] *= np.linspace(0, 1, a)
    r = int(0.06 * SR)
    x[-r:] *= np.linspace(1, 0, r)
    return x * vel


def pluck(freq, dur, vel=0.8, bright=0.5, seed=0):
    """Karplus-Strong plucked string (upright bass / guitar)."""
    n = int(dur * SR)
    period = max(2, int(SR / freq))
    r = np.random.default_rng(seed)
    buf = r.uniform(-1, 1, period)
    buf = fft_filter(np.tile(buf, 4), hi=freq * (2 + 8 * bright))[:period]
    out = np.zeros(n)
    blocks = n // period + 1
    cur = buf.copy()
    decay = 0.996 - 0.004 * (1 - bright)
    pos = 0
    for b in range(blocks):
        m = min(period, n - pos)
        if m <= 0:
            break
        out[pos:pos + m] = cur[:m]
        nxt = 0.5 * (cur + np.roll(cur, -1)) * decay
        cur = nxt
        pos += m
    out *= vel
    out[-int(0.02 * SR):] *= np.linspace(1, 0, int(0.02 * SR))
    return out


def marimba(freq, dur, vel=0.8):
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.sin(2 * np.pi * freq * t) * np.exp(-t / 0.35) + np.sin(2 * np.pi * freq * 3.93 * t) * np.exp(-t / 0.05) * 0.35 + np.sin(2 * np.pi * freq * 9.4 * t) * np.exp(-t / 0.015) * 0.15
    x[: int(0.002 * SR)] *= np.linspace(0, 1, int(0.002 * SR))
    return x * vel


def kick(vel=1.0):
    d = 0.35
    n = int(d * SR)
    f = 110 * np.exp(-np.arange(n) / (0.04 * SR)) + 48
    click = np.zeros(n)
    click[: int(0.004 * SR)] = fft_filter(noise(0.004, 1), lo=1000, hi=6000) * 0.4
    return (sine(f, d) * np.exp(-np.arange(n) / (0.12 * SR)) + click) * vel


def snare_brush(vel=1.0, seed=0):
    d = 0.25
    n = int(d * SR)
    return fft_filter(noise(d, seed), lo=800, hi=7000) * env_ad(n, 0.01, 0.35) * vel * 0.5


def snare(vel=1.0, seed=0):
    d = 0.2
    n = int(d * SR)
    return (fft_filter(noise(d, seed), lo=1000, hi=9000) * env_exp(n, 0.05) + sine(190, d) * env_exp(n, 0.03) * 0.6) * vel


def hat(vel=1.0, seed=0, open_=False):
    d = 0.18 if open_ else 0.05
    n = int(d * SR)
    return fft_filter(noise(d, seed), lo=6000, hi=14000) * env_exp(n, 0.06 if open_ else 0.012) * vel * 0.45


def shaker(vel=1.0, seed=0):
    d = 0.09
    n = int(d * SR)
    return fft_filter(noise(d, seed), lo=4000, hi=11000) * env_ad(n, 0.02, 0.4) * vel * 0.25


CHORDS = {  # voicings as midi offsets from root; root midi
    "Fmaj9": (53, [0, 7, 11, 14, 16]), "Em7": (52, [0, 7, 10, 14, 15]), "Dm9": (50, [0, 7, 10, 14, 15]),
    "G13": (55, [0, 4, 10, 14, 21]), "Cmaj9": (48, [0, 7, 11, 14, 16]), "Am9": (45, [0, 7, 10, 14, 15]),
    "Bbmaj7": (46, [0, 7, 11, 14, 16]), "A7": (45, [0, 7, 10, 13, 16]), "Gm9": (43, [0, 7, 10, 14, 17]),
    "C7": (48, [0, 4, 10, 14]), "F6": (53, [0, 4, 7, 9]), "D7": (50, [0, 4, 10, 18]), "Gmaj7": (43, [0, 7, 11, 16]),
    "Em9": (40, [0, 7, 10, 14, 15]), "Am7": (45, [0, 7, 10, 15]), "D9": (50, [0, 4, 10, 14]),
}


def song(name, bpm, bars, prog, style, seed, swing=0.58, length_beats=None):
    beat = 60.0 / bpm
    total = bars * 4 * beat
    n = int(total * SR) + SR * 3
    L = np.zeros(n)
    R = np.zeros(n)
    r = np.random.default_rng(seed)

    def at(bar, b):
        whole = int(b)
        frac = b - whole
        # swing the off-beat eighths
        if abs(frac - 0.5) < 1e-6:
            frac = swing
        return (bar * 4 + whole + frac) * beat

    def put(x, time, pan=0.0, gain=1.0):
        gl = gain * math.cos((pan + 1) * math.pi / 4)
        gr = gain * math.sin((pan + 1) * math.pi / 4)
        place(L, x * gl, time)
        place(R, x * gr, time)

    for bar in range(bars):
        chord = prog[bar % len(prog)]
        root, ivs = CHORDS[chord]
        if style == "lofi":
            # electric piano comping: chord on 1 and the "and" of 2, with gentle velocity humanisation
            for b, dur, vel in ((0, 1.6, 0.5), (1.5, 1.2, 0.38), (3.0, 0.9, 0.3)):
                for k, iv in enumerate(ivs):
                    f = mtof(root + 12 + iv)
                    put(epiano(f, dur * beat * 1.6, vel * r.uniform(0.85, 1.05)), at(bar, b) + k * 0.012 + r.uniform(0, 0.008), pan=(k / len(ivs) - 0.5) * 0.6, gain=0.16)
            # upright bass
            for b, off in ((0, 0), (1.5, 7), (2.5, 12 if bar % 2 else 10), (3.5, 7)):
                put(pluck(mtof(root - 12 + off), beat * 1.2, 0.9, 0.35, seed=bar * 10 + int(b * 2)), at(bar, b), pan=0, gain=0.5)
            # brushed drums
            put(kick(0.9), at(bar, 0), gain=0.55)
            put(kick(0.6), at(bar, 2.5), gain=0.45)
            for b in (1, 3):
                put(snare_brush(0.9, seed=bar * 4 + b), at(bar, b), pan=0.1, gain=0.6)
            for b in np.arange(0, 4, 0.5):
                put(hat(0.6 if b % 1 else 0.9, seed=int(bar * 8 + b * 2)), at(bar, b), pan=-0.3, gain=0.35)
            # sparse melody on marimba-ish epiano, pentatonic over the chord
            if bar % 2 == 1:
                scale = [0, 2, 4, 7, 9]
                for b in (0.5, 1.5, 2, 3):
                    if r.random() < 0.6:
                        m = root + 24 + scale[r.integers(0, 5)]
                        put(epiano(mtof(m), beat * 1.5, 0.35), at(bar, b), pan=0.35, gain=0.22)
        elif style == "caper":
            # bouncy pizzicato bass, marimba hook, snappy drums
            for b, off in ((0, 0), (0.5, 12), (1, 7), (1.5, 12), (2, 0), (2.5, 12), (3, 10), (3.5, 7)):
                put(pluck(mtof(root - 12 + off), beat * 0.5, 0.8, 0.6, seed=bar * 16 + int(b * 2)), at(bar, b), gain=0.42)
            hook = [0, 4, 7, 12, 11, 7, 4, 2] if bar % 4 < 2 else [0, 2, 4, 7, 9, 7, 4, 0]
            for k, b in enumerate(np.arange(0, 4, 0.5)):
                if (k + bar) % 3 == 2 and bar % 2 == 0:
                    continue
                put(marimba(mtof(root + 24 + hook[k]), beat * 0.7, 0.55), at(bar, b), pan=0.25, gain=0.26)
            for k, iv in enumerate(ivs[:4]):
                put(epiano(mtof(root + 12 + iv), beat * 0.6, 0.3), at(bar, 1.5), pan=-0.3, gain=0.1)
                put(epiano(mtof(root + 12 + iv), beat * 0.6, 0.3), at(bar, 3.5), pan=-0.3, gain=0.1)
            put(kick(1.0), at(bar, 0), gain=0.6)
            put(kick(0.8), at(bar, 2), gain=0.5)
            put(snare(0.8, seed=bar), at(bar, 1), gain=0.42)
            put(snare(0.8, seed=bar + 99), at(bar, 3), gain=0.42)
            for b in np.arange(0, 4, 0.5):
                put(shaker(1.0, seed=int(bar * 8 + b * 2)), at(bar, b), pan=0.4, gain=0.6)
        elif style == "title":
            for b, dur in ((0, 3.8),):
                for k, iv in enumerate(ivs):
                    put(epiano(mtof(root + 12 + iv), dur * beat, 0.45), at(bar, b) + k * 0.03, pan=(k / len(ivs) - 0.5) * 0.8, gain=0.17)
            put(pluck(mtof(root - 12), beat * 3, 0.8, 0.3, seed=bar), at(bar, 0), gain=0.45)
            put(pluck(mtof(root - 5), beat * 1, 0.6, 0.3, seed=bar + 50), at(bar, 2.5), gain=0.35)
            melody = [7, 9, 12, 14, 12, 9, 7, 4]
            for k, b in enumerate((0.5, 1.0, 1.5, 2.5, 3.0)):
                if (bar + k) % 2 == 0:
                    put(marimba(mtof(root + 24 + melody[(bar * 3 + k) % 8]), beat * 1.2, 0.45), at(bar, b), pan=0.3, gain=0.24)
            if bar >= 2:
                put(kick(0.7), at(bar, 0), gain=0.4)
                put(snare_brush(0.8, seed=bar), at(bar, 2), gain=0.45)
                for b in np.arange(0, 4, 0.5):
                    put(hat(0.5, seed=int(bar * 8 + b * 2)), at(bar, b), pan=-0.3, gain=0.25)
        elif style == "reveal":
            # gentle drum-roll swell then a warm chord
            for k, iv in enumerate(ivs):
                put(epiano(mtof(root + 12 + iv), 4 * beat, 0.4), at(bar, 0) + k * 0.05, pan=(k / len(ivs) - 0.5) * 0.8, gain=0.16)
            put(pluck(mtof(root - 12), beat * 4, 0.7, 0.3, seed=bar), at(bar, 0), gain=0.4)
            for b in np.arange(0, 4, 0.25):
                put(snare_brush(0.25 + 0.15 * (b / 4), seed=int(bar * 16 + b * 4)), at(bar, b), gain=0.3)

    mixL, mixR = L[: int(total * SR)], R[: int(total * SR)]
    # wrap the tail of the overhang back to the start so the loop is seamless
    tailL, tailR = L[int(total * SR):], R[int(total * SR):]
    mixL[: len(tailL)] += tailL
    mixR[: len(tailR)] += tailR
    # vinyl hiss / warmth for lofi
    if style in ("lofi", "title"):
        hiss = fft_filter(np.random.default_rng(seed + 1).uniform(-1, 1, len(mixL)), lo=2000, hi=9000) * 0.004
        mixL += hiss
        mixR += hiss * 0.9
    wetL = reverb(mixL, 1.4, 0.18, 5000, seed)
    wetR = reverb(mixR, 1.4, 0.18, 5000, seed + 3)
    outL = wetL[: len(mixL)]
    outR = wetR[: len(mixR)]
    outL[: len(wetL) - len(mixL)] += wetL[len(mixL):]
    outR[: len(wetR) - len(mixR)] += wetR[len(mixR):]
    outL = softclip(outL * 1.1, 1.2)
    outR = softclip(outR * 1.1, 1.2)
    peak = max(np.max(np.abs(outL)), np.max(np.abs(outR))) + 1e-9
    outL *= 0.85 / peak
    outR *= 0.85 / peak
    write_wav(os.path.join(MUSIC_DIR, name + ".wav"), outL, outR)
    print(f"[music] {name} {total:.1f}s")


def make_music():
    song("packing", 78, 16, ["Fmaj9", "Em7", "Dm9", "G13", "Fmaj9", "Em7", "Am9", "G13"], "lofi", 11)
    song("journey", 128, 16, ["C7", "F6", "C7", "G13", "Am7", "D9", "Gm9", "C7"], "caper", 21)
    song("title", 72, 8, ["Cmaj9", "Am9", "Fmaj9", "G13"], "title", 31)
    song("reveal", 70, 2, ["Fmaj9", "Cmaj9"], "reveal", 41)
    # short stingers
    beat = 60 / 110
    def chord_hit(name, chords, path, arp=True):
        x = silence(2.6)
        y = silence(2.6)
        for i, (root, ivs) in enumerate(chords):
            for k, iv in enumerate(ivs):
                tone = mix(epiano(mtof(root + iv), 1.6, 0.5) * 0.25, marimba(mtof(root + iv + 12), 1.2, 0.4) * 0.12)
                t0 = i * beat * 0.5 + (k * 0.04 if arp else 0)
                place(x, tone, t0)
                place(y, tone, t0 + 0.003)
        x = reverb(x, 1.0, 0.25)[: len(x)]
        y = reverb(y, 1.0, 0.25, seed=9)[: len(y)]
        m = max(np.max(np.abs(x)), np.max(np.abs(y)))
        write_wav(os.path.join(SFX_DIR, path + ".wav"), x * 0.85 / m, y * 0.85 / m)
        print(f"[sting] {path}")
    chord_hit("success", [(60, [0, 4, 7]), (65, [0, 4, 9]), (67, [0, 4, 7, 11])], "success")
    chord_hit("fanfare", [(60, [0, 4, 7]), (64, [0, 3, 8]), (67, [0, 4, 7]), (72, [0, 4, 7, 12])], "fanfare")
    chord_hit("fail", [(57, [0, 3, 7]), (55, [0, 3, 6]), (52, [0, 3, 7])], "fail")
    # sad trombone-ish slide for big failures
    d = 1.6
    n = int(d * SR)
    f = np.concatenate([np.full(int(0.3 * SR), 233), np.full(int(0.3 * SR), 220), np.full(int(0.3 * SR), 207), np.linspace(196, 185, n - int(0.9 * SR)) * (1 + 0.02 * np.sin(np.linspace(0, 50, n - int(0.9 * SR))))])
    tb = fft_filter(np.sign(sine(f, d)) * 0.5 + sine(f, d) * 0.5, hi=1800) * adsr(n, 0.03, 0.1, 0.8, 0.3)
    sfx("fanfare_tail", reverb(mix(*[marimba(mtof(m), 0.8, 0.4) for m in (84,)]), 0.6, 0.2) * 0.6)
    sfx("wahwah", tb)


def main():
    args = sys.argv[1:]
    only = None
    if "--only" in args:
        only = set(args[args.index("--only") + 1].split(","))
    groups = [a for a in args if a in ("sfx", "music")] or ["sfx", "music"]
    os.makedirs(SFX_DIR, exist_ok=True)
    os.makedirs(MUSIC_DIR, exist_ok=True)
    if "sfx" in groups:
        make_sfx()
    if "music" in groups:
        make_music()


main()
