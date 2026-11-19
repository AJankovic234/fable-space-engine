(() => {
  'use strict';

  const BPM = 102;
  const BEAT = 60 / BPM; // ~0.588s

  const SCENES = [
    {
      at: 0.0,
      kind: 'narrate',
      bg: '/assets/docks/aether.webp',
      kicker: 'ARCHIV 14.03.2034 · RELAIS RT-9 · SIGNAL LOST',
      line: '2034 sparte AETHER an einer einzigen Software-Lizenz.',
      sub: 'HERMES 2 verstummte im G5-Sturm. ORPHEUS 7 zerbrach an einer gespiegelten ID. Ein halbes Jahrtausend später nennt man das noch immer Kundenservice.',
      accent: '#78dbe8',
    },
    {
      at: 5.0,
      kind: 'narrate',
      live3d: true,
      shot3d: 0,
      bg: '/assets/menu-backdrop.webp',
      kicker: '567 JAHRE SPÄTER · REGISTERJAHR 2601 AE',
      line: 'Heute gehört ihnen der Sektor. Und deine Atemluft auf Ratenzahlung.',
      sub: 'Keine SIN, kein Sauerstoff. Beschwerden nimmt das Vakuum entgegen.',
      accent: '#e4b158',
      slam: true,
    },
    {
      at: 9.5,
      kind: 'card',
      live3d: true,
      shot3d: 1,
      freeze3d: true,
      bg: '/assets/docks/kestrel-ore.webp',
      kicker: 'DEIN SCHIFF · OFFIZIELL VERSCHROTTET',
      title: 'KESTREL-04',
      tagline: '200 Credits auf dem Konto, gestohlene ID, null Versicherung. Läuft bei dir.',
      accent: '#78dbe8',
      slam: true,
      voice: '/assets/sfx/story_halden_blackbox.mp3',
      voiceMax: 3.4,
    },
    {
      at: 13.5,
      kind: 'card',
      bg: '/assets/docks/helios.webp',
      subject: '/assets/portraits/orlov.webp',
      kicker: 'HELIOS-IV · SCHICHTBÜRO',
      title: 'ORLOV',
      tagline: 'Noch 41 Jahre arbeiten für eine neue Lunge. AETHER nennt das Mitarbeiterbindung.',
      accent: '#e59c5c',
      slam: true,
      voice: '/assets/sfx/story_helios_orlov.mp3',
      voiceMax: 3.4,
    },
    {
      at: 17.5,
      kind: 'card',
      bg: '/assets/docks/aether.webp',
      subject: '/assets/portraits/mara_lind.jpg',
      kicker: 'RELAIS THETA · ANALOGARCHIV 2034',
      title: 'DR. MARA LIND',
      tagline: 'Die Einzige im Sektor, die noch Bänder liest. Alle anderen sind schon Puffer.',
      accent: '#9bc8f5',
      slam: true,
      voice: '/assets/sfx/story_act2_lind.mp3',
      voiceMax: 3.4,
    },
    {
      at: 21.5,
      kind: 'card',
      bg: '/assets/docks/bazaar.webp',
      subject: '/assets/portraits/kipper.jpg',
      kicker: 'DRIFT-9 · FIXER-FREQUENZ',
      title: 'KIPPER TARO',
      tagline: 'Lötet dir eine frische SIN. Garantie erlischt exakt an der Luftschleuse.',
      accent: '#63e2d4',
      slam: true,
      voice: '/assets/sfx/story_kipper_drift.mp3',
      voiceMax: 3.4,
    },
    {
      at: 25.5,
      kind: 'card',
      bg: '/assets/docks/bastion.webp',
      subject: '/assets/portraits/kaelen_vance.jpg',
      kicker: 'AETHER SOVEREIGN · PLATIN-SIN · KLON #7',
      title: 'DIREKTOR VANCE',
      tagline: 'Hält sich für den einzigen Erwachsenen im Raum. Und dich für Sondermüll.',
      accent: '#e65b49',
      slam: true,
      voice: '/assets/sfx/story_vance_taunt.mp3',
      voiceMax: 3.5,
    },
    {
      at: 29.5,
      kind: 'finale',
      live3d: true,
      shot3d: 2,
      bg: '/assets/ships/yard-bomber.webp',
      kicker: 'SIGNAL LOST III · 2034 → 2601 AE',
      title: 'SPACE 2601AE',
      tagline: 'KEIN GELD. KEINE LIZENZ. KEINE RUHE FÜR DIE HALUNKEN.',
      accent: '#f0bc52',
      slam: true,
      finaleChord: true,
    },
  ];

  const DURATION = 35.2;
  const VOICE_URLS = [
    '/assets/sfx/radio_squelch.ogg',
    '/assets/sfx/story_halden_blackbox.mp3',
    '/assets/sfx/story_helios_orlov.mp3',
    '/assets/sfx/story_act2_lind.mp3',
    '/assets/sfx/story_kipper_drift.mp3',
    '/assets/sfx/story_vance_taunt.mp3',
  ];

  // Space-Blues-Rock WebAudio engine with stereo reverb, slapback guitar delay & auto-ducked radio voices
  class BluesGroove {
    constructor() {
      this.ctx = null;
      this.master = null;
      this.musicBus = null;
      this.guitarSend = null;
      this.radioBus = null;
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
          d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / len, 2.6);
        }
      }
      return buf;
    }

    preloadVoices() {
      if (!this.ctx) return;
      const ctx = this.ctx;
      for (const url of VOICE_URLS) {
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
      comp.threshold.value = -12;
      comp.ratio.value = 5;

      this.master = this.ctx.createGain();
      this.master.gain.value = this.muted ? 0 : 0.44 * this.savedMaster();

      this.musicBus = this.ctx.createGain();
      this.musicBus.gain.value = 1.0;
      this.musicBus.connect(this.master);

      // Dusty desert/space slapback delay + stereo room reverb for slide guitar & brass
      const delay = this.ctx.createDelay(0.4);
      delay.delayTime.value = 0.185;
      const delayFb = this.ctx.createGain();
      delayFb.gain.value = 0.24;
      const delayTone = this.ctx.createBiquadFilter();
      delayTone.type = 'lowpass';
      delayTone.frequency.value = 1850;
      this.guitarSend = this.ctx.createGain();
      this.guitarSend.gain.value = 0.36;
      this.guitarSend.connect(delay);
      delay.connect(delayTone);
      delayTone.connect(delayFb);
      delayFb.connect(delay);
      delayTone.connect(this.musicBus);

      if (typeof this.ctx.createConvolver === 'function') {
        const rev = this.ctx.createConvolver();
        rev.buffer = this.makeImpulse(this.ctx, 1.35);
        const revLp = this.ctx.createBiquadFilter();
        revLp.type = 'lowpass';
        revLp.frequency.value = 2100;
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
      this.radioBus.gain.value = 1.15;
      radioHp.connect(radioLp);
      radioLp.connect(this.radioBus);
      this.radioBus.connect(this.master);
      this.radioIn = radioHp;

      this.analyser = this.ctx.createAnalyser();
      this.analyser.fftSize = 128;
      this.analyser.smoothingTimeConstant = 0.72;
      this.waveData = new Uint8Array(this.analyser.frequencyBinCount);
      this.master.connect(comp);
      comp.connect(this.analyser);
      this.analyser.connect(this.ctx.destination);

      this.driveCurve = this.makeDistortion(34);
      this.step = Math.floor(startOffsetSec / (BEAT / 4));
      this.nextTime = this.ctx.currentTime + 0.04;
      this.usingCustomTrack = false;
      this.preloadVoices();
      this.tryLoadCustomTrack(startOffsetSec);
      if (startOffsetSec < 1.0) this.playOpeningSwell(this.nextTime);
      this.timer = window.setInterval(() => this.schedule(), 40);
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
      g.gain.setValueAtTime(1.0, t + 0.08 + Math.max(0.2, dur - 0.38));
      g.gain.exponentialRampToValueAtTime(0.001, t + 0.08 + dur);
      src.connect(g);
      g.connect(this.radioIn);
      src.start(t + 0.08, 0, dur + 0.05);
      this.activeVoice = src;
      if (this.musicBus) {
        this.musicBus.gain.setTargetAtTime(0.46, t, 0.08);
        this.musicBus.gain.setTargetAtTime(1.0, t + dur, 0.18);
      }
    }

    playOpeningSwell(t) {
      if (!this.ctx || !this.musicBus) return;
      for (const [freq, vol] of [[36.71, 0.28], [55.0, 0.16], [73.42, 0.18]]) {
        const osc = this.ctx.createOscillator();
        const lp = this.ctx.createBiquadFilter();
        const g = this.ctx.createGain();
        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(freq, t);
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(135, t);
        lp.frequency.linearRampToValueAtTime(440, t + 4.0);
        lp.frequency.exponentialRampToValueAtTime(160, t + 8.6);
        g.gain.setValueAtTime(0.001, t);
        g.gain.linearRampToValueAtTime(vol, t + 1.8);
        g.gain.setValueAtTime(vol, t + 7.4);
        g.gain.exponentialRampToValueAtTime(0.001, t + 8.8);
        osc.connect(lp);
        lp.connect(g);
        g.connect(this.musicBus);
        if (this.guitarSend) g.connect(this.guitarSend);
        osc.start(t);
        osc.stop(t + 9.0);
      }
    }

    finaleChord() {
      if (!this.ctx || !this.musicBus) return;
      this.stopVoice();
      const t = this.ctx.currentTime + 0.02;
      for (const [freq, vol] of [[73.42, 0.26], [110.0, 0.18], [146.83, 0.20], [174.61, 0.14], [220.0, 0.14]]) {
        const osc = this.ctx.createOscillator();
        const lp = this.ctx.createBiquadFilter();
        const g = this.ctx.createGain();
        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(freq, t);
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(1500, t);
        lp.frequency.exponentialRampToValueAtTime(220, t + 4.6);
        g.gain.setValueAtTime(0.001, t);
        g.gain.linearRampToValueAtTime(vol, t + 0.08);
        g.gain.exponentialRampToValueAtTime(0.001, t + 4.8);
        osc.connect(lp);
        lp.connect(g);
        g.connect(this.musicBus);
        if (this.guitarSend) g.connect(this.guitarSend);
        osc.start(t);
        osc.stop(t + 5.0);
      }
    }

    setMuted(m) {
      this.muted = !!m;
      if (this.master && this.ctx) {
        this.master.gain.setTargetAtTime(this.muted ? 0 : 0.44 * this.savedMaster(), this.ctx.currentTime, 0.03);
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
      return { energy: Math.min(1, Math.sqrt(sum / this.waveData.length) * 3.2), bins: this.waveData };
    }

    kick(t, accent = 1.0) {
      const ctx = this.ctx;
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(138, t);
      osc.frequency.exponentialRampToValueAtTime(40, t + 0.13);
      gain.gain.setValueAtTime(0.95 * accent, t);
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.24);
      osc.connect(gain);
      gain.connect(this.musicBus);
      osc.start(t);
      osc.stop(t + 0.26);
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
      bp.frequency.value = 1480;
      bp.Q.value = 0.9;
      const g = ctx.createGain();
      g.gain.setValueAtTime(0.55 * accent, t);
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

    bass(t, freq, dur) {
      const ctx = this.ctx;
      const osc = ctx.createOscillator();
      const lp = ctx.createBiquadFilter();
      const g = ctx.createGain();
      osc.type = 'triangle';
      osc.frequency.setValueAtTime(freq, t);
      lp.type = 'lowpass';
      lp.frequency.setValueAtTime(420, t);
      lp.frequency.exponentialRampToValueAtTime(160, t + dur);
      g.gain.setValueAtTime(0.42, t);
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
      osc2.frequency.setValueAtTime(fromHz * 1.004, t);
      if (toHz !== fromHz) {
        osc1.frequency.exponentialRampToValueAtTime(toHz, t + Math.min(0.11, dur * 0.45));
        osc2.frequency.exponentialRampToValueAtTime(toHz * 1.004, t + Math.min(0.11, dur * 0.45));
      }
      const shaper = ctx.createWaveShaper();
      shaper.curve = this.driveCurve;
      const cab = ctx.createBiquadFilter();
      cab.type = 'bandpass';
      cab.frequency.value = 1180;
      cab.Q.value = 0.72;
      const g = ctx.createGain();
      g.gain.setValueAtTime(0.001, t);
      g.gain.linearRampToValueAtTime(vol, t + 0.012);
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

    slamStinger() {
      if (!this.ctx || !this.musicBus) return;
      const t = this.ctx.currentTime + 0.01;
      const osc = this.ctx.createOscillator();
      const g = this.ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(112, t);
      osc.frequency.exponentialRampToValueAtTime(30, t + 0.36);
      g.gain.setValueAtTime(0.85, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + 0.42);
      osc.connect(g);
      g.connect(this.musicBus);
      osc.start(t);
      osc.stop(t + 0.44);
      this.slideGuitar(t, 146.83, 146.83, 0.44, 0.26);
      this.slideGuitar(t, 220.0, 220.0, 0.44, 0.20);
    }

    schedule() {
      if (!this.ctx || !this.musicBus) return;
      const stepDur = BEAT / 4;
      const riff = {
        0: [146.83, 146.83, 0.34],
        3: [164.81, 174.61, 0.22],
        6: [196.0, 207.65, 0.26],
        8: [220.0, 220.0, 0.36],
        11: [196.0, 174.61, 0.24],
        14: [146.83, 146.83, 0.24],
        16: [146.83, 146.83, 0.32],
        19: [174.61, 196.0, 0.22],
        22: [261.63, 293.66, 0.30],
        26: [220.0, 196.0, 0.24],
        28: [174.61, 146.83, 0.40],
      };
      const bassNotes = {
        0: 73.42, 6: 73.42, 8: 87.31, 12: 98.0,
        16: 73.42, 22: 110.0, 24: 98.0, 28: 87.31,
      };
      while (this.nextTime < this.ctx.currentTime + 0.14) {
        const s = this.step % 32;
        const swing = (this.step % 2 === 1) ? stepDur * 0.16 : 0;
        const t = this.nextTime + swing;
        const introSolo = this.step < 34; // First ~5s (2034 RT-9 archive): brooding drone + lone slide licks before 2601 AE stomp drops
        const outroRing = this.step >= 200; // Finale (~29.5s): let epic chord ring out cleanly
        if (!outroRing) {
          if (!introSolo) {
            if (s % 8 === 0 || s === 6 || s === 22) this.kick(t, s % 8 === 0 ? 1.0 : 0.72);
            if (s % 8 === 4) this.clap(t, 0.95);
            if (s % 2 === 0) this.hat(t, s % 4 === 2 ? 0.26 : 0.14);
            if (bassNotes[s]) this.bass(t, bassNotes[s], stepDur * 3.2);
          }
          if (riff[s] && (!introSolo || s === 0 || s === 6 || s === 8 || s === 22)) {
            const [f0, f1, d] = riff[s];
            this.slideGuitar(t, f0, f1, introSolo ? d * 1.4 : d, introSolo ? 0.20 : 0.24);
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
    let activeBg = 0;
    let activeAccent = '#e4b158';
    let flarePulse = 0;
    const groove = new BluesGroove();
    const reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const particles = Array.from({ length: 68 }, () => ({
      x: Math.random(),
      y: Math.random(),
      vx: -0.015 - Math.random() * 0.045,
      vy: -0.008 - Math.random() * 0.024,
      r: 0.9 + Math.random() * 2.2,
      a: 0.25 + Math.random() * 0.65,
      ember: Math.random() < 0.42,
    }));

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

    function drawFx(dt, energy) {
      if (fxCtx && fxCanvas) {
        resizeFx();
        const w = fxCanvas.width;
        const h = fxCanvas.height;
        fxCtx.clearRect(0, 0, w, h);
        const speedBoost = 1 + energy * 1.9 + flarePulse * 2.4;
        for (const p of particles) {
          if (!reduceMotion) {
            p.x += p.vx * dt * speedBoost;
            p.y += p.vy * dt * speedBoost;
            if (p.x < -0.04) { p.x = 1.04; p.y = Math.random(); }
            if (p.y < -0.04) { p.y = 1.04; p.x = Math.random(); }
          }
          const px = p.x * w;
          const py = p.y * h;
          const rad = p.r * (w / 1280) * (1 + energy * 0.55);
          fxCtx.globalAlpha = Math.min(1, p.a * (0.65 + energy * 0.65));
          fxCtx.fillStyle = p.ember ? activeAccent : '#d7eff8';
          fxCtx.beginPath();
          fxCtx.arc(px, py, Math.max(0.8, rad), 0, Math.PI * 2);
          fxCtx.fill();
        }
        const streak = Math.min(1, energy * 0.42 + flarePulse * 0.85);
        if (streak > 0.05) {
          const grad = fxCtx.createLinearGradient(0, h * 0.5, w, h * 0.5);
          grad.addColorStop(0, 'transparent');
          grad.addColorStop(0.5, activeAccent);
          grad.addColorStop(1, 'transparent');
          fxCtx.globalAlpha = streak * 0.24;
          fxCtx.fillStyle = grad;
          fxCtx.fillRect(0, h * 0.5 - 2, w, 4 + streak * 8);
        }
        fxCtx.globalAlpha = 1;
      }
      flarePulse = Math.max(0, flarePulse - dt * 2.6);
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
          const y = h * 0.5 + v * (h * 0.44);
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
      activeAccent = scene.accent || '#e4b158';
      overlay.style.setProperty('--intro-accent', activeAccent);
      overlay.setAttribute('data-kind', scene.kind);
      overlay.setAttribute('data-live3d', scene.live3d ? 'true' : 'false');
      overlay.setAttribute('data-freeze3d', scene.freeze3d ? 'true' : 'false');
      chapterButtons.forEach((b, i) => b.setAttribute('aria-current', i === index ? 'step' : 'false'));

      if (!scene.live3d) swapBackdrop(scene.bg);
      else {
        if (bgA) bgA.classList.remove('is-active');
        if (bgB) bgB.classList.remove('is-active');
      }

      if (scene.slam) {
        flarePulse = 1.0;
        flashEl.classList.remove('is-flash');
        void flashEl.offsetWidth;
        flashEl.classList.add('is-flash');
        overlay.classList.remove('is-shake');
        void overlay.offsetWidth;
        overlay.classList.add('is-shake');
        groove.slamStinger();
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
      cancelAnimationFrame(rafId);
      groove.stop();
      window.introCam = { active: false, freeze: false, time: 0 };
      overlay.hidden = true;
      overlay.classList.remove('is-ending', 'is-shake');
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
      if (!sc.freeze3d) {
        window.introCam = { active: !!sc.live3d, freeze: false, time: elapsed, shot };
      } else if (window.introCam) {
        window.introCam.active = true;
        window.introCam.freeze = true;
        window.introCam.shot = shot;
      } else {
        window.introCam = { active: true, freeze: true, time: elapsed, shot };
      }
      const { energy, bins } = groove.sampleWave();
      drawFx(dt, energy);
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
      flarePulse = 0.6;
      // Preload all scene backdrops and portraits immediately to eliminate cut-in flicker
      for (const sc of SCENES) {
        if (sc.bg) { const im = new Image(); im.src = sc.bg; }
        if (sc.subject) { const im = new Image(); im.src = sc.subject; }
      }
      const menuEl = document.getElementById('flight-menu');
      if (menuEl) menuEl.style.visibility = 'hidden';
      window.introCam = { active: false, freeze: false, time: 0, shot: 0 };
      overlay.classList.remove('is-ending', 'is-shake');
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
