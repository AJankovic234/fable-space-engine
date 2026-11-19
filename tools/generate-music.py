"""Physically-modeled, studio-mastered Nordic instrumental loops for Space 2601AE.

Generates seamless stereo WAV loops (no vocals, RAVNIR / Wardruna war-drum & Tagelharpa vibe):
  - public/assets/sfx/music_calm.wav   (86 BPM, 12 bars = 33.49s, relaxed Nordic space drift)
  - public/assets/sfx/music_combat.wav (124 BPM, 16 bars = 30.97s, driving war-drum battle anthem)
"""

from pathlib import Path
import wave
import numpy as np
from scipy import signal

SR = 24000
OUT_DIR = Path(__file__).resolve().parent.parent / "public" / "assets" / "sfx"
RNG = np.random.default_rng(2601)


def wrap_add(buf: np.ndarray, start_idx: int, sig_l: np.ndarray, sig_r: np.ndarray | None = None):
    """Add mono/stereo signal into circular loop buffer so tails wrap seamlessly."""
    n = buf.shape[0]
    m = sig_l.shape[0]
    if sig_r is None:
        sig_r = sig_l
    idx = (np.arange(m) + start_idx) % n
    np.add.at(buf[:, 0], idx, sig_l)
    np.add.at(buf[:, 1], idx, sig_r)


def pan_gains(pan: float) -> tuple[float, float]:
    angle = (np.clip(pan, -1.0, 1.0) + 1.0) * (np.pi * 0.25)
    return float(np.cos(angle)), float(np.sin(angle))


def make_hall_ir(seconds: float = 2.1, decay: float = 2.5) -> np.ndarray:
    """Stereo Nordic stone-hall / fjord impulse response with early reflections."""
    n = int(SR * seconds)
    t = np.linspace(0.0, seconds, n, endpoint=False)
    ir = np.zeros((n, 2), dtype=np.float64)
    for ch in range(2):
        noise = RNG.standard_normal(n)
        env = np.exp(-t * (3.6 / seconds) * decay) * (1.0 - np.exp(-t * 45.0))
        raw = noise * env
        # Warm stone-hall high-cut (~3100 Hz)
        b, a = signal.butter(2, 3100 / (SR * 0.5), btype="low")
        tail = signal.lfilter(b, a, raw)
        # Early reflections for spatial width
        for delay_ms, amp in [(17 + ch * 5, 0.42), (31 + ch * 7, 0.30), (53 - ch * 6, 0.22), (79 + ch * 4, 0.14)]:
            d = int(SR * delay_ms / 1000.0)
            if d < n:
                tail[d] += amp * (1.0 if ch == 0 else -0.95)
        ir[:, ch] = tail
    ir /= np.max(np.abs(ir)) + 1e-9
    return ir * 0.32


def circular_reverb(stereo: np.ndarray, ir: np.ndarray, wet: float = 0.32) -> np.ndarray:
    """Convolve with stereo hall IR and wrap reverb tail seamlessly around loop boundary."""
    n = stereo.shape[0]
    out = stereo.copy()
    for ch in range(2):
        conv = signal.fftconvolve(stereo[:, ch], ir[:, ch], mode="full")
        wrapped = np.zeros(n, dtype=np.float64)
        for off in range(0, len(conv), n):
            chunk = conv[off:off + n]
            wrapped[:len(chunk)] += chunk
        out[:, ch] = stereo[:, ch] * (1.0 - wet * 0.28) + wrapped * wet
    return out


# ---------------- Physically-modeled Nordic Instruments ----------------

def synth_war_drum(pitch_hz: float = 54.0, dur: float = 0.68, vel: float = 1.0) -> np.ndarray:
    """Multi-mode Bessel circular-membrane Taiko / Nordic War Drum with leathery mallet punch."""
    n = int(SR * dur)
    t = np.linspace(0.0, dur, n, endpoint=False)
    # Circular drum-head eigenmodes (1.0, 1.594, 2.136, 2.296)
    f_env = pitch_hz * (1.0 + 1.45 * np.exp(-t / 0.048) + 0.35 * np.exp(-t / 0.16))
    phase = 2.0 * np.pi * np.cumsum(f_env) / SR
    body = (
        1.00 * np.sin(phase) * np.exp(-t / 0.26)
        + 0.36 * np.sin(phase * 1.594 + 0.4) * np.exp(-t / 0.12)
        + 0.18 * np.sin(phase * 2.136 + 1.1) * np.exp(-t / 0.065)
        + 0.12 * np.sin(phase * 2.296 + 2.0) * np.exp(-t / 0.045)
    )
    # Warm leathery skin beater thwack
    n_atk = int(SR * 0.06)
    noise = RNG.standard_normal(n_atk) * np.exp(-np.linspace(0, 0.06, n_atk) / 0.011)
    b, a = signal.butter(2, [140 / (SR * 0.5), 780 / (SR * 0.5)], btype="band")
    thwack = signal.lfilter(b, a, noise)
    sig = body * 0.85
    sig[:n_atk] += thwack * 1.55
    # Sub-chest punch
    sig = np.tanh(sig * 1.35) * vel
    return sig


def add_war_drum_ensemble(buf: np.ndarray, sec: float, vel: float = 1.0, pitch: float = 54.0, spread: float = 0.38):
    """Layer 3 slightly staggered & detuned drummers for massive RAVNIR drum-circle width."""
    hits = [
        (0.0, pitch, vel * 0.85, 0.0),
        (0.007, pitch * 0.96, vel * 0.52, -spread),
        (0.012, pitch * 1.05, vel * 0.50, spread),
    ]
    for dt, p, v, pan in hits:
        s = synth_war_drum(p, 0.68, v)
        gl, gr = pan_gains(pan)
        wrap_add(buf, int((sec + dt) * SR), s * gl, s * gr)


