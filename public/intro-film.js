(() => {
  'use strict';

  const BPM = 114;
  const BEAT = 60 / BPM; // ~0.526s

  const SCENES = [
    {
      at: 0.0,
      kind: 'narrate',
      mood: 'archive',
      status: 'RT-9 · SIGNAL LOST 2034',
      bg: '/assets/research/rt9-labor.webp',
      kicker: 'ARCHIV 14.03.2034 · RELAIS RT-9 · SIGNAL LOST',
      line: 'Zwei Körper. Eine Lizenz. Ein Konzern, der nicht zahlt.',
      sub: 'Im G5-Sturm sparte AETHER an einer einzigen Kennung. 567 Jahre später gehört ihnen der ganze Sektor.',
      accent: '#78dbe8',
      cues: [
        { at: 0.15, url: '/assets/sfx/scanner_ping.ogg', vol: 0.36, pan: -0.2 },
        { at: 1.85, url: '/assets/sfx/ecm_jam_0.ogg', vol: 0.34, pan: 0.25 },
        { at: 3.45, url: '/assets/sfx/jump_charge.ogg', vol: 0.56, pan: 0.0 },
      ],
    },
    {
      at: 4.6,
      kind: 'narrate',
      mood: 'warp',
      status: 'SPRUNG · 2601 AE',
      live3d: true,
      shot3d: 0,
      bg: '/assets/menu-backdrop.webp',
      kicker: 'REGISTERJAHR 2601 AE · ÄUSSERER GÜRTEL',
      line: 'Keine SIN. Kein Sauerstoff.',
      sub: 'Wer nicht zahlt, fliegt ohne Netz. Wer draußen stirbt, funkt als synthetischer Puffer weiter.',
      accent: '#f0b84c',
      slam: true,
      hardShake: true,
      cues: [
        { at: 0.02, url: '/assets/sfx/thrusterFire_000.ogg', vol: 0.52, pan: 0.0 },
        { at: 0.05, url: '/assets/sfx/deep_thunder.mp3', vol: 0.62, pan: 0.0 },
      ],
    },
    {
      at: 9.0,
      kind: 'card',
      mood: 'freeze',
      status: 'SIN: TERMINIERT · 200 CR',
      live3d: true,
      shot3d: 1,
      freezeAfter: 0.68,
      bg: '/assets/docks/kestrel-ore.webp',
      kicker: 'DEIN SCHIFF · OFFIZIELL VERSCHROTTET',
      title: 'KESTREL-04',
      tagline: '200 Credits, eine gestohlene Kennung und scharfe Rohre. Zeit für Ärger.',
      accent: '#78dbe8',
      slam: true,
      voice: '/assets/sfx/story_halden_blackbox.mp3',
      voiceMax: 3.5,
      cues: [
        { at: 0.64, url: '/assets/sfx/target_lock.ogg', vol: 0.55, pan: 0.0, shock: true },
      ],
    },
    {
      at: 13.2,
      kind: 'card',
      mood: 'sparks',
      status: 'KESTREL · SCHICHTFUNK',
      bg: '/assets/docks/kestrel-ore.webp',
      subject: '/assets/portraits/voss.jpg',
      kicker: 'BERGBAU-DEPOT KESTREL · DISPATCHER',
      title: '„OLD MAN“ VOSS',
      tagline: '„Schau nicht auf den Monitor. Vertrau nur deinem Radar.“',
      accent: '#e59c5c',
      slam: true,
      voice: '/assets/sfx/story_act1_voss.mp3',
      voiceMax: 3.3,
    },
    {
      at: 16.9,
      kind: 'card',
      mood: 'sparks',
      status: 'DRIFT-9 · FIXER-BAND',
      bg: '/assets/docks/bazaar.webp',
      subject: '/assets/portraits/kipper.jpg',
      kicker: 'FREIHAFEN DRIFT-9 · SIN-FIXER',
      title: 'KIPPER TARO',
      tagline: 'Lötet dir eine neue Identität aus Altmetall. Garantie erlischt an der Luftschleuse.',
      accent: '#63e2d4',
      slam: true,
      voice: '/assets/sfx/story_kipper_drift.mp3',
      voiceMax: 3.2,
    },
    {
      at: 20.4,
      kind: 'card',
      mood: 'archive',
      status: 'THETA · ANALOGBAND RT-9',
      bg: '/assets/docks/aether.webp',
      subject: '/assets/portraits/mara_lind.jpg',
      kicker: 'RELAIS THETA · ANALOGARCHIV 2034',
      title: 'DR. MARA LIND',
      tagline: '„Vertrau keiner Stimme ohne Rauschen. Rauschen heißt, da atmet noch jemand.“',
      accent: '#9bc8f5',
      slam: true,
      voice: '/assets/sfx/story_act2_lind.mp3',
      voiceMax: 3.3,
    },
    {
      at: 24.0,
      kind: 'narrate',
      mood: 'combat',
      status: 'GEFECHTSALARM · WAFFEN SCHARF',
      live3d: true,
      shot3d: 2,
      bg: '/assets/ships/yard-bomber.webp',
      kicker: 'NEUN SEKTOREN · BLOCKADEN · WRACKFELDER · KOPFGELD',
      line: 'Berge Erz. Brich die Blockade. Überleb den Gürtel.',
      sub: 'Fünf Schiffe · Forschungslabor RT-9 · Auto-Turrets, Raketen & Schmuggler-Routen.',
      accent: '#ff8a3d',
      slam: true,
      hardShake: true,
      cues: [
        { at: 0.02, url: '/assets/sfx/afterburner.ogg', vol: 0.45, pan: 0.0 },
        { at: 0.42, url: '/assets/sfx/laser_player.mp3', vol: 0.42, pan: -0.45 },
        { at: 0.82, url: '/assets/sfx/laser_player.mp3', vol: 0.42, pan: 0.45 },
        { at: 1.24, url: '/assets/sfx/laser_heavy_0.ogg', vol: 0.46, pan: -0.3 },
        { at: 1.56, url: '/assets/sfx/explosion.mp3', vol: 0.56, pan: 0.35, shock: true },
        { at: 1.60, url: '/assets/sfx/explosionCrunch_000.ogg', vol: 0.44, pan: 0.35 },
        { at: 2.25, url: '/assets/sfx/laser_player.mp3', vol: 0.42, pan: -0.4 },
        { at: 2.65, url: '/assets/sfx/laser_heavy_0.ogg', vol: 0.48, pan: 0.4 },
        { at: 3.08, url: '/assets/sfx/explosion.mp3', vol: 0.58, pan: -0.25, shock: true },
      ],
    },
    {
      at: 28.2,
      kind: 'narrate',
      mood: 'alert',
      status: 'WARNUNG · AETHER GRACE PERIOD',
      bg: '/assets/raumverkehr.webp',
      kicker: 'SPERRZONE MORGENLICHT · AETHER GRACE PERIOD & TARIFF VII',
      line: 'Sie nennen es Ordnung.',
      sub: 'Zollkreuzer am Tor. Autonome Drohnen im Nebel. Und ein Direktor, der nicht sterben kann.',
      accent: '#ff4b3e',
      slam: true,
      hardShake: true,
      cues: [
        { at: 0.04, url: '/assets/sfx/capital_horn.mp3', vol: 0.85, pan: 0.0, shock: true },
        { at: 0.08, url: '/assets/sfx/deep_thunder.mp3', vol: 0.72, pan: 0.0 },
        { at: 0.55, url: '/assets/sfx/capital_alarm.mp3', vol: 0.30, pan: -0.35 },
        { at: 2.25, url: '/assets/sfx/capital_alarm.mp3', vol: 0.26, pan: 0.35 },
      ],
    },
    {
      at: 32.4,
      kind: 'card',
      mood: 'alert',
      status: 'PLATIN-VOLLSTRECKUNG · KLON #7',
      bg: '/assets/docks/bastion.webp',
      subject: '/assets/portraits/kaelen_vance.jpg',
      kicker: 'AETHER GRACE PERIOD · PLATIN-SIN · KLON #7',
      title: 'DIREKTOR VANCE',
      tagline: 'Für ihn ist der Tod ein Wartungsfenster – und dein Schiff eine Abschreibung.',
      accent: '#e6493d',
      slam: true,
      hardShake: true,
      voice: '/assets/sfx/story_vance_taunt.mp3',
      voiceMax: 3.6,
      cues: [
        { at: 0.02, url: '/assets/sfx/macro_cannon_0.ogg', vol: 0.58, pan: 0.0 },
      ],
    },
    {
      at: 36.6,
      kind: 'finale',
      mood: 'finale',
      status: 'COCKPIT BEREIT',
      live3d: true,
      shot3d: 4,
      bg: '/assets/ships/yard-bomber.webp',
      kicker: 'SIGNAL LOST III · 2034 → 2601 AE',
      title: 'SPACE 2601AE',
      tagline: 'KEINE LIZENZ. KEIN BACKUP. FLIEG, SOLANGE ES RAUSCHT.',
      accent: '#f0bc52',
      slam: true,
      hardShake: true,
      finaleChord: true,
      cues: [
        { at: 0.02, url: '/assets/sfx/explosion.mp3', vol: 0.52, pan: 0.0, shock: true },
        { at: 0.35, url: '/assets/sfx/afterburner.ogg', vol: 0.42, pan: 0.0 },
      ],
    },
  ];

  const DURATION = 42.6;
  const AUDIO_URLS = [
    '/assets/sfx/radio_squelch.ogg',
    '/assets/sfx/scanner_ping.ogg',
    '/assets/sfx/ecm_jam_0.ogg',
    '/assets/sfx/jump_charge.ogg',
    '/assets/sfx/thrusterFire_000.ogg',
    '/assets/sfx/deep_thunder.mp3',
    '/assets/sfx/target_lock.ogg',
    '/assets/sfx/afterburner.ogg',
    '/assets/sfx/laser_player.mp3',
    '/assets/sfx/laser_heavy_0.ogg',
    '/assets/sfx/explosion.mp3',
    '/assets/sfx/explosionCrunch_000.ogg',
    '/assets/sfx/capital_horn.mp3',
    '/assets/sfx/capital_alarm.mp3',
    '/assets/sfx/macro_cannon_0.ogg',
    '/assets/sfx/story_halden_blackbox.mp3',
    '/assets/sfx/story_act1_voss.mp3',
    '/assets/sfx/story_kipper_drift.mp3',
    '/assets/sfx/story_act2_lind.mp3',
    '/assets/sfx/story_vance_taunt.mp3',
  ];

  // Dynamic Space-Blues-Rock & Cinematic SFX WebAudio Engine
  class BluesGroove {
    constructor() {
      this.ctx = null;
      this.master = null;
      this.musicBus = null;
      this.sfxBus = null;
      this.guitarSend = null;
      this.radioBus = null;
      this.radioIn = null;
      this.analyser = null;
      this.waveData = null;
      this.driveCurve = null;
      this.buffers = new Map();
      this.activeVoice = null;
      this.customTrack = null;
      this.usingCustomTrack = false;
      this.muted = false;
      this.timer = null;
      this.step = 0;
      this.nextTime = 0;
    }

    tryLoadCustomTrack(startOffsetSec) {
      if (!this.ctx || !this.musicBus) return;
      const ctx = this.ctx;
      const tryUrls = ['/assets/sfx/intro_theme.mp3', '/assets/sfx/intro_theme.ogg'];
      const next = (idx) => {
        if (idx >= tryUrls.length || this.ctx !== ctx) return;
        fetch(tryUrls[idx])
          .then((r) => (r.ok ? r.arrayBuffer() : null))
          .then((ab) => (ab && this.ctx === ctx ? ctx.decodeAudioData(ab) : null))
          .then((buf) => {
            if (!buf || this.ctx !== ctx) return next(idx + 1);
            this.usingCustomTrack = true;
            if (this.timer !== null) { clearInterval(this.timer); this.timer = null; }
            const src = ctx.createBufferSource();
            src.buffer = buf;
            src.loop = true;
            src.connect(this.musicBus);
            src.start(ctx.currentTime + 0.02, startOffsetSec % buf.duration);
            this.customTrack = src;
          })
          .catch(() => next(idx + 1));
      };
      next(0);
    }

    savedMaster() {
      try {
        const raw = localStorage.getItem('space2601.audio');
        if (!raw) return 1.0;
        const v = JSON.parse(raw);
        return typeof v.master === 'number' ? Math.max(0.15, Math.min(1, v.master)) : 1.0;
      } catch {
        return 1.0;
      }
    }

    makeDistortion(amount) {
      const n = 512;
      const curve = new Float32Array(n);
      for (let i = 0; i < n; i++) {
        const x = (i * 2) / n - 1;
        curve[i] = ((3 + amount) * x * 20 * (Math.PI / 180)) / (Math.PI + amount * Math.abs(x));
      }
      return curve;
    }

    makeImpulse(ctx, seconds) {
      const len = Math.floor(ctx.sampleRate * seconds);
      const buf = ctx.createBuffer(2, len, ctx.sampleRate);
      for (let ch = 0; ch < 2; ch++) {
        const d = buf.getChannelData(ch);
        for (let i = 0; i < len; i++) {
          d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / len, 2.5);
        }
      }
      return buf;
    }

    preloadAudio() {
      if (!this.ctx) return;
      const ctx = this.ctx;
      for (const url of AUDIO_URLS) {
        if (this.buffers.has(url)) continue;
        this.buffers.set(url, null);
        fetch(url)
          .then((r) => (r.ok ? r.arrayBuffer() : null))
          .then((ab) => (ab && this.ctx === ctx ? ctx.decodeAudioData(ab) : null))
          .then((buf) => { if (buf && this.ctx === ctx) this.buffers.set(url, buf); })
          .catch(() => {});
      }
    }

    start(startOffsetSec = 0) {
      const Ctx = window.AudioContext || window.webkitAudioContext;
      if (!Ctx) return;
      this.ctx = new Ctx();
      if (this.ctx.state === 'suspended') this.ctx.resume().catch(() => {});

      const comp = this.ctx.createDynamicsCompressor();
      comp.threshold.value = -11;
      comp.ratio.value = 5.5;

      this.master = this.ctx.createGain();
      this.master.gain.value = this.muted ? 0 : 0.48 * this.savedMaster();

      this.musicBus = this.ctx.createGain();
      this.musicBus.gain.value = 1.0;
      this.musicBus.connect(this.master);

      this.sfxBus = this.ctx.createGain();
      this.sfxBus.gain.value = 0.92;
      this.sfxBus.connect(this.master);

      // Dusty space slapback delay + stereo room reverb
      const delay = this.ctx.createDelay(0.4);
      delay.delayTime.value = 0.165;
      const delayFb = this.ctx.createGain();
      delayFb.gain.value = 0.25;
      const delayTone = this.ctx.createBiquadFilter();
      delayTone.type = 'lowpass';
      delayTone.frequency.value = 1950;
      this.guitarSend = this.ctx.createGain();
      this.guitarSend.gain.value = 0.36;
      this.guitarSend.connect(delay);
      delay.connect(delayTone);
      delayTone.connect(delayFb);
      delayFb.connect(delay);
      delayTone.connect(this.musicBus);

      if (typeof this.ctx.createConvolver === 'function') {
        const rev = this.ctx.createConvolver();
        rev.buffer = this.makeImpulse(this.ctx, 1.45);
        const revLp = this.ctx.createBiquadFilter();
        revLp.type = 'lowpass';
        revLp.frequency.value = 2200;
        const revGain = this.ctx.createGain();
        revGain.gain.value = 0.34;
        this.guitarSend.connect(rev);
        rev.connect(revLp);
        revLp.connect(revGain);
        revGain.connect(this.musicBus);
      }

      // Cockpit radio voice bus (360-2850 Hz bandpass)
      const radioHp = this.ctx.createBiquadFilter();
      radioHp.type = 'highpass';
      radioHp.frequency.value = 360;
      const radioLp = this.ctx.createBiquadFilter();
      radioLp.type = 'lowpass';
      radioLp.frequency.value = 2850;
      this.radioBus = this.ctx.createGain();
      this.radioBus.gain.value = 1.2;
      radioHp.connect(radioLp);
      radioLp.connect(this.radioBus);
      this.radioBus.connect(this.master);
      this.radioIn = radioHp;

      this.analyser = this.ctx.createAnalyser();
      this.analyser.fftSize = 128;
      this.analyser.smoothingTimeConstant = 0.70;
      this.waveData = new Uint8Array(this.analyser.frequencyBinCount);
      this.master.connect(comp);
      comp.connect(this.analyser);
      this.analyser.connect(this.ctx.destination);

      this.driveCurve = this.makeDistortion(42);
      this.step = Math.floor(startOffsetSec / (BEAT / 4));
      this.nextTime = this.ctx.currentTime + 0.04;
      this.usingCustomTrack = false;
      this.preloadAudio();
      this.tryLoadCustomTrack(startOffsetSec);
      if (startOffsetSec < 1.0) this.playOpeningSwell(this.nextTime);
      this.timer = window.setInterval(() => this.schedule(), 36);
    }

    syncToTime(sec) {
      this.stopVoice();
      this.step = Math.floor(sec / (BEAT / 4));
      if (this.ctx) this.nextTime = this.ctx.currentTime + 0.04;
    }

    stopVoice() {
      if (this.activeVoice) {
        try { this.activeVoice.stop(); } catch {}
        this.activeVoice = null;
      }
      if (this.musicBus && this.ctx) {
        this.musicBus.gain.setTargetAtTime(1.0, this.ctx.currentTime, 0.08);
      }
    }

    playSfx(url, vol = 0.5, pan = 0.0) {
      if (!this.ctx || !this.sfxBus || !url) return;
      const buf = this.buffers.get(url);
      if (!buf) return;
      const src = this.ctx.createBufferSource();
      src.buffer = buf;
      const g = this.ctx.createGain();
      g.gain.value = vol;
      src.connect(g);
      if (pan && Math.abs(pan) > 0.01 && typeof this.ctx.createStereoPanner === 'function') {
        const p = this.ctx.createStereoPanner();
        p.pan.value = Math.max(-0.8, Math.min(0.8, pan));
        g.connect(p);
        p.connect(this.sfxBus);
      } else {
        g.connect(this.sfxBus);
      }
      src.start(this.ctx.currentTime + 0.01);
    }

    playVoice(url, maxSec = 3.5) {
      if (!this.ctx || !this.radioIn || !url) return;
      this.stopVoice();
      const squelch = this.buffers.get('/assets/sfx/radio_squelch.ogg');
      const clip = this.buffers.get(url);
      const t = this.ctx.currentTime + 0.03;
      if (squelch) {
        const s = this.ctx.createBufferSource();
        s.buffer = squelch;
        const sg = this.ctx.createGain();
        sg.gain.value = 0.28;
        s.connect(sg);
        sg.connect(this.radioIn);
        s.start(t);
      }
      if (!clip) return;
      const dur = Math.min(clip.duration, maxSec);
      const src = this.ctx.createBufferSource();
      src.buffer = clip;
      const g = this.ctx.createGain();
      g.gain.setValueAtTime(0.001, t + 0.08);
      g.gain.linearRampToValueAtTime(1.0, t + 0.14);
      g.gain.setValueAtTime(1.0, t + 0.08 + Math.max(0.2, dur - 0.36));
      g.gain.exponentialRampToValueAtTime(0.001, t + 0.08 + dur);
      src.connect(g);
      g.connect(this.radioIn);
      src.start(t + 0.08, 0, dur + 0.05);
      this.activeVoice = src;
      if (this.musicBus) {
        this.musicBus.gain.setTargetAtTime(0.42, t, 0.07);
        this.musicBus.gain.setTargetAtTime(1.0, t + dur, 0.16);
      }
    }

    playOpeningSwell(t) {
      if (!this.ctx || !this.musicBus) return;
      for (const [freq, vol] of [[36.71, 0.32], [55.0, 0.18], [73.42, 0.20]]) {
        const osc = this.ctx.createOscillator();
        const lp = this.ctx.createBiquadFilter();
        const g = this.ctx.createGain();
        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(freq, t);
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(120, t);
        lp.frequency.linearRampToValueAtTime(620, t + 4.2);
        lp.frequency.exponentialRampToValueAtTime(180, t + 5.2);
        g.gain.setValueAtTime(0.001, t);
        g.gain.linearRampToValueAtTime(vol, t + 1.4);
        g.gain.setValueAtTime(vol, t + 4.3);
        g.gain.exponentialRampToValueAtTime(0.001, t + 5.3);
        osc.connect(lp);
        lp.connect(g);
        g.connect(this.musicBus);
        if (this.guitarSend) g.connect(this.guitarSend);
        osc.start(t);
        osc.stop(t + 5.4);
      }
    }

    finaleChord() {
      if (!this.ctx || !this.musicBus) return;
      this.stopVoice();
      const t = this.ctx.currentTime + 0.02;
      for (const [freq, vol] of [[36.71, 0.30], [73.42, 0.28], [110.0, 0.20], [146.83, 0.22], [174.61, 0.16], [220.0, 0.16], [293.66, 0.12]]) {
        const osc = this.ctx.createOscillator();
        const lp = this.ctx.createBiquadFilter();
        const g = this.ctx.createGain();
        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(freq, t);
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(2200, t);
        lp.frequency.exponentialRampToValueAtTime(240, t + 5.2);
        g.gain.setValueAtTime(0.001, t);
        g.gain.linearRampToValueAtTime(vol, t + 0.06);
        g.gain.exponentialRampToValueAtTime(0.001, t + 5.4);
        osc.connect(lp);
        lp.connect(g);
        g.connect(this.musicBus);
        if (this.guitarSend) g.connect(this.guitarSend);
        osc.start(t);
        osc.stop(t + 5.5);
      }
    }

    setMuted(m) {
      this.muted = !!m;
      if (this.master && this.ctx) {
        this.master.gain.setTargetAtTime(this.muted ? 0 : 0.48 * this.savedMaster(), this.ctx.currentTime, 0.03);
      }
    }

    stop() {
      if (this.timer !== null) {
        clearInterval(this.timer);
        this.timer = null;
      }
      if (this.customTrack) {
        try { this.customTrack.stop(); } catch {}
        this.customTrack = null;
      }
      this.usingCustomTrack = false;
      this.stopVoice();
      if (this.ctx) {
        const ctx = this.ctx;
        if (this.master) {
          try { this.master.gain.setTargetAtTime(0.0001, ctx.currentTime, 0.04); } catch {}
        }
        this.ctx = null;
        this.master = null;
        this.musicBus = null;
        this.sfxBus = null;
        this.guitarSend = null;
        this.radioBus = null;
        this.radioIn = null;
        this.analyser = null;
        this.waveData = null;
        setTimeout(() => ctx.close().catch(() => {}), 140);
      }
    }

    sampleWave() {
      if (!this.analyser || !this.waveData) return { energy: 0, bins: null };
      this.analyser.getByteTimeDomainData(this.waveData);
      let sum = 0;
      for (let i = 0; i < this.waveData.length; i++) {
        const d = (this.waveData[i] - 128) / 128;
        sum += d * d;
      }
      return { energy: Math.min(1, Math.sqrt(sum / this.waveData.length) * 3.4), bins: this.waveData };
    }

    kick(t, accent = 1.0) {
      const ctx = this.ctx;
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(148, t);
      osc.frequency.exponentialRampToValueAtTime(38, t + 0.13);
      gain.gain.setValueAtTime(1.0 * accent, t);
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.25);
      osc.connect(gain);
      gain.connect(this.musicBus);
      osc.start(t);
      osc.stop(t + 0.27);
    }

    clap(t, accent = 1.0) {
      const ctx = this.ctx;
      const len = Math.floor(ctx.sampleRate * 0.14);
      const buf = ctx.createBuffer(1, len, ctx.sampleRate);
      const data = buf.getChannelData(0);
      for (let i = 0; i < len; i++) data[i] = (Math.random() * 2 - 1) * Math.exp(-i / (ctx.sampleRate * 0.032));
      const src = ctx.createBufferSource();
      src.buffer = buf;
      const bp = ctx.createBiquadFilter();
      bp.type = 'bandpass';
      bp.frequency.value = 1520;
      bp.Q.value = 0.85;
      const g = ctx.createGain();
      g.gain.setValueAtTime(0.6 * accent, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + 0.13);
      src.connect(bp);
      bp.connect(g);
      g.connect(this.musicBus);
      src.start(t);
    }

    hat(t, accent = 0.22) {
      const ctx = this.ctx;
      const len = Math.floor(ctx.sampleRate * 0.045);
      const buf = ctx.createBuffer(1, len, ctx.sampleRate);
      const data = buf.getChannelData(0);
      for (let i = 0; i < len; i++) data[i] = (Math.random() * 2 - 1) * Math.exp(-i / (ctx.sampleRate * 0.01));
      const src = ctx.createBufferSource();
      src.buffer = buf;
      const hp = ctx.createBiquadFilter();
      hp.type = 'highpass';
      hp.frequency.value = 6800;
      const g = ctx.createGain();
      g.gain.setValueAtTime(accent, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + 0.04);
      src.connect(hp);
      hp.connect(g);
      g.connect(this.musicBus);
      src.start(t);
    }

    bass(t, freq, dur, vol = 0.46) {
      const ctx = this.ctx;
      const osc = ctx.createOscillator();
      const lp = ctx.createBiquadFilter();
      const g = ctx.createGain();
      osc.type = 'sawtooth';
      osc.frequency.setValueAtTime(freq, t);
      lp.type = 'lowpass';
      lp.frequency.setValueAtTime(480, t);
      lp.frequency.exponentialRampToValueAtTime(150, t + dur);
      g.gain.setValueAtTime(vol, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + dur);
      osc.connect(lp);
      lp.connect(g);
      g.connect(this.musicBus);
      osc.start(t);
      osc.stop(t + dur + 0.02);
    }

    slideGuitar(t, fromHz, toHz, dur, vol = 0.25) {
      const ctx = this.ctx;
      const osc1 = ctx.createOscillator();
      const osc2 = ctx.createOscillator();
      osc1.type = 'sawtooth';
      osc2.type = 'triangle';
      osc1.frequency.setValueAtTime(fromHz, t);
      osc2.frequency.setValueAtTime(fromHz * 1.005, t);
      if (toHz !== fromHz) {
        osc1.frequency.exponentialRampToValueAtTime(toHz, t + Math.min(0.10, dur * 0.45));
        osc2.frequency.exponentialRampToValueAtTime(toHz * 1.005, t + Math.min(0.10, dur * 0.45));
      }
      const shaper = ctx.createWaveShaper();
      shaper.curve = this.driveCurve;
      const cab = ctx.createBiquadFilter();
      cab.type = 'bandpass';
      cab.frequency.value = 1220;
      cab.Q.value = 0.7;
      const g = ctx.createGain();
      g.gain.setValueAtTime(0.001, t);
      g.gain.linearRampToValueAtTime(vol, t + 0.01);
      g.gain.exponentialRampToValueAtTime(0.001, t + dur);
      osc1.connect(shaper);
      osc2.connect(shaper);
      shaper.connect(cab);
      cab.connect(g);
      g.connect(this.musicBus);
      if (this.guitarSend) g.connect(this.guitarSend);
      osc1.start(t);
      osc2.start(t);
      osc1.stop(t + dur + 0.03);
      osc2.stop(t + dur + 0.03);
    }

    slamStinger(hard = false) {
      if (!this.ctx || !this.musicBus) return;
      const t = this.ctx.currentTime + 0.01;
      const osc = this.ctx.createOscillator();
      const g = this.ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(hard ? 132 : 112, t);
      osc.frequency.exponentialRampToValueAtTime(28, t + (hard ? 0.46 : 0.34));
      g.gain.setValueAtTime(hard ? 1.0 : 0.85, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + (hard ? 0.52 : 0.40));
      osc.connect(g);
      g.connect(this.musicBus);
      osc.start(t);
      osc.stop(t + 0.55);
      this.slideGuitar(t, 146.83, 146.83, hard ? 0.55 : 0.42, hard ? 0.30 : 0.25);
      this.slideGuitar(t, 220.0, 220.0, hard ? 0.55 : 0.42, hard ? 0.24 : 0.19);
      if (hard) this.slideGuitar(t, 293.66, 293.66, 0.46, 0.16);
    }

    schedule() {
      if (!this.ctx || !this.musicBus) return;
      const stepDur = BEAT / 4;
      const riff = {
        0: [146.83, 146.83, 0.32],
        3: [164.81, 174.61, 0.20],
        6: [196.0, 207.65, 0.24],
        8: [220.0, 220.0, 0.34],
        11: [196.0, 174.61, 0.22],
        14: [146.83, 146.83, 0.22],
        16: [146.83, 146.83, 0.30],
        19: [174.61, 196.0, 0.20],
        22: [261.63, 293.66, 0.28],
        26: [220.0, 196.0, 0.22],
        28: [174.61, 146.83, 0.38],
      };
      const bassNotes = {
        0: 73.42, 6: 73.42, 8: 87.31, 12: 98.0,
        16: 73.42, 22: 110.0, 24: 98.0, 28: 87.31,
      };
      while (this.nextTime < this.ctx.currentTime + 0.14) {
        const s = this.step % 32;
        const sec = this.step * stepDur;
        const swing = (this.step % 2 === 1) ? stepDur * 0.14 : 0;
        const t = this.nextTime + swing;

        const coldOpen = sec < 4.55;
        const actionOverdrive = sec >= 24.0 && sec < 28.15;
        const warhornDrop = sec >= 28.15 && sec < 29.65;
        const menaceStomp = sec >= 29.65 && sec < 36.55;
        const outroRing = sec >= 36.55;

        if (!outroRing && !warhornDrop) {
          if (coldOpen) {
            if (s === 0 || s === 16) this.kick(t, 0.45);
            if (riff[s] && (s === 0 || s === 8 || s === 22)) {
              const [f0, f1, d] = riff[s];
              this.slideGuitar(t, f0, f1, d * 1.5, 0.20);
            }
          } else if (actionOverdrive) {
            // Double-time high-adrenaline combat push
            if (s % 4 === 0 || s === 6 || s === 14 || s === 22 || s === 30) this.kick(t, s % 8 === 0 ? 1.05 : 0.78);
            if (s % 8 === 4 || s === 15 || s === 31) this.clap(t, s % 8 === 4 ? 1.0 : 0.55);
            this.hat(t, s % 2 === 0 ? 0.26 : 0.16);
            if (bassNotes[s]) this.bass(t, bassNotes[s], stepDur * 2.4, 0.52);
            if (riff[s]) {
              const [f0, f1, d] = riff[s];
              this.slideGuitar(t, f0, f1, d * 0.9, 0.28);
              if (s % 8 === 0) this.slideGuitar(t, f0 * 2, f1 * 2, d * 0.75, 0.14);
            }
          } else if (menaceStomp) {
            // Heavy half-time AETHER menace stomp
            if (s % 8 === 0 || s === 14 || s === 30) this.kick(t, 1.05);
            if (s === 8 || s === 24) this.clap(t, 1.0);
            if (s % 4 === 0) this.hat(t, 0.20);
            if (s === 0 || s === 16) {
              this.bass(t, 73.42, stepDur * 6.0, 0.54);
              this.slideGuitar(t, 146.83, 146.83, 0.48, 0.28);
            } else if (s === 12 || s === 28) {
              this.bass(t, 65.41, stepDur * 3.5, 0.48);
              this.slideGuitar(t, 130.81, 146.83, 0.34, 0.24);
            }
          } else {
            // Core Space-Blues-Rock driving groove
            if (s % 8 === 0 || s === 6 || s === 22) this.kick(t, s % 8 === 0 ? 1.0 : 0.74);
            if (s % 8 === 4) this.clap(t, 0.95);
            if (s % 2 === 0) this.hat(t, s % 4 === 2 ? 0.26 : 0.14);
            if (bassNotes[s]) this.bass(t, bassNotes[s], stepDur * 3.0);
            if (riff[s]) {
              const [f0, f1, d] = riff[s];
              this.slideGuitar(t, f0, f1, d, 0.24);
            }
          }
        }
        this.nextTime += stepDur;
        this.step++;
      }
    }
  }

  function formatTimecode(sec) {
    const s = Math.max(0, Math.floor(sec));
    const f = Math.floor((sec - s) * 24);
    return `00:${String(s).padStart(2, '0')}:${String(f).padStart(2, '0')}`;
  }

  function initIntro() {
    const overlay = document.getElementById('intro-film');
    const launchBtn = document.getElementById('menu-intro');
    if (!overlay || !launchBtn) return;

    const bgA = document.getElementById('intro-bg-a');
    const bgB = document.getElementById('intro-bg-b');
    const fxCanvas = document.getElementById('intro-fx');
    const fxCtx = fxCanvas ? fxCanvas.getContext('2d') : null;
    const waveCanvas = document.getElementById('intro-wave');
    const waveCtx = waveCanvas ? waveCanvas.getContext('2d') : null;
    const timecodeEl = document.getElementById('intro-timecode');
    const statusEl = document.getElementById('intro-status');
    const subjectWrap = document.getElementById('intro-subject-wrap');
    const subjectEl = document.getElementById('intro-subject');
    const flashEl = document.getElementById('intro-flash');
    const cardEl = document.getElementById('intro-card');
    const kickerEl = document.getElementById('intro-kicker');
    const titleEl = document.getElementById('intro-title');
    const taglineEl = document.getElementById('intro-tagline');
    const playCta = document.getElementById('intro-play-cta');
    const captionEl = document.getElementById('intro-caption');
    const captionLine = document.getElementById('intro-caption-line');
    const captionSub = document.getElementById('intro-caption-sub');
    const barEl = document.getElementById('intro-progress');
    const chaptersEl = document.getElementById('intro-chapters');
    const skipBtn = document.getElementById('intro-skip');
    const muteBtn = document.getElementById('intro-mute');

    let playing = false;
    let rafId = 0;
    let startAt = 0;
    let lastFrameAt = 0;
    let currentIndex = -1;
    let sceneStartedAt = 0;
    let activeBg = 0;
    let activeAccent = '#e4b158';
    let activeMood = 'sparks';
    let flarePulse = 0;
    let combatSpawnTimer = 0;
    let pendingCues = [];

    const groove = new BluesGroove();
    const reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    const particles = Array.from({ length: 82 }, () => ({
      x: Math.random(),
      y: Math.random(),
      vx: -0.02 - Math.random() * 0.055,
      vy: -0.008 - Math.random() * 0.028,
      r: 0.9 + Math.random() * 2.2,
      a: 0.25 + Math.random() * 0.65,
      ember: Math.random() < 0.48,
    }));

    const warpStars = Array.from({ length: 110 }, () => ({
      angle: Math.random() * Math.PI * 2,
      dist: 0.03 + Math.random() * 0.72,
      speed: 0.55 + Math.random() * 1.45,
      size: 1.0 + Math.random() * 1.8,
    }));

    const bolts = [];
    const rings = [];

    // Build clickable chapter indicators
    const chapterButtons = [];
    if (chaptersEl) {
      chaptersEl.innerHTML = '';
      SCENES.forEach((sc, i) => {
        const b = document.createElement('button');
        b.type = 'button';
        b.className = 'intro-chapter';
        b.textContent = String(i + 1).padStart(2, '0');
        b.setAttribute('aria-label', `Szene ${i + 1}`);
        b.addEventListener('click', () => jumpToScene(i));
        chaptersEl.appendChild(b);
        chapterButtons.push(b);
      });
    }

    function clearCues() {
      for (const id of pendingCues) clearTimeout(id);
      pendingCues = [];
    }

    function triggerShock(hard = false) {
      flarePulse = Math.min(1.2, flarePulse + (hard ? 1.0 : 0.65));
      rings.push({ x: 0.5 + (Math.random() - 0.5) * 0.28, y: 0.5 + (Math.random() - 0.5) * 0.22, r: 0.02, alpha: 0.95, color: activeAccent });
      if (flashEl) {
        flashEl.classList.remove('is-flash');
        void flashEl.offsetWidth;
        flashEl.classList.add('is-flash');
      }
      overlay.classList.remove('is-shake', 'is-shake-hard');
      void overlay.offsetWidth;
      overlay.classList.add(hard ? 'is-shake-hard' : 'is-shake');
    }

    function spawnCombatBolt() {
      const playerSide = Math.random() < 0.52;
      const fromLeft = Math.random() < 0.5;
      const x = fromLeft ? -0.04 : 1.04;
      const y = playerSide ? (0.58 + Math.random() * 0.34) : (0.14 + Math.random() * 0.34);
      const tx = fromLeft ? 1.08 : -0.08;
      const ty = playerSide ? (0.18 + Math.random() * 0.32) : (0.55 + Math.random() * 0.35);
      const dx = tx - x;
      const dy = ty - y;
      const len = Math.hypot(dx, dy) || 1;
      const speed = 2.1 + Math.random() * 1.4;
      bolts.push({
        x, y,
        vx: (dx / len) * speed,
        vy: (dy / len) * speed,
        color: playerSide ? '#6ef3ff' : '#ff4b3e',
        life: 0.72,
      });
    }

    function resizeFx() {
      if (!fxCanvas) return;
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const w = Math.max(1, Math.floor(window.innerWidth * dpr));
      const h = Math.max(1, Math.floor(window.innerHeight * dpr));
      if (fxCanvas.width !== w || fxCanvas.height !== h) {
        fxCanvas.width = w;
        fxCanvas.height = h;
      }
    }

    function drawFx(dt, energy, elapsed) {
      if (fxCtx && fxCanvas) {
        resizeFx();
        const w = fxCanvas.width;
        const h = fxCanvas.height;
        const cx = w * 0.5;
        const cy = h * 0.5;
        fxCtx.clearRect(0, 0, w, h);

        // 1) Warp radial hyperspace streaks
        if (activeMood === 'warp') {
          fxCtx.lineCap = 'round';
          for (const st of warpStars) {
            if (!reduceMotion) {
              st.dist += st.speed * dt * (1.1 + energy * 1.6);
              if (st.dist > 1.05) { st.dist = 0.03 + Math.random() * 0.12; st.angle = Math.random() * Math.PI * 2; }
            }
            const r0 = st.dist * Math.max(w, h) * 0.62;
            const r1 = r0 + (24 + st.dist * 110) * (w / 1280);
            fxCtx.globalAlpha = Math.min(0.9, st.dist * 1.3);
            fxCtx.strokeStyle = st.dist > 0.55 ? activeAccent : '#d7f4ff';
            fxCtx.lineWidth = st.size * (w / 1280);
            fxCtx.beginPath();
            fxCtx.moveTo(cx + Math.cos(st.angle) * r0, cy + Math.sin(st.angle) * r0);
            fxCtx.lineTo(cx + Math.cos(st.angle) * r1, cy + Math.sin(st.angle) * r1);
            fxCtx.stroke();
          }
        }

        // 2) Cold open / RT-9 archive twin-signal trace & CRT glitch bars
        if (activeMood === 'archive') {
          fxCtx.strokeStyle = activeAccent;
          fxCtx.lineWidth = 1.6 * (w / 1280);
          for (let trace = 0; trace < 2; trace++) {
            fxCtx.globalAlpha = trace === 0 ? 0.24 : 0.14;
            fxCtx.beginPath();
            const baseY = h * (0.28 + trace * 0.44);
            for (let x = 0; x <= w; x += Math.max(12, w / 90)) {
              const n = x / w;
              const amp = (12 + energy * 34) * (h / 720);
              const y = baseY + Math.sin(n * 22 + elapsed * (4.2 + trace * 1.7)) * amp * Math.sin(n * Math.PI);
              if (x === 0) fxCtx.moveTo(x, y);
              else fxCtx.lineTo(x, y);
            }
            fxCtx.stroke();
          }
          if (!reduceMotion && Math.random() < 0.12) {
            fxCtx.globalAlpha = 0.12;
            fxCtx.fillStyle = activeAccent;
            const gy = Math.random() * h;
            fxCtx.fillRect(0, gy, w, (2 + Math.random() * 8) * (h / 720));
          }
        }

        // 3) Borderlands freeze-frame radial burst & target lock brackets
        if (activeMood === 'freeze') {
          fxCtx.strokeStyle = activeAccent;
          fxCtx.lineWidth = 2 * (w / 1280);
          fxCtx.globalAlpha = 0.18 + flarePulse * 0.25;
          for (let i = 0; i < 18; i++) {
            const a = (i / 18) * Math.PI * 2 + 0.15;
            fxCtx.beginPath();
            fxCtx.moveTo(cx + Math.cos(a) * Math.min(w, h) * 0.38, cy + Math.sin(a) * Math.min(w, h) * 0.38);
            fxCtx.lineTo(cx + Math.cos(a) * Math.max(w, h) * 0.72, cy + Math.sin(a) * Math.max(w, h) * 0.72);
            fxCtx.stroke();
          }
          const box = Math.min(w, h) * 0.24;
          const arm = box * 0.24;
          fxCtx.globalAlpha = 0.55;
          fxCtx.lineWidth = 2.5 * (w / 1280);
          for (const [sx, sy] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) {
            const bx = cx + sx * box;
            const by = cy + sy * box;
            fxCtx.beginPath();
            fxCtx.moveTo(bx - sx * arm, by);
            fxCtx.lineTo(bx, by);
            fxCtx.lineTo(bx, by - sy * arm);
            fxCtx.stroke();
          }
        }

        // 4) Action combat laser bolts & targeting crosshairs
        if (activeMood === 'combat') {
          if (!reduceMotion) {
            combatSpawnTimer -= dt;
            if (combatSpawnTimer <= 0) {
              spawnCombatBolt();
              combatSpawnTimer = 0.11 + Math.random() * 0.12;
            }
          }
          for (let i = bolts.length - 1; i >= 0; i--) {
            const b = bolts[i];
            b.x += b.vx * dt;
            b.y += b.vy * dt;
            b.life -= dt;
            if (b.life <= 0) { bolts.splice(i, 1); continue; }
            const x1 = b.x * w;
            const y1 = b.y * h;
            const x0 = (b.x - b.vx * 0.055) * w;
            const y0 = (b.y - b.vy * 0.055) * h;
            fxCtx.globalAlpha = Math.min(1, b.life * 2.2);
            fxCtx.strokeStyle = b.color;
            fxCtx.lineWidth = 4.0 * (w / 1280);
            fxCtx.beginPath();
            fxCtx.moveTo(x0, y0);
            fxCtx.lineTo(x1, y1);
            fxCtx.stroke();
            fxCtx.strokeStyle = '#ffffff';
            fxCtx.lineWidth = 1.6 * (w / 1280);
            fxCtx.beginPath();
            fxCtx.moveTo(x0 + (x1 - x0) * 0.35, y0 + (y1 - y0) * 0.35);
            fxCtx.lineTo(x1, y1);
            fxCtx.stroke();
          }
        } else if (bolts.length > 0) {
          bolts.length = 0;
        }

        // 5) Red Alert tactical radar sweep
        if (activeMood === 'alert') {
          const sweepR = Math.min(w, h) * 0.42;
          const ang = elapsed * 1.9;
          fxCtx.strokeStyle = '#ff4b3e';
          fxCtx.globalAlpha = 0.22;
          fxCtx.lineWidth = 1.6 * (w / 1280);
          fxCtx.beginPath();
          fxCtx.arc(cx, cy, sweepR, 0, Math.PI * 2);
          fxCtx.stroke();
          fxCtx.globalAlpha = 0.34;
          fxCtx.beginPath();
          fxCtx.moveTo(cx, cy);
          fxCtx.lineTo(cx + Math.cos(ang) * sweepR, cy + Math.sin(ang) * sweepR);
          fxCtx.stroke();
        }

        // 6) Expanding shockwave rings on explosions / slams
        for (let i = rings.length - 1; i >= 0; i--) {
          const rg = rings[i];
          rg.r += dt * 0.95;
          rg.alpha -= dt * 1.65;
          if (rg.alpha <= 0) { rings.splice(i, 1); continue; }
          fxCtx.globalAlpha = rg.alpha * 0.65;
          fxCtx.strokeStyle = rg.color;
          fxCtx.lineWidth = (3.5 * rg.alpha + 1.0) * (w / 1280);
          fxCtx.beginPath();
          fxCtx.arc(rg.x * w, rg.y * h, rg.r * Math.min(w, h), 0, Math.PI * 2);
          fxCtx.stroke();
        }

        // 7) Kinetic embers / debris particles
        const speedBoost = 1 + energy * 2.1 + flarePulse * 2.8 + (activeMood === 'combat' ? 1.8 : 0);
        for (const p of particles) {
          if (!reduceMotion) {
            p.x += p.vx * dt * speedBoost;
            p.y += p.vy * dt * speedBoost;
            if (p.x < -0.04) { p.x = 1.04; p.y = Math.random(); }
            if (p.y < -0.04) { p.y = 1.04; p.x = Math.random(); }
          }
          const px = p.x * w;
          const py = p.y * h;
          const rad = p.r * (w / 1280) * (1 + energy * 0.6);
          fxCtx.globalAlpha = Math.min(1, p.a * (0.6 + energy * 0.7));
          fxCtx.fillStyle = p.ember ? activeAccent : '#d7eff8';
          fxCtx.beginPath();
          fxCtx.arc(px, py, Math.max(0.8, rad), 0, Math.PI * 2);
          fxCtx.fill();
        }

        // 8) Anamorphic flare streak on beats & slams
        const streak = Math.min(1, energy * 0.45 + flarePulse * 0.95);
        if (streak > 0.05) {
          const grad = fxCtx.createLinearGradient(0, cy, w, cy);
          grad.addColorStop(0, 'transparent');
          grad.addColorStop(0.5, activeAccent);
          grad.addColorStop(1, 'transparent');
          fxCtx.globalAlpha = streak * 0.28;
          fxCtx.fillStyle = grad;
          fxCtx.fillRect(0, cy - 2, w, 4 + streak * 10);
        }
        fxCtx.globalAlpha = 1;
      }
      flarePulse = Math.max(0, flarePulse - dt * 2.5);
    }

    function drawWave(bins) {
      if (!waveCtx || !waveCanvas) return;
      const w = waveCanvas.width;
      const h = waveCanvas.height;
      waveCtx.clearRect(0, 0, w, h);
      waveCtx.strokeStyle = activeAccent;
      waveCtx.lineWidth = 1.5;
      waveCtx.beginPath();
      if (!bins || groove.muted) {
        waveCtx.moveTo(0, h * 0.5);
        waveCtx.lineTo(w, h * 0.5);
      } else {
        const step = w / (bins.length - 1);
        for (let i = 0; i < bins.length; i++) {
          const v = (bins[i] - 128) / 128;
          const y = h * 0.5 + v * (h * 0.46);
          if (i === 0) waveCtx.moveTo(0, y);
          else waveCtx.lineTo(i * step, y);
        }
      }
      waveCtx.stroke();
    }

    function swapBackdrop(url) {
      const next = activeBg === 0 ? bgB : bgA;
      const prev = activeBg === 0 ? bgA : bgB;
      activeBg = 1 - activeBg;
      if (next) {
        next.style.backgroundImage = `url('${url}')`;
        next.classList.remove('is-zooming');
        void next.offsetWidth;
        next.classList.add('is-active', 'is-zooming');
      }
      if (prev) prev.classList.remove('is-active');
    }

    function applyScene(scene, index) {
      clearCues();
      sceneStartedAt = performance.now();
      activeAccent = scene.accent || '#e4b158';
      activeMood = scene.mood || 'sparks';
      overlay.style.setProperty('--intro-accent', activeAccent);
      overlay.setAttribute('data-kind', scene.kind);
      overlay.setAttribute('data-mood', activeMood);
      overlay.setAttribute('data-live3d', scene.live3d ? 'true' : 'false');
      overlay.setAttribute('data-freeze3d', 'false');
      if (statusEl) statusEl.textContent = scene.status || 'SPACE 2601AE';
      chapterButtons.forEach((b, i) => b.setAttribute('aria-current', i === index ? 'step' : 'false'));

      if (!scene.live3d) swapBackdrop(scene.bg);
      else {
        if (bgA) bgA.classList.remove('is-active');
        if (bgB) bgB.classList.remove('is-active');
      }

      if (scene.slam) {
        triggerShock(!!scene.hardShake);
        groove.slamStinger(!!scene.hardShake);
      }

      if (scene.cues && scene.cues.length) {
        for (const cue of scene.cues) {
          const id = window.setTimeout(() => {
            if (!playing) return;
            groove.playSfx(cue.url, cue.vol || 0.5, cue.pan || 0.0);
            if (cue.shock) triggerShock(true);
          }, Math.max(0, Math.round(cue.at * 1000)));
          pendingCues.push(id);
        }
      }

      if (scene.voice) {
        groove.playVoice(scene.voice, scene.voiceMax || 3.5);
      } else {
        groove.stopVoice();
      }
      if (scene.finaleChord) {
        groove.finaleChord();
      }

      if (playCta) playCta.hidden = scene.kind !== 'finale';

      if (scene.kind === 'narrate') {
        cardEl.hidden = true;
        subjectWrap.hidden = true;
        captionEl.hidden = false;
        kickerEl.textContent = '';
        captionEl.querySelector('.intro-cap-kicker').textContent = scene.kicker;
        captionLine.textContent = scene.line;
        captionSub.textContent = scene.sub;
        captionEl.classList.remove('is-in');
        void captionEl.offsetWidth;
        captionEl.classList.add('is-in');
      } else {
        captionEl.hidden = true;
        cardEl.hidden = false;
        kickerEl.textContent = scene.kicker;
        titleEl.textContent = scene.title;
        taglineEl.textContent = scene.tagline;
        cardEl.classList.remove('is-slam');
        void cardEl.offsetWidth;
        cardEl.classList.add('is-slam');

        if (scene.subject) {
          subjectEl.src = scene.subject;
          subjectEl.className = scene.contain ? 'is-contain' : '';
          subjectWrap.hidden = false;
          subjectWrap.classList.remove('is-pop');
          void subjectWrap.offsetWidth;
          subjectWrap.classList.add('is-pop');
        } else {
          subjectWrap.hidden = true;
        }
      }
    }

    function jumpToScene(index) {
      if (!playing) return;
      const clamped = Math.max(0, Math.min(SCENES.length - 1, index));
      const targetSec = SCENES[clamped].at;
      startAt = performance.now() - targetSec * 1000;
      currentIndex = clamped;
      groove.syncToTime(targetSec);
      applyScene(SCENES[clamped], clamped);
    }

    function stopIntro(launchGame = false) {
      if (!playing) return;
      playing = false;
      clearCues();
      cancelAnimationFrame(rafId);
      groove.stop();
      window.introCam = { active: false, freeze: false, time: 0, shot: 0, shotTime: 0 };
      overlay.hidden = true;
      overlay.classList.remove('is-ending', 'is-shake', 'is-shake-hard');
      const menuEl = document.getElementById('flight-menu');
      if (menuEl) menuEl.style.visibility = '';
      if (launchGame) {
        const menuLaunch = document.getElementById('menu-launch');
        if (menuLaunch) menuLaunch.click();
      } else {
        launchBtn.focus();
      }
    }

    function tick(now) {
      if (!playing) return;
      const dt = Math.min(0.05, Math.max(0.001, (now - lastFrameAt) / 1000));
      lastFrameAt = now;
      const elapsed = (now - startAt) / 1000;
      const pct = Math.min(100, (elapsed / DURATION) * 100);
      barEl.style.width = pct.toFixed(2) + '%';
      if (timecodeEl) timecodeEl.textContent = `${formatTimecode(elapsed)} · REC`;

      let idx = 0;
      for (let i = 0; i < SCENES.length; i++) {
        if (elapsed >= SCENES[i].at) idx = i;
      }
      if (idx !== currentIndex) {
        currentIndex = idx;
        applyScene(SCENES[idx], idx);
      }
      const sc = SCENES[idx];
      const shot = typeof sc.shot3d === 'number' ? sc.shot3d : 1;
      const shotTime = Math.max(0, (now - sceneStartedAt) / 1000);
      const frozen = typeof sc.freezeAfter === 'number' ? shotTime >= sc.freezeAfter : !!sc.freeze3d;
      overlay.setAttribute('data-freeze3d', frozen ? 'true' : 'false');

      if (!frozen) {
        window.introCam = { active: !!sc.live3d, freeze: false, time: elapsed, shot, shotTime };
      } else if (window.introCam) {
        window.introCam.active = !!sc.live3d;
        window.introCam.freeze = true;
        window.introCam.shot = shot;
      } else {
        window.introCam = { active: !!sc.live3d, freeze: true, time: elapsed, shot, shotTime };
      }

      const { energy, bins } = groove.sampleWave();
      drawFx(dt, energy, elapsed);
      drawWave(bins);
      if (elapsed >= DURATION - 0.9) {
        overlay.classList.add('is-ending');
      }
      if (elapsed >= DURATION) {
        stopIntro(false);
        return;
      }
      rafId = requestAnimationFrame(tick);
    }

    function startIntro() {
      if (playing) return;
      playing = true;
      currentIndex = -1;
      flarePulse = 0.7;
      bolts.length = 0;
      rings.length = 0;
      for (const sc of SCENES) {
        if (sc.bg) { const im = new Image(); im.src = sc.bg; }
        if (sc.subject) { const im = new Image(); im.src = sc.subject; }
      }
      const menuEl = document.getElementById('flight-menu');
      if (menuEl) menuEl.style.visibility = 'hidden';
      window.introCam = { active: false, freeze: false, time: 0, shot: 0, shotTime: 0 };
      overlay.classList.remove('is-ending', 'is-shake', 'is-shake-hard');
      overlay.hidden = false;
      barEl.style.width = '0%';
      groove.setMuted(false);
      muteBtn.textContent = 'Ton aus';
      groove.start(0);
      startAt = performance.now();
      lastFrameAt = startAt;
      skipBtn.focus();
      rafId = requestAnimationFrame(tick);
    }

    launchBtn.addEventListener('click', startIntro);
    skipBtn.addEventListener('click', () => stopIntro(false));
    if (playCta) playCta.addEventListener('click', () => stopIntro(true));
    muteBtn.addEventListener('click', () => {
      const next = !groove.muted;
      groove.setMuted(next);
      muteBtn.textContent = next ? 'Ton an' : 'Ton aus';
    });

    window.addEventListener('keydown', (e) => {
      if (!playing) return;
      if (e.code === 'Escape' || e.code === 'Space') {
        e.preventDefault();
        e.stopPropagation();
        stopIntro(false);
      } else if (e.code === 'Enter') {
        e.preventDefault();
        e.stopPropagation();
        stopIntro(currentIndex === SCENES.length - 1);
      } else if (e.code === 'ArrowRight') {
        e.preventDefault();
        e.stopPropagation();
        jumpToScene(currentIndex + 1);
      } else if (e.code === 'ArrowLeft') {
        e.preventDefault();
        e.stopPropagation();
        jumpToScene(currentIndex - 1);
      } else if (e.code === 'KeyM') {
        e.preventDefault();
        e.stopPropagation();
        const next = !groove.muted;
        groove.setMuted(next);
        muteBtn.textContent = next ? 'Ton an' : 'Ton aus';
      }
    }, true);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initIntro);
  } else {
    initIntro();
  }
})();
