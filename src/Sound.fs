namespace SpaceEngine

// =====================================================================================
//  Sound.fs  -  Prozeduraler Web-Audio-Synthesizer (ohne externe Audiodateien)
//
//  Alle Klaenge werden zur Laufzeit aus Oszillatoren, Rauschpuffern und Biquad-Filtern
//  synthetisiert:
//    * Kontinuierliches Triebwerks- und Boost-Geraeusch (reagiert auf Schub + Tempo)
//    * Laser-Schuesse (Spieler: heller Dual-Puls, Gegner: tiefer Plasma-Impuls)
//    * Schildtreffer (hochfrequenter Resonanz-Ping) vs. Huellentreffer (dumpfer Schlag)
//    * Asteroiden- und Schiffsexplosionen (gefiltertes Rauschen + Sub-Bass-Drop)
//    * UI-/Wellen-Signale (Zielwechsel, Wellenstart, Welle abgewehrt, Game Over)
// =====================================================================================

open System
open Fable.Core

type SoundEngine =
    { mutable Handle: obj
      mutable Muted: bool }

[<RequireQualifiedAccess>]
module Sound =

    [<Emit("""(() => {
        const Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx) return null;
        let ctx = null;
        let master = null;
        let noiseBuf = null;
        let engOsc1 = null;
        let engOsc2 = null;
        let engNoiseGain = null;
        let engFilter = null;
        let engGain = null;

        function ensure() {
            if (!ctx) {
                ctx = new Ctx();
                master = ctx.createGain();
                master.gain.value = 0.42;
                master.connect(ctx.destination);

                // 2 Sekunden weisses Rauschen fuer Explosionen und Triebwerks-Rauschen
                const sr = ctx.sampleRate;
                noiseBuf = ctx.createBuffer(1, sr * 2, sr);
                const data = noiseBuf.getChannelData(0);
                for (let i = 0; i < data.length; i++) {
                    data[i] = Math.random() * 2 - 1;
                }

                // Kontinuierliches Triebwerk
                engGain = ctx.createGain();
                engGain.gain.value = 0.0;
                engFilter = ctx.createBiquadFilter();
                engFilter.type = "lowpass";
                engFilter.frequency.value = 110;
                engFilter.Q.value = 2.2;

                engOsc1 = ctx.createOscillator();
                engOsc1.type = "sawtooth";
                engOsc1.frequency.value = 48;
                const g1 = ctx.createGain();
                g1.gain.value = 0.35;
                engOsc1.connect(g1);
                g1.connect(engFilter);

                engOsc2 = ctx.createOscillator();
                engOsc2.type = "triangle";
                engOsc2.frequency.value = 72;
                const g2 = ctx.createGain();
                g2.gain.value = 0.25;
                engOsc2.connect(g2);
                g2.connect(engFilter);

                const nSrc = ctx.createBufferSource();
                nSrc.buffer = noiseBuf;
                nSrc.loop = true;
                engNoiseGain = ctx.createGain();
                engNoiseGain.gain.value = 0.08;
                nSrc.connect(engNoiseGain);
                engNoiseGain.connect(engFilter);

                engFilter.connect(engGain);
                engGain.connect(master);

                engOsc1.start();
                engOsc2.start();
                nSrc.start();
            }
            if (ctx.state === "suspended") {
                ctx.resume().catch(() => {});
            }
            return ctx;
        }

        return {
            ensure,
            setMuted(m) {
                if (!master || !ctx) return;
                master.gain.setTargetAtTime(m ? 0.0 : 0.42, ctx.currentTime, 0.03);
            },
            setEngine(active, throttle, boost) {
                if (!ctx || !engGain) return;
                const t = ctx.currentTime;
                if (!active) {
                    engGain.gain.setTargetAtTime(0.0, t, 0.08);
                    return;
                }
                const power = Math.min(1.8, throttle * 0.8 + boost * 1.0);
                engOsc1.frequency.setTargetAtTime(42 + throttle * 32 + boost * 48, t, 0.06);
                engOsc2.frequency.setTargetAtTime(63 + throttle * 48 + boost * 78, t, 0.06);
                engFilter.frequency.setTargetAtTime(95 + throttle * 190 + boost * 520, t, 0.06);
                engNoiseGain.gain.setTargetAtTime(0.04 + boost * 0.28, t, 0.05);
                engGain.gain.setTargetAtTime(0.045 + power * 0.095, t, 0.06);
            },
            laser(isPlayer, atten, overdrive) {
                const c = ensure();
                if (!c || atten <= 0.02) return;
                const t = c.currentTime;
                const osc = c.createOscillator();
                const g = c.createGain();
                osc.type = isPlayer ? "sawtooth" : "square";
                const f0 = isPlayer ? (overdrive ? 980 : 780) : 420;
                const f1 = isPlayer ? 135 : 95;
                osc.frequency.setValueAtTime(f0, t);
                osc.frequency.exponentialRampToValueAtTime(f1, t + (isPlayer ? 0.095 : 0.12));
                const vol = (isPlayer ? 0.14 : 0.09) * Math.min(1.0, atten);
                g.gain.setValueAtTime(vol, t);
                g.gain.exponentialRampToValueAtTime(0.001, t + (isPlayer ? 0.10 : 0.13));
                osc.connect(g);
                g.connect(master);
                osc.start(t);
                osc.stop(t + 0.14);
            },
            hit(shield, isPlayer) {
                const c = ensure();
                if (!c) return;
                const t = c.currentTime;
                const osc = c.createOscillator();
                const g = c.createGain();
                if (shield) {
                    osc.type = "sine";
                    osc.frequency.setValueAtTime(isPlayer ? 520 : 680, t);
                    osc.frequency.exponentialRampToValueAtTime(isPlayer ? 190 : 980, t + 0.14);
                    g.gain.setValueAtTime(isPlayer ? 0.22 : 0.11, t);
                    g.gain.exponentialRampToValueAtTime(0.001, t + 0.15);
                } else {
                    osc.type = "triangle";
                    osc.frequency.setValueAtTime(isPlayer ? 160 : 240, t);
                    osc.frequency.exponentialRampToValueAtTime(42, t + 0.18);
                    g.gain.setValueAtTime(isPlayer ? 0.30 : 0.14, t);
                    g.gain.exponentialRampToValueAtTime(0.001, t + 0.19);
                }
                osc.connect(g);
                g.connect(master);
                osc.start(t);
                osc.stop(t + 0.20);
            },
            explosion(scale, atten) {
                const c = ensure();
                if (!c || !noiseBuf || atten <= 0.02) return;
                const t = c.currentTime;
                const dur = Math.min(1.1, 0.28 + scale * 0.35);
                const src = c.createBufferSource();
                src.buffer = noiseBuf;
                const flt = c.createBiquadFilter();
                flt.type = "lowpass";
                flt.frequency.setValueAtTime(280 + scale * 160, t);
                flt.frequency.exponentialRampToValueAtTime(45, t + dur);
                const g = c.createGain();
                const vol = Math.min(0.45, (0.16 + scale * 0.18) * atten);
                g.gain.setValueAtTime(vol, t);
                g.gain.exponentialRampToValueAtTime(0.001, t + dur);
                src.connect(flt);
                flt.connect(g);
                g.connect(master);
                src.start(t);
                src.stop(t + dur + 0.02);

                // Sub-Bass-Impuls
                const sub = c.createOscillator();
                const sg = c.createGain();
                sub.type = "sine";
                sub.frequency.setValueAtTime(95, t);
                sub.frequency.exponentialRampToValueAtTime(26, t + dur * 0.85);
                sg.gain.setValueAtTime(vol * 0.9, t);
                sg.gain.exponentialRampToValueAtTime(0.001, t + dur * 0.9);
                sub.connect(sg);
                sg.connect(master);
                sub.start(t);
                sub.stop(t + dur);
            },
            cue(kind) {
                const c = ensure();
                if (!c) return;
                const t = c.currentTime;
                const notes =
                    kind === 0 ? [520, 780] :           // Ziel erfasst / gewechselt
                    kind === 1 ? [330, 440, 660] :      // Welle startet
                    kind === 2 ? [440, 554, 659, 880] : // Welle abgewehrt
                                 [300, 235, 175];       // Game Over
                const step = kind === 0 ? 0.045 : 0.09;
                for (let i = 0; i < notes.length; i++) {
                    const osc = c.createOscillator();
                    const g = c.createGain();
                    osc.type = kind === 3 ? "sawtooth" : "triangle";
                    osc.frequency.setValueAtTime(notes[i], t + i * step);
                    g.gain.setValueAtTime(kind === 0 ? 0.07 : 0.12, t + i * step);
                    g.gain.exponentialRampToValueAtTime(0.001, t + i * step + step * 1.9);
                    osc.connect(g);
                    g.connect(master);
                    osc.start(t + i * step);
                    osc.stop(t + i * step + step * 2.0);
                }
            }
        };
    })()""")>]
    let private createBackend () : obj = jsNative

    [<Emit("$0 && $0.ensure()")>]
    let private emitEnsure (_h: obj) : unit = jsNative

    [<Emit("$0 && $0.setMuted($1)")>]
    let private emitMuted (_h: obj) (_m: bool) : unit = jsNative

    [<Emit("$0 && $0.setEngine($1, $2, $3)")>]
    let private emitEngine (_h: obj) (_active: bool) (_throttle: float) (_boost: float) : unit = jsNative

    [<Emit("$0 && $0.laser($1, $2, $3)")>]
    let private emitLaser (_h: obj) (_isPlayer: bool) (_atten: float) (_overdrive: bool) : unit = jsNative

    [<Emit("$0 && $0.hit($1, $2)")>]
    let private emitHit (_h: obj) (_shield: bool) (_isPlayer: bool) : unit = jsNative

    [<Emit("$0 && $0.explosion($1, $2)")>]
    let private emitExplosion (_h: obj) (_scale: float) (_atten: float) : unit = jsNative

    [<Emit("$0 && $0.cue($1)")>]
    let private emitCue (_h: obj) (_kind: int) : unit = jsNative

    let create () : SoundEngine =
        let h =
            try
                createBackend ()
            with _ ->
                null

        { Handle = h; Muted = false }

    let unlock (s: SoundEngine) =
        if not s.Muted then
            try
                emitEnsure s.Handle
            with _ ->
                ()

    let toggleMute (s: SoundEngine) =
        s.Muted <- not s.Muted

        try
            emitMuted s.Handle s.Muted
        with _ ->
            ()

    let updateEngine (s: SoundEngine) (active: bool) (throttle: float) (boost: float) =
        try
            emitEngine s.Handle (active && not s.Muted) throttle boost
        with _ ->
            ()

    let laser (s: SoundEngine) (isPlayer: bool) (distance: float) (overdrive: bool) =
        if not s.Muted then
            let atten = if isPlayer then 1.0 else Math.Max(0.0, 1.0 - distance / 520.0)

            try
                emitLaser s.Handle isPlayer atten overdrive
            with _ ->
                ()

    let hit (s: SoundEngine) (shieldAbsorbed: bool) (isPlayer: bool) =
        if not s.Muted then
            try
                emitHit s.Handle shieldAbsorbed isPlayer
            with _ ->
                ()

    let explosion (s: SoundEngine) (scale: float) (distance: float) =
        if not s.Muted then
            let atten = Math.Max(0.05, 1.0 - distance / 680.0)

            try
                emitExplosion s.Handle scale atten
            with _ ->
                ()

    let targetLock (s: SoundEngine) =
        if not s.Muted then
            try
                emitCue s.Handle 0
            with _ ->
                ()

    let waveStart (s: SoundEngine) =
        if not s.Muted then
            try
                emitCue s.Handle 1
            with _ ->
                ()

    let waveCleared (s: SoundEngine) =
        if not s.Muted then
            try
                emitCue s.Handle 2
            with _ ->
                ()

    let gameOver (s: SoundEngine) =
        if not s.Muted then
            try
                emitCue s.Handle 3
            with _ ->
                ()