def synth_frame_drum(pitch_hz: float = 112.0, vel: float = 0.8, slap: bool = False) -> np.ndarray:
    """Hand-played Nordic shamanic frame drum (taut skin tone + palm slap)."""
    dur = 0.26 if not slap else 0.18
    n = int(SR * dur)
    t = np.linspace(0.0, dur, n, endpoint=False)
    f = pitch_hz * (1.0 + 0.82 * np.exp(-t / 0.028))
    ph = 2.0 * np.pi * np.cumsum(f) / SR
    tone = (np.sin(ph) * np.exp(-t / 0.10) + 0.42 * np.sin(ph * 1.62) * np.exp(-t / 0.045))
    noise = RNG.standard_normal(n) * np.exp(-t / (0.009 if slap else 0.015))
    lo, hi = (680, 2600) if slap else (280, 1250)
    b, a = signal.butter(2, [lo / (SR * 0.5), hi / (SR * 0.5)], btype="band")
    hit = signal.lfilter(b, a, noise)
    return np.tanh((tone * (0.45 if slap else 0.78) + hit * (1.8 if slap else 1.1)) * 1.2) * vel


def synth_rim_wood(vel: float = 0.6) -> np.ndarray:
    """Dry wood/antler rim knock."""
    dur = 0.065
    n = int(SR * dur)
    t = np.linspace(0.0, dur, n, endpoint=False)
    knock = (
        np.sin(2 * np.pi * 1380 * t) * np.exp(-t / 0.008)
        + 0.7 * np.sin(2 * np.pi * 2460 * t) * np.exp(-t / 0.006)
        + 0.5 * RNG.standard_normal(n) * np.exp(-t / 0.005)
    )
    b, a = signal.butter(2, [900 / (SR * 0.5), 4200 / (SR * 0.5)], btype="band")
    return signal.lfilter(b, a, knock) * vel * 0.55


def synth_rattle(vel: float = 0.25) -> np.ndarray:
    """Organic birch-bark seed rattle."""
    dur = 0.075
    n = int(SR * dur)
    t = np.linspace(0.0, dur, n, endpoint=False)
    env = np.sin(np.pi * t / dur) ** 1.6
    noise = RNG.standard_normal(n) * env
    b, a = signal.butter(2, [3400 / (SR * 0.5), 8800 / (SR * 0.5)], btype="band")
    return signal.lfilter(b, a, noise) * vel * 0.55


def synth_kravik_lyre(freq: float, dur: float = 2.2, vel: float = 0.45) -> np.ndarray:
    """Karplus-Strong physically-modeled plucked Nordic Kravik-Lyre string."""
    n = int(SR * dur)
    delay = max(4, int(round(SR / freq)))
    # Warm finger-pluck excitation (filtered burst)
    exc = RNG.standard_normal(delay)
    b, a = signal.butter(2, min(0.85, (freq * 6.5) / (SR * 0.5)), btype="low")
    ring = signal.lfilter(b, a, exc)
    ring -= np.mean(ring)
    ring /= np.max(np.abs(ring)) + 1e-9

    out = np.zeros(n, dtype=np.float64)
    damp = 0.9965 if freq < 260 else 0.9945
    pos = 0
    prev = ring[-1]
    for i in range(n):
        cur = ring[pos]
        out[i] = cur
        nxt = damp * (0.52 * cur + 0.48 * prev)
        ring[pos] = nxt
        prev = cur
        pos += 1
        if pos >= delay:
            pos = 0

    # Wooden lyre soundboard warmth
    b1, a1 = signal.butter(2, [190 / (SR * 0.5), 1100 / (SR * 0.5)], btype="band")
    body = signal.lfilter(b1, a1, out) * 0.42
    env = np.minimum(1.0, np.arange(n) / (SR * 0.008)) * np.exp(-np.linspace(0, dur, n) / (dur * 0.52))
    return (out * 0.72 + body) * env * vel


def synth_tagelharpa_note(freq: float, dur: float, vel: float = 0.42, octave: bool = False) -> np.ndarray:
    """Gritty bowed Nordic horsehair Tagelharpa with wooden-body formants & rosin friction."""
    n = int(SR * dur)
    t = np.linspace(0.0, dur, n, endpoint=False)
    # Human bow vibrato
    vib = 1.0 + 0.0085 * np.sin(2.0 * np.pi * 5.3 * t) * np.minimum(1.0, t / 0.14)
    ph1 = 2.0 * np.pi * np.cumsum(freq * vib) / SR
    ph2 = 2.0 * np.pi * np.cumsum(freq * 1.0042 * vib) / SR
    ph3 = 2.0 * np.pi * np.cumsum(freq * 0.9958 * vib) / SR
    # Rich bowed sawtooth spectrum via harmonic series (band-limited, zero aliasing!)
    raw = np.zeros(n, dtype=np.float64)
    max_h = min(14, int((SR * 0.44) / freq))
    for k in range(1, max_h + 1):
        amp = (1.0 / (k ** 0.92)) * (1.0 if k % 2 == 1 else 0.78)
        raw += amp * (np.sin(k * ph1) + 0.55 * np.sin(k * ph2) + 0.55 * np.sin(k * ph3))
    if octave and freq * 2 < SR * 0.42:
        ph_o = 2.0 * np.pi * np.cumsum(freq * 2.0 * vib) / SR
        for k in range(1, min(7, int((SR * 0.44) / (freq * 2))) + 1):
            raw += (0.36 / k) * np.sin(k * ph_o)
    raw /= np.max(np.abs(raw)) + 1e-9

    # Horsehair rosin friction noise layer
    rosin = RNG.standard_normal(n)
    br, ar = signal.butter(2, [1600 / (SR * 0.5), 4200 / (SR * 0.5)], btype="band")
    rosin = signal.lfilter(br, ar, rosin) * 0.14
    driven = np.tanh((raw + rosin) * 1.85)

    # 3-band wooden Tagelharpa body resonance formants
    body = np.zeros(n, dtype=np.float64)
    for fc, bw, gain in [(320, 180, 0.55), (640, 260, 0.48), (1450, 520, 0.42), (2650, 800, 0.24)]:
        lo = max(40, fc - bw * 0.5) / (SR * 0.5)
        hi = min(SR * 0.48, fc + bw * 0.5) / (SR * 0.5)
        bf, af = signal.butter(2, [lo, hi], btype="band")
        body += signal.lfilter(bf, af, driven) * gain

    # Bowed envelope: slightly raspy bow-catch attack + firm sustain + clean release
    atk = max(1, int(SR * 0.032))
    rel = max(1, int(SR * min(0.09, dur * 0.35)))
    env = np.ones(n, dtype=np.float64)
    env[:atk] = np.linspace(0.0, 1.0, atk) ** 0.8
    env[-rel:] = np.linspace(1.0, 0.0, rel) ** 1.3
    return body * env * vel


def synth_drone_bed(total_samples: int, root_hz: float = 73.42, vel: float = 0.22, dark: bool = True) -> np.ndarray:
    """Seamless deep bowed open-fifth Tagelharpa / Hurdy-Gurdy drone (D2 + A2)."""
    t = np.linspace(0.0, total_samples / SR, total_samples, endpoint=False)
    # Exact integer cycles per loop so drone wraps with ZERO click!
    dur = total_samples / SR
    l = np.zeros(total_samples, dtype=np.float64)
    r = np.zeros(total_samples, dtype=np.float64)
    for base, amp, pan in [(root_hz, 1.0, 0.0), (root_hz * 1.4983, 0.46, -0.35), (root_hz * 2.0, 0.24, 0.35)]:
        cycles = round(base * dur)
        f_exact = cycles / dur
        ph = 2.0 * np.pi * f_exact * t
        s = np.zeros(total_samples, dtype=np.float64)
        for k in range(1, 9):
            s += (1.0 / (k ** (1.25 if dark else 1.0))) * np.sin(k * ph + k * 0.3)
        # Slow organic breathing modulation (exact 2 or 3 cycles per loop)
        mod = 0.78 + 0.22 * np.sin(2.0 * np.pi * (2.0 / dur) * t + (1.2 if pan > 0 else 0.0))
        s *= mod * amp
        gl, gr = pan_gains(pan)
        l += s * gl
        r += s * gr
    b, a = signal.butter(2, (360 if dark else 540) / (SR * 0.5), btype="low")
    l = signal.lfilter(b, a, l)
    r = signal.lfilter(b, a, r)
    peak = max(np.max(np.abs(l)), np.max(np.abs(r)), 1e-9)
    return np.column_stack([l / peak, r / peak]) * vel


def synth_lur_horn(freq: float, dur: float, vel: float = 0.38) -> np.ndarray:
    """Deep bronze Nordic Lur war-horn brass swell."""
    n = int(SR * dur)
    t = np.linspace(0.0, dur, n, endpoint=False)
    vib = 1.0 + 0.005 * np.sin(2.0 * np.pi * 4.1 * t)
    s = np.zeros(n, dtype=np.float64)
    for det in [0.996, 1.0, 1.0045]:
        ph = 2.0 * np.pi * np.cumsum(freq * det * vib) / SR
        for k in range(1, 10):
            s += (1.0 / (k ** 0.85)) * np.sin(k * ph)
    s /= np.max(np.abs(s)) + 1e-9
    # Swelling warm brass filter approximated by crossfading dark & bright filtered copies
    b_dark, a_dark = signal.butter(2, 280 / (SR * 0.5), btype="low")
    b_bright, a_bright = signal.butter(2, 1150 / (SR * 0.5), btype="low")
    dark = signal.lfilter(b_dark, a_dark, s)
    bright = signal.lfilter(b_bright, a_bright, s)
    swell = np.sin(np.pi * t / dur) ** 0.75
    tone = dark * (1.0 - swell * 0.68) + bright * (swell * 0.68)
    env = np.minimum(1.0, t / 0.28) * np.minimum(1.0, (dur - t) / 0.45)
    return np.tanh(tone * 1.4) * env * vel


def master_and_save(buf: np.ndarray, ir: np.ndarray, wet: float, path: Path, target_peak: float = 0.86):
    """Apply circular hall reverb, sub-tightening HPF, warm tape saturation & export 16-bit WAV."""
    mixed = circular_reverb(buf, ir, wet=wet)
    # Tight 28 Hz sub-rumble filter with circular padding so loop seam is click-free
    b_hp, a_hp = signal.butter(2, 28 / (SR * 0.5), btype="high")
    pad = min(SR * 2, mixed.shape[0] // 2)
    for ch in range(2):
        padded = np.concatenate([mixed[-pad:, ch], mixed[:, ch]])
        mixed[:, ch] = signal.lfilter(b_hp, a_hp, padded)[pad:]
    # Gentle analog tape saturation + loudness normalization
    rms = float(np.sqrt(np.mean(mixed ** 2)) + 1e-9)
    mixed = np.tanh((mixed / (rms * 3.8)) * 1.15)
    peak = float(np.max(np.abs(mixed)) + 1e-9)
    mixed = mixed * (target_peak / peak)

    pcm = np.clip(mixed * 32767.0, -32768, 32767).astype(np.int16)
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(2)
        wf.setsampwidth(2)
        wf.setframerate(SR)
        wf.writeframes(pcm.tobytes())
    print(f"Wrote {path.name:20s}  {path.stat().st_size / 1024:7.1f} KB  ({buf.shape[0] / SR:.2f}s @ {SR}Hz stereo)")


# ---------------- Track 1: Relaxed Nordic Space Drift (86 BPM, 12 Bars) ----------------

def build_calm(ir: np.ndarray):
    bpm = 86.0
    beat = 60.0 / bpm
    step = beat / 4.0
    bars = 12
    total_sec = bars * 4 * beat
    n = int(round(total_sec * SR))
    buf = synth_drone_bed(n, root_hz=73.42, vel=0.22, dark=True)

    # D-Dorian / D-Minor Kravik-Lyre phrases across 12 bars (3 distinct 4-bar movements)
    phrases = [
        # Bars 0..3: Quiet open-fifth stargazing
        [(0, 293.66, -0.35), (6, 220.00, 0.30), (10, 261.63, -0.20), (16, 293.66, 0.35), (22, 349.23, -0.30), (28, 329.63, 0.25)],
        [(0, 293.66, -0.30), (4, 329.63, 0.20), (10, 349.23, -0.35), (16, 220.00, 0.32), (24, 261.63, -0.25), (28, 220.00, 0.20)],
        # Bars 4..7: Deeper melodic drift with upper D4 shimmer
        [(0, 293.66, -0.40), (6, 349.23, 0.35), (12, 392.00, -0.25), (16, 440.00, 0.40), (22, 392.00, -0.30), (28, 349.23, 0.25)],
        [(0, 329.63, -0.35), (6, 293.66, 0.30), (12, 261.63, -0.30), (16, 220.00, 0.35), (24, 196.00, -0.25), (28, 220.00, 0.25)],
        # Bars 8..11: Warm atmospheric resolution back to D
        [(0, 293.66, -0.32), (8, 349.23, 0.32), (14, 329.63, -0.28), (16, 293.66, 0.30), (24, 220.00, -0.35), (28, 261.63, 0.30)],
        [(0, 293.66, -0.25), (6, 220.00, 0.25), (12, 174.61, -0.30), (16, 196.00, 0.30), (24, 220.00, -0.20), (28, 293.66, 0.20)],
    ]

    for block in range(6):
        base_sec = block * 2 * 4 * beat
        for st, freq, pan in phrases[block]:
            s = synth_kravik_lyre(freq, dur=2.6, vel=0.34)
            gl, gr = pan_gains(pan)
            wrap_add(buf, int((base_sec + st * step) * SR), s * gl, s * gr)

        # Distant warm heartbeat shamanic drum & gentle rattle
        for st, vel in [(0, 0.42), (14, 0.24), (16, 0.36), (30, 0.20)]:
            d = synth_war_drum(52.0, dur=0.58, vel=vel)
            wrap_add(buf, int((base_sec + st * step) * SR), d * 0.72, d * 0.72)
        for st, vel, pan in [(8, 0.14, -0.45), (24, 0.16, 0.45)]:
            rt = synth_rattle(vel)
            gl, gr = pan_gains(pan)
            wrap_add(buf, int((base_sec + st * step) * SR), rt * gl, rt * gr)

        # Soft distant Tagelharpa sigh on bars 4 & 8 for rich Nordic warmth
        if block in (2, 4):
            bow = synth_tagelharpa_note(146.83 if block == 2 else 174.61, dur=3.4, vel=0.16)
            wrap_add(buf, int((base_sec + 0.1) * SR), bow * 0.62, bow * 0.78)
        if block == 3:
            horn = synth_lur_horn(73.42, dur=3.8, vel=0.16)
            wrap_add(buf, int((base_sec + 4 * step) * SR), horn * 0.75, horn * 0.75)

    master_and_save(buf, ir, wet=0.40, path=OUT_DIR / "music_calm.wav", target_peak=0.82)


# ---------------- Track 2: Driving Nordic War-Drum Battle Anthem (124 BPM, 16 Bars) ----------------

def build_combat(ir: np.ndarray):
    bpm = 124.0
    beat = 60.0 / bpm
    step = beat / 4.0
    bars = 16
    total_sec = bars * 4 * beat
    n = int(round(total_sec * SR))
    buf = synth_drone_bed(n, root_hz=73.42, vel=0.26, dark=False)

    # Fierce D-Dorian / D-Minor Tagelharpa ostinatos (A/B/C/D evolving intensity)
    ost_a = [
        (0, 146.83, 0.30), (4, 146.83, 0.22), (6, 174.61, 0.24), (8, 196.00, 0.28),
        (10, 174.61, 0.22), (12, 164.81, 0.24), (14, 130.81, 0.22),
        (16, 146.83, 0.30), (20, 146.83, 0.22), (22, 174.61, 0.24), (24, 220.00, 0.30),
        (26, 196.00, 0.22), (28, 174.61, 0.24), (30, 164.81, 0.24),
    ]
    ost_b = [
        (0, 146.83, 0.32), (4, 174.61, 0.24), (6, 196.00, 0.24), (8, 220.00, 0.32),
        (10, 196.00, 0.22), (12, 174.61, 0.24), (14, 164.81, 0.22),
        (16, 146.83, 0.32), (20, 130.81, 0.24), (22, 146.83, 0.24), (24, 174.61, 0.28),
        (26, 164.81, 0.22), (28, 146.83, 0.26), (30, 130.81, 0.22),
    ]

    for block in range(8):  # 8 x 2-bar blocks = 16 bars
        base_sec = block * 32 * step
        high_energy = block >= 4  # Second half ramps up with octave Tagelharpa & Lur war-horns!
        climax = block in (4, 5, 7)

        # 1) Giant Nordic War-Drum Ensemble (relentless RAVNIR gallop)
        drum_steps = [(0, 1.05), (6, 0.82), (10, 0.74), (16, 1.02), (22, 0.84), (26, 0.78), (28, 0.88)]
        if high_energy:
            drum_steps.extend([(4, 0.65), (20, 0.65)])
        for st, vel in drum_steps:
            add_war_drum_ensemble(buf, base_sec + st * step, vel=vel, pitch=55.0 if st % 16 == 0 else 59.0)

        # 2) Stereo Frame-Drum Circle (syncopated L/R 16th tribal rolls)
        for st in range(32):
            if st in (2, 8, 12, 14, 18, 24, 28, 30) or (high_energy and st in (3, 15, 19, 31)):
                pan = -0.56 if (st % 4 < 2) else 0.56
                slap = (st % 8 == 4) or (st in (14, 30))
                pitch = 138.0 if (st % 4 == 2) else 104.0
                fd = synth_frame_drum(pitch_hz=pitch, vel=0.58 if not high_energy else 0.68, slap=slap)
                gl, gr = pan_gains(pan)
                wrap_add(buf, int((base_sec + st * step) * SR), fd * gl, fd * gr)

        # 3) Driving Wood/Antler Rim Clacks & Shamanic Rattles
        for st in (4, 12, 20, 28):
            rk = synth_rim_wood(0.62 if not high_energy else 0.74)
            gl, gr = pan_gains(0.42 if st in (4, 20) else -0.42)
            wrap_add(buf, int((base_sec + st * step) * SR), rk * gl, rk * gr)
        for st in range(0, 32, 2):
            rt = synth_rattle(0.26 if st % 4 == 2 else 0.14)
            gl, gr = pan_gains(-0.30 if st % 4 == 0 else 0.30)
            wrap_add(buf, int((base_sec + st * step) * SR), rt * gl, rt * gr)

        # 4) Driving Bowed Tagelharpa Ostinato (with octave power in second half!)
        ost = ost_a if (block % 2 == 0) else ost_b
        for st, freq, dur_s in ost:
            th = synth_tagelharpa_note(freq, dur=dur_s * 1.05, vel=0.36 if not climax else 0.42, octave=climax)
            # Stereo double-tracked Tagelharpa feel
            wrap_add(buf, int((base_sec + st * step) * SR), th * 0.78, th * 0.62)
            wrap_add(buf, int((base_sec + st * step + 0.008) * SR), th * 0.45, th * 0.64)

        # 5) Plucked Kravik-Lyre aggressive rhythmic counter-accents on bars 2-3 & 6-7
        if block in (2, 3, 6, 7):
            for st, freq, pan in [(0, 293.66, -0.5), (6, 349.23, 0.5), (12, 329.63, -0.45), (16, 293.66, 0.5), (24, 440.0, -0.5), (28, 392.0, 0.45)]:
                lp = synth_kravik_lyre(freq, dur=0.75, vel=0.22)
                gl, gr = pan_gains(pan)
                wrap_add(buf, int((base_sec + st * step) * SR), lp * gl, lp * gr)

        # 6) Epic Bronze Nordic Lur War-Horn swells on section drops (bars 1, 9, 13)
        if block in (0, 4, 6):
            horn_root = 73.42 if block != 6 else 87.31  # D2 -> D2 -> F2 dramatic lift!
            h1 = synth_lur_horn(horn_root, dur=3.2, vel=0.34 if block == 4 else 0.26)
            h2 = synth_lur_horn(horn_root * 1.498, dur=3.2, vel=0.18)
            wrap_add(buf, int(base_sec * SR), h1 * 0.76 + h2 * 0.45, h1 * 0.76 + h2 * 0.65)

        # 7) Thunderous War-Drum Fill at the end of bar 8 & bar 16 leading seamlessly back into bar 1
        if block in (3, 7):
            for idx, st in enumerate([26, 28, 29, 30, 31]):
                add_war_drum_ensemble(buf, base_sec + st * step, vel=0.65 + idx * 0.10, pitch=66.0 - idx * 2.5, spread=0.55)

    master_and_save(buf, ir, wet=0.30, path=OUT_DIR / "music_combat.wav", target_peak=0.88)


# ---------------- Track 3: AETHER Cold Corporate March (112 BPM, D Minor / Phrygian, 16 Bars) ----------------

def filt(x: np.ndarray, btype: str, cutoff, order: int = 2) -> np.ndarray:
    nyq = SR * 0.5
    if isinstance(cutoff, tuple):
        wn = (max(20.0, cutoff[0]) / nyq, min(nyq * 0.96, cutoff[1]) / nyq)
    else:
        wn = np.clip(cutoff / nyq, 0.001, 0.96)
    b, a = signal.butter(order, wn, btype=btype)
    return signal.lfilter(b, a, x)


def synth_march_snare(vel: float = 0.85, accent: bool = False, flam: bool = False) -> np.ndarray:
    """Dark, cold military parade snare: lower steel-shell resonance + tight dry snare wires."""
    dur = 0.25 if accent else 0.16
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR
    # Lower, colder steel-shell body (172 Hz + 298 Hz)
    p_env = 1.0 + 0.30 * np.exp(-t * 85.0)
    shell = (
        0.78 * np.sin(2 * np.pi * 172.0 * np.cumsum(p_env) / SR) * np.exp(-t * 25.0)
        + 0.34 * np.sin(2 * np.pi * 298.0 * t) * np.exp(-t * 34.0)
    )
    # Darker, sharper snare wires (1.1 kHz - 5.4 kHz — menacing, never tinny)
    noise = RNG.standard_normal(n)
    wires = filt(noise, "bandpass", (1100.0, 5400.0), order=2) * np.exp(-t * (15.0 if accent else 23.0))
    stick = filt(RNG.standard_normal(n), "bandpass", (2400.0, 6200.0), order=2) * np.exp(-t * 130.0)
    hit = 0.56 * shell + (0.92 if accent else 0.70) * wires + 0.38 * stick
    if flam:
        grace = np.zeros(n, dtype=np.float64)
        off = int(0.024 * SR)
        if off < n:
            grace[off:] = hit[:-off]
        hit = 0.46 * hit + 0.94 * grace
    return np.tanh(hit * 1.45) * vel * 0.54


def synth_iron_anvil(vel: float = 0.65) -> np.ndarray:
    """Heavy dark industrial bulkhead / steel strike anchoring AETHER downbeats."""
    dur = 0.75
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR
    clang = (
        0.55 * np.sin(2 * np.pi * 110.0 * t) * np.exp(-t * 9.0)
        + 0.40 * np.sin(2 * np.pi * 233.0 * t) * np.exp(-t * 14.0)
        + 0.32 * np.sin(2 * np.pi * 389.0 * t) * np.exp(-t * 19.0)
        + 0.22 * np.sin(2 * np.pi * 617.0 * t) * np.exp(-t * 26.0)
    )
    impact = filt(RNG.standard_normal(n), "bandpass", (180.0, 1800.0), order=2) * np.exp(-t * 32.0)
    return np.tanh((clang + 0.55 * impact) * 1.4) * vel * 0.42


def synth_aether_braam(freq_hz: float = 36.71, dur: float = 2.6, vel: float = 0.52) -> np.ndarray:
    """Crushing Dune/Blade-Runner-style contrabass brass monolith swell with subtle tritone grit."""
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR
    out = np.zeros(n, dtype=np.float64)
    # Dense detuned contrabass saw-brass stack (D1 + D2 + A2 + subtle Ab2 tritone shadow)
    for mul, amp in [(1.0, 0.95), (1.0035, 0.70), (0.9965, 0.70), (2.0, 0.85), (2.005, 0.55), (3.0, 0.40), (2.828, 0.14)]:
        ph = 2 * np.pi * (freq_hz * mul) * t
        out += amp * (np.sin(ph) + 0.60 * np.sin(2 * ph) + 0.42 * np.sin(3 * ph) + 0.26 * np.sin(4 * ph))
    # Dark brass saturation & low-pass bloom
    out = np.tanh(out * 0.42)
    out = filt(out, "lowpass", 520.0, order=3)
    out = filt(out, "highpass", 30.0, order=2)
    att = np.clip(t / 0.16, 0.0, 1.0) ** 0.75
    rel = np.clip((dur - t) / 0.55, 0.0, 1.0) ** 1.4
    growl = 1.0 + 0.25 * np.exp(-t * 2.2)
    return out * att * rel * growl * vel


def synth_march_roll(dur: float, vel: float = 0.55, crescendo: bool = True) -> np.ndarray:
    """Disciplined military snare buzz-roll leading into march downbeats."""
    n = int(SR * dur)
    out = np.zeros(n, dtype=np.float64)
    stroke_dt = 0.044
    k = 0
    while k * stroke_dt < dur - 0.015:
        frac = (k * stroke_dt) / max(dur, 1e-4)
        v = vel * (0.28 + 0.85 * (frac ** 1.35)) if crescendo else vel * (0.85 - 0.35 * frac)
        s = synth_march_snare(vel=v, accent=False, flam=False)
        idx = int(k * stroke_dt * SR)
        end = min(n, idx + len(s))
        out[idx:end] += s[:end - idx]
        k += 1
    return out


def synth_parade_bass(vel: float = 0.98, pitch: float = 46.0) -> np.ndarray:
    """Crushing taut gran cassa parade bass drum (deep chest impact on beats 1 & 3)."""
    dur = 0.58
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR
    p_sweep = pitch * (1.0 + 0.72 * np.exp(-t * 32.0))
    phase = 2 * np.pi * np.cumsum(p_sweep) / SR
    body = (
        0.90 * np.sin(phase) * np.exp(-t * 5.8)
        + 0.30 * np.sin(phase * 1.58) * np.exp(-t * 13.0)
    )
    beater = filt(RNG.standard_normal(n), "bandpass", (75.0, 420.0), order=2) * np.exp(-t * 38.0)
    return np.tanh((body + 0.45 * beater) * 1.45) * vel * 0.72


def synth_imperial_brass(freq_hz: float, dur: float, vel: float = 0.40, bright: float = 0.8, staccato: bool = False) -> np.ndarray:
    """Dark, ominous low orchestral brass (bass trombones, horns, tuba) with ominous metallic edge."""
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR
    out = np.zeros(n, dtype=np.float64)
    detunes = [-0.0044, -0.0020, 0.0, 0.0021, 0.0046]
    for i, dt_mul in enumerate(detunes):
        f = freq_hz * (1.0 + dt_mul)
        scoop = 1.0 - 0.016 * np.exp(-t * 68.0)
        ph = 2 * np.pi * np.cumsum(f * scoop) / SR
        voice = (
            1.00 * np.sin(ph)
            + 0.76 * np.sin(2 * ph)
            + 0.58 * np.sin(3 * ph)
            + 0.38 * np.sin(4 * ph)
            + 0.24 * np.sin(5 * ph)
            + 0.14 * np.sin(6 * ph)
        )
        out += voice * (0.24 if i == 2 else 0.19)

    cutoff = min(SR * 0.38, max(420.0, freq_hz * (3.2 + 2.2 * bright)))
    out = filt(out, "lowpass", cutoff, order=3)
    out = filt(out, "highpass", max(32.0, freq_hz * 0.60), order=2)
    form = filt(out, "bandpass", (360.0, 1250.0), order=2)
    out = np.tanh((out + 0.42 * bright * form) * 1.32)

    att_s = 0.028 if staccato else 0.062
    rel_s = 0.085 if staccato else 0.18
    att = np.clip(t / att_s, 0.0, 1.0) ** 0.75
    rel = np.clip((dur - t) / rel_s, 0.0, 1.0) ** 1.25
    bite = 1.0 + 0.28 * np.exp(-t * 15.0)
    return out * att * rel * bite * vel


def synth_aether_telemetry_bed(total_samples: int, beat_sec: float) -> np.ndarray:
    """Oppressive dark corporate orbital dread: D1 abyss sub + icy Eb3/D3 minor-second tension whisper."""
    t = np.arange(total_samples, dtype=np.float64) / SR
    sub = (
        0.22 * np.sin(2 * np.pi * 36.71 * t)
        + 0.14 * np.sin(2 * np.pi * 73.42 * t)
        + 0.07 * np.sin(2 * np.pi * 103.83 * t)  # Ab2 tritone darkness
    )
    # Slow suffocating minor-second metallic tension (D3 + Eb3 rubbing softly in cold orbital space)
    swell = 0.45 + 0.55 * np.sin(2 * np.pi * t / (beat_sec * 16.0) - np.pi * 0.5)
    dread = (
        0.042 * np.sin(2 * np.pi * 146.83 * t)
        + 0.036 * np.sin(2 * np.pi * 155.56 * t)  # Eb3 Phrygian tension against D3
        + 0.022 * np.sin(2 * np.pi * 207.65 * t)  # Ab3 tritone chill
    ) * swell
    # Filtered dark wind/hull pressure noise
    rumble = filt(RNG.standard_normal(total_samples), "bandpass", (38.0, 165.0), order=2) * 0.07
    left = sub + rumble + dread * (0.90 + 0.10 * np.sin(2 * np.pi * t / (beat_sec * 8.0)))
    right = sub + rumble * 0.92 + dread * (0.90 - 0.10 * np.sin(2 * np.pi * t / (beat_sec * 8.0)))
    return np.stack([left, right], axis=1)


def build_aether(ir: np.ndarray):
    """Dark, crushing Prussian-inspired D-Minor / Phrygian / Tritone AETHER March (102 BPM, 16 bars)."""
    bpm = 102.0
    beat = 60.0 / bpm
    step = beat / 4.0  # 16th note
    bars = 16
    total_sec = bars * 4 * beat
    n = int(round(total_sec * SR))
    buf = synth_aether_telemetry_bed(n, beat)

    # Low ominous register (D1..Bb3 max — NO bright high trumpets!)
    D1, Eb1, E1, F1, G1, Ab1, A1, Bb1 = 36.71, 38.89, 41.20, 43.65, 49.00, 51.91, 55.00, 58.27
    D2, Eb2, E2, F2, G2, Ab2, A2, Bb2, C3 = 73.42, 77.78, 82.41, 87.31, 98.00, 103.83, 110.00, 116.54, 130.81
    D3, Eb3, E3, F3, G3, Ab3, A3, Bb3 = 146.83, 155.56, 164.81, 174.61, 196.00, 207.65, 220.00, 233.08

    # 1) Crushing Disciplined Military March Percussion + Iron Bulkhead Strikes
    for bar in range(bars):
        b_sec = bar * 16 * step
        heavy = bar >= 8

        # Monolith contrabass BRAAAM on major 4-bar section drops (bars 1, 5, 9, 13)
        if bar in (0, 4, 8, 12):
            br_root = D1 if bar in (0, 8) else (Eb1 if bar == 4 else G1)
            br = synth_aether_braam(freq_hz=br_root, dur=2.9, vel=0.58 if heavy else 0.46)
            wrap_add(buf, int(b_sec * SR), br * 0.78, br * 0.78)

        # Heavy Gran Cassa Parade Bass Drum on beats 1 & 3 + dark iron anvil on beat 1
        for st, vel in [(0, 1.05), (8, 0.94)]:
            bd = synth_parade_bass(vel=vel if heavy else vel * 0.88, pitch=45.0)
            wrap_add(buf, int((b_sec + st * step) * SR), bd * 0.80, bd * 0.80)
        if bar % 2 == 0:
            anv = synth_iron_anvil(vel=0.62 if heavy else 0.44)
            wrap_add(buf, int(b_sec * SR), anv * 0.68, anv * 0.56)
        if bar in (3, 7, 11, 15):
            bd_pick = synth_parade_bass(vel=0.82, pitch=49.0)
            wrap_add(buf, int((b_sec + 14 * step) * SR), bd_pick * 0.76, bd_pick * 0.76)

        # Cold Military Snare Corps (strict Prussian march cadence, darker & tighter)
        snare_hits = [
            (0, 0.70, True, True),
            (4, 0.86, True, False),
            (7, 0.54, False, False),
            (8, 0.78, True, False),
            (12, 0.90, True, True),
            (14, 0.56, False, False),
        ]
        if heavy:
            snare_hits.extend([(2, 0.44, False, False), (10, 0.48, False, False), (15, 0.62, False, False)])
        for st, vel, acc, flm in snare_hits:
            s_l = synth_march_snare(vel=vel, accent=acc, flam=flm)
            s_r = synth_march_snare(vel=vel * 0.95, accent=acc, flam=flm)
            wrap_add(buf, int((b_sec + st * step) * SR), s_l * 0.76, s_l * 0.36)
            wrap_add(buf, int((b_sec + st * step + 0.004) * SR), s_r * 0.36, s_r * 0.76)

        if bar % 2 == 1:
            roll_dur = 4 * step if bar in (3, 7, 11, 15) else 2 * step
            roll_start = b_sec + (12 * step if bar in (3, 7, 11, 15) else 14 * step)
            rl = synth_march_roll(roll_dur, vel=0.66 if heavy else 0.52, crescendo=True)
            wrap_add(buf, int(roll_start * SR), rl * 0.72, rl * 0.64)

    # 2) Heavy Marching Contrabass Tuba Pedal (D Phrygian / Tritone dread)
    bass_bars = [
        (D2, A1), (D2, Eb1 * 2), (D2, Ab1 * 2), (A1, E1),
        (Eb2, Bb1), (D2, A1), (Ab1 * 2, G1), (A1, A1),
        (D2, A1), (Eb2, Bb1), (Bb1, Ab1 * 2), (A1, E1),
        (G1, D2), (Eb2, Ab1 * 2), (A1, Eb2), (D2, A1),
    ]
    for bar, (n1, n2) in enumerate(bass_bars):
        b_sec = bar * 16 * step
        heavy = bar >= 8
        for st, note in [(0, n1), (8, n2)]:
            tb = synth_imperial_brass(note, dur=6.8 * step, vel=0.42 if heavy else 0.35, bright=0.38, staccato=True)
            wrap_add(buf, int((b_sec + st * step) * SR), tb * 0.76, tb * 0.76)
        pk = synth_imperial_brass(n2, dur=2.2 * step, vel=0.28, bright=0.42, staccato=True)
        wrap_add(buf, int((b_sec + 14 * step) * SR), pk * 0.72, pk * 0.72)

    # 3) Dark Low Trombone & Horn Stabs on Off-beats (hollow fifths & Phrygian/tritone intervals)
    chord_bars = [
        (D2, A2), (D2, Eb3), (D2, Ab2), (A2, E3),
        (Eb2, Bb2), (D2, A2), (Ab2, D3), (A2, E3),
        (D2, A2), (Eb2, Bb2), (Bb2, F3), (A2, Eb3),
        (G2, D3), (Eb2, Ab2), (A2, E3), (D2, A2),
    ]
    for bar, (lo, hi) in enumerate(chord_bars):
        b_sec = bar * 16 * step
        heavy = bar >= 8
        for st in (4, 12):
            c1 = synth_imperial_brass(lo, dur=2.5 * step, vel=0.24 if heavy else 0.19, bright=0.58, staccato=True)
            c2 = synth_imperial_brass(hi, dur=2.5 * step, vel=0.19 if heavy else 0.14, bright=0.62, staccato=True)
            wrap_add(buf, int((b_sec + st * step) * SR), (c1 + c2) * 0.64, (c1 + c2) * 0.46)
            wrap_add(buf, int((b_sec + st * step + 0.005) * SR), (c1 + c2) * 0.42, (c1 + c2) * 0.66)

    # 4) Low, Sinister AETHER Brass Theme (growling trombones & dark horns — Phrygian Eb & Tritone Ab!)
    melody = [
        # Bars 0..3: Cold low trombone patrol theme (D2..A2)
        (0, 6, D2 * 2, 0.36, False), (6, 2, Eb3, 0.34, True), (8, 4, D3, 0.36, True), (12, 4, A2, 0.34, True),
        (16, 6, Bb2, 0.36, False), (22, 2, A2, 0.32, True), (24, 8, Ab2, 0.38, False),
        (32, 6, G2, 0.36, False), (38, 2, F2, 0.32, True), (40, 4, Eb2, 0.36, True), (44, 4, D2, 0.36, True),
        (48, 12, A2, 0.38, False),
        # Bars 4..7: Suffocating Phrygian descent
        (64, 6, Eb3, 0.38, False), (70, 2, D3, 0.34, True), (72, 4, Bb2, 0.36, True), (76, 4, A2, 0.34, True),
        (80, 6, Ab2, 0.38, False), (86, 2, G2, 0.32, True), (88, 8, D2 * 2, 0.36, False),
        (96, 4, F3, 0.36, True), (100, 4, Eb3, 0.36, True), (104, 4, D3, 0.36, True), (108, 4, Ab2, 0.38, True),
        (112, 10, A2, 0.40, False), (124, 4, A2, 0.34, True),
        # Bars 8..15: Crushing Imperial Hegemony — unison low horns + sub-octave bass trombones!
        (128, 6, D3, 0.40, False), (134, 2, Eb3, 0.36, True), (136, 4, F3, 0.38, True), (140, 4, D3, 0.38, True),
        (144, 6, Eb3, 0.40, False), (150, 2, D3, 0.36, True), (152, 8, Bb2, 0.40, False),
        (160, 6, Ab2, 0.40, False), (166, 2, A2, 0.36, True), (168, 4, Bb2, 0.38, True), (172, 4, G2, 0.36, True),
        (176, 12, A2, 0.42, False), (188, 4, Eb3, 0.36, True),
        (192, 6, D3, 0.40, False), (198, 2, Bb2, 0.36, True), (200, 4, Ab2, 0.42, True), (204, 4, G2, 0.38, True),
        (208, 6, Eb3, 0.40, False), (214, 2, D3, 0.36, True), (216, 8, Ab2, 0.40, False),
        (224, 4, A2, 0.42, True), (228, 4, Bb2, 0.40, True), (232, 4, Eb3, 0.40, True), (236, 4, A2, 0.42, True),
        (240, 14, D2 * 2, 0.44, False),
    ]
    for st, d_st, freq, vel, stac in melody:
        is_climax = st >= 128
        lead = synth_imperial_brass(freq, dur=d_st * step * 1.06, vel=vel, bright=0.82 if is_climax else 0.62, staccato=stac)
        sub_b = synth_imperial_brass(freq * 0.5, dur=d_st * step * 1.06, vel=vel * (0.82 if is_climax else 0.52), bright=0.45, staccato=stac)
        combined = lead + sub_b
        gl, gr = pan_gains(-0.12 if is_climax else 0.08)
        wrap_add(buf, int(st * step * SR), combined * gl, combined * gr)

    master_and_save(buf, ir, wet=0.34, path=OUT_DIR / "music_aether.wav", target_peak=0.88)


if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser(description="Render physically-modeled Space 2601AE music loops.")
    ap.add_argument("--only", nargs="*", choices=["calm", "combat", "aether"], help="Render only selected track(s).")
    args = ap.parse_args()
    targets = set(args.only) if args.only else {"calm", "combat", "aether"}

    print("Rendering stereo hall impulse response...")
    hall_ir = make_hall_ir(seconds=2.0, decay=2.6)
    if "calm" in targets:
        build_calm(hall_ir)
    if "combat" in targets:
        build_combat(hall_ir)
    if "aether" in targets:
        build_aether(hall_ir)
    print("Done.")
