(() => {
  'use strict';

  const BPM = 102;
  const BEAT = 60 / BPM; // ~0.588s

  const SCENES = [
    {
      at: 0.0,
      kind: 'narrate',
      bg: '/assets/menu-backdrop.webp',
      kicker: 'ÄUSSERER GÜRTEL · 2601 AE',
      line: 'AETHER Sovereign nennt es den Wohlstandsgürtel.',
      sub: 'Sechs Stationen. Ein Konzern. Volle Kontrolle über jeden Sprungkorridor.',
      accent: '#e4b158',
    },
    {
      at: 4.2,
      kind: 'narrate',
      bg: '/assets/docks/helios.webp',
      kicker: 'SCHMELZWERK HELIOS · BLOCKADE',
      line: 'Die Schichtarbeiter nennen es das Ende der Leitung.',
      sub: 'Zollkreuzer vor dem Tor. Medizinische Filter gesperrt. Geschäft wie immer.',
      accent: '#e59c5c',
      slam: true,
    },
    {
      at: 8.4,
      kind: 'card',
      bg: '/assets/docks/kestrel-ore.webp',
      subject: '/assets/ships/kutter.webp',
      contain: true,
      kicker: 'KESTREL-04 · FREIER KURIER',
      title: 'BERGUNGSKUTTER',
      tagline: 'Ausgemustert. Überfällig. Offiziell seit drei Jahren verschrottet.',
      accent: '#78dbe8',
      slam: true,
    },
    {
      at: 12.2,
      kind: 'card',
      bg: '/assets/docks/helios.webp',
      subject: '/assets/portraits/orlov.webp',
      kicker: 'HELIOS-IV · SCHICHTBÜRO',
      title: 'ORLOV',
      tagline: '„Unsere Lungen zahlen keinen Tarif.“',
      accent: '#e59c5c',
      slam: true,
    },
    {
      at: 16.0,
      kind: 'card',
      bg: '/assets/docks/bazaar.webp',
      subject: '/assets/portraits/kipper.jpg',
      kicker: 'DRIFT-9 · FIXER-FREQUENZ',
      title: 'KIPPER TARO',
      tagline: 'Verkauft dir eine neue Kennung. Garantie erlischt beim Abdocken.',
      accent: '#63e2d4',
      slam: true,
    },
    {
      at: 19.8,
      kind: 'card',
      bg: '/assets/docks/bastion.webp',
      subject: '/assets/portraits/kaelen_vance.jpg',
      kicker: 'AETHER SOVEREIGN · DIREKTION',
      title: 'DIREKTOR VANCE',
      tagline: 'Zahlt pünktlich. Solange niemand nach den gelöschten Namen fragt.',
      accent: '#e65b49',
      slam: true,
    },
    {
      at: 23.8,
      kind: 'finale',
      bg: '/assets/ships/yard-bomber.webp',
      kicker: 'KEIN MANDAT · KEINE FLOTTE · NUR EIN OFFENER KANAL',
      title: 'SPACE 2601AE',
      tagline: 'DER SEKTOR SCHLÄFT NICHT. DIE HALUNKEN AUCH NICHT.',
      accent: '#f0bc52',
      slam: true,
      finaleChord: true,
    },
  ];

  const DURATION = 29.6;

  // Gritty blues-rock WebAudio groove synthesizer ("No Rest in the Belt", D minor blues @ 102 BPM)
  class BluesGroove {
    constructor() {
      this.ctx = null;
      this.master = null;
      this.analyser = null;
      this.waveData = null;
      this.driveCurve = null;
      this.muted = false;
      this.timer = null;
      this.step = 0;
      this.nextTime = 0;
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

    start() {
      const Ctx = window.AudioContext || window.webkitAudioContext;
      if (!Ctx) return;
      this.ctx = new Ctx();
      if (this.ctx.state === 'suspended') this.ctx.resume().catch(() => {});

      const comp = this.ctx.createDynamicsCompressor();
      comp.threshold.value = -12;
      comp.ratio.value = 5;

      this.master = this.ctx.createGain();
      this.master.gain.value = this.muted ? 0 : 0.42 * this.savedMaster();
      this.analyser = this.ctx.createAnalyser();
      this.analyser.fftSize = 128;
      this.analyser.smoothingTimeConstant = 0.72;
      this.waveData = new Uint8Array(this.analyser.frequencyBinCount);
      this.master.connect(comp);
      comp.connect(this.analyser);
      this.analyser.connect(this.ctx.destination);

      this.driveCurve = this.makeDistortion(36);
      this.step = 0;
      this.nextTime = this.ctx.currentTime + 0.04;
      this.playOpeningSwell(this.nextTime);
      this.timer = window.setInterval(() => this.schedule(), 40);
    }

    playOpeningSwell(t) {
      if (!this.ctx || !this.master) return;
      // Deep atmospheric brass/sub drone (D1 + A1 + D2) for the epic opening before the groove drops
      for (const [freq, vol] of [[36.71, 0.28], [55.0, 0.16], [73.42, 0.18]]) {
        const osc = this.ctx.createOscillator();
        const lp = this.ctx.createBiquadFilter();
        const g = this.ctx.createGain();
        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(freq, t);
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(140, t);
        lp.frequency.linearRampToValueAtTime(420, t + 4.0);
        lp.frequency.exponentialRampToValueAtTime(160, t + 8.6);
        g.gain.setValueAtTime(0.001, t);
        g.gain.linearRampToValueAtTime(vol, t + 1.8);
        g.gain.setValueAtTime(vol, t + 7.4);
        g.gain.exponentialRampToValueAtTime(0.001, t + 8.8);
        osc.connect(lp);
        lp.connect(g);
        g.connect(this.master);
        osc.start(t);
        osc.stop(t + 9.0);
      }
    }

    finaleChord() {
      if (!this.ctx || !this.master) return;
      const t = this.ctx.currentTime + 0.02;
      // Epic sustained D-minor cinematic brass + guitar ring-out
      for (const [freq, vol] of [[73.42, 0.26], [110.0, 0.18], [146.83, 0.20], [174.61, 0.14], [220.0, 0.14]]) {
        const osc = this.ctx.createOscillator();
        const lp = this.ctx.createBiquadFilter();
        const g = this.ctx.createGain();
        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(freq, t);
        lp.type = 'lowpass';
        lp.frequency.setValueAtTime(1400, t);
        lp.frequency.exponentialRampToValueAtTime(240, t + 4.4);
        g.gain.setValueAtTime(0.001, t);
        g.gain.linearRampToValueAtTime(vol, t + 0.08);
        g.gain.exponentialRampToValueAtTime(0.001, t + 4.6);
        osc.connect(lp);
        lp.connect(g);
        g.connect(this.master);
        osc.start(t);
        osc.stop(t + 4.8);
      }
    }

    setMuted(m) {
      this.muted = !!m;
      if (this.master && this.ctx) {
        this.master.gain.setTargetAtTime(this.muted ? 0 : 0.42 * this.savedMaster(), this.ctx.currentTime, 0.03);
      }
    }

    stop() {
      if (this.timer !== null) {
        clearInterval(this.timer);
        this.timer = null;
      }
      if (this.ctx) {
        const ctx = this.ctx;
        if (this.master) {
          try {
            this.master.gain.setTargetAtTime(0.0001, ctx.currentTime, 0.04);
          } catch {}
        }
        this.ctx = null;
        this.master = null;
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
      osc.frequency.setValueAtTime(135, t);
      osc.frequency.exponentialRampToValueAtTime(42, t + 0.12);
      gain.gain.setValueAtTime(0.95 * accent, t);
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.24);
      osc.connect(gain);
      gain.connect(this.master);
      osc.start(t);
      osc.stop(t + 0.26);
    }

    clap(t, accent = 1.0) {
      const ctx = this.ctx;
      const len = Math.floor(ctx.sampleRate * 0.14);
      const buf = ctx.createBuffer(1, len, ctx.sampleRate);
      const data = buf.getChannelData(0);
      for (let i = 0; i < len; i++) {
        data[i] = (Math.random() * 2 - 1) * Math.exp(-i / (ctx.sampleRate * 0.032));
      }
      const src = ctx.createBufferSource();
      src.buffer = buf;
      const bp = ctx.createBiquadFilter();
      bp.type = 'bandpass';
      bp.frequency.value = 1450;
      bp.Q.value = 0.9;
      const g = ctx.createGain();
      g.gain.setValueAtTime(0.55 * accent, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + 0.13);
      src.connect(bp);
      bp.connect(g);
      g.connect(this.master);
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
      g.connect(this.master);
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
      g.connect(this.master);
      osc.start(t);
      osc.stop(t + dur + 0.02);
    }

    slideGuitar(t, fromHz, toHz, dur, vol = 0.26) {
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
      cab.frequency.value = 1150;
      cab.Q.value = 0.75;
      const g = ctx.createGain();
      g.gain.setValueAtTime(0.001, t);
      g.gain.linearRampToValueAtTime(vol, t + 0.015);
      g.gain.exponentialRampToValueAtTime(0.001, t + dur);
      osc1.connect(shaper);
      osc2.connect(shaper);
      shaper.connect(cab);
      cab.connect(g);
      g.connect(this.master);
      osc1.start(t);
      osc2.start(t);
      osc1.stop(t + dur + 0.03);
      osc2.stop(t + dur + 0.03);
    }

    slamStinger() {
      if (!this.ctx || !this.master) return;
      const t = this.ctx.currentTime + 0.01;
      // Heavy sub drop + crunchy guitar power chord stab (D3 + A3)
      const osc = this.ctx.createOscillator();
      const g = this.ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(110, t);
      osc.frequency.exponentialRampToValueAtTime(32, t + 0.36);
      g.gain.setValueAtTime(0.85, t);
      g.gain.exponentialRampToValueAtTime(0.001, t + 0.4);
      osc.connect(g);
      g.connect(this.master);
      osc.start(t);
      osc.stop(t + 0.42);
      this.slideGuitar(t, 146.83, 146.83, 0.42, 0.28);
      this.slideGuitar(t, 220.0, 220.0, 0.42, 0.22);
    }

    schedule() {
      if (!this.ctx || !this.master) return;
      const stepDur = BEAT / 4; // 16th notes
      // 32-step (2-bar) swaggering D-minor blues slide riff
      const riff = {
        0: [146.83, 146.83, 0.34],
        3: [164.81, 174.61, 0.22], // E3 -> F3 slide
        6: [196.0, 207.65, 0.26],  // G3 -> G#3 blue note slide
        8: [220.0, 220.0, 0.36],   // A3
        11: [196.0, 174.61, 0.24], // G3 -> F3 pull
        14: [146.83, 146.83, 0.24],
        16: [146.83, 146.83, 0.32],
        19: [174.61, 196.0, 0.22],
        22: [261.63, 293.66, 0.30], // C4 -> D4 slide!
        26: [220.0, 196.0, 0.24],
        28: [174.61, 146.83, 0.40], // F3 -> D3 resolve
      };
      const bassNotes = {
        0: 73.42, 6: 73.42, 8: 87.31, 12: 98.0,
        16: 73.42, 22: 110.0, 24: 98.0, 28: 87.31,
      };
      while (this.nextTime < this.ctx.currentTime + 0.14) {
        const s = this.step % 32;
        const swing = (this.step % 2 === 1) ? stepDur * 0.16 : 0;
        const t = this.nextTime + swing;
        const introSolo = this.step < 28; // First ~4s: brooding space drone + lone slide licks before stomp drops
        const outroRing = this.step >= 162; // Finale: let epic chord ring out cleanly
        if (!outroRing) {
          if (!introSolo) {
            if (s % 8 === 0 || s === 6 || s === 22) this.kick(t, s % 8 === 0 ? 1.0 : 0.72);
            if (s % 8 === 4) this.clap(t, 0.95);
            if (s % 2 === 0) this.hat(t, s % 4 === 2 ? 0.26 : 0.14);
            if (bassNotes[s]) this.bass(t, bassNotes[s], stepDur * 3.2);
          }
          if (riff[s] && (!introSolo || s === 0 || s === 6 || s === 8 || s === 22)) {
            const [f0, f1, d] = riff[s];
            this.slideGuitar(t, f0, f1, introSolo ? d * 1.4 : d, introSolo ? 0.21 : 0.25);
          }
        }
        this.nextTime += stepDur;
        this.step++;
      }
    }
  }

  function initIntro() {
    const overlay = document.getElementById('intro-film');
    const launchBtn = document.getElementById('menu-intro');
    if (!overlay || !launchBtn) return;

    const bgEl = document.getElementById('intro-bg');
    const fxCanvas = document.getElementById('intro-fx');
    const fxCtx = fxCanvas ? fxCanvas.getContext('2d') : null;
    const waveCanvas = document.getElementById('intro-wave');
    const waveCtx = waveCanvas ? waveCanvas.getContext('2d') : null;
    const subjectWrap = document.getElementById('intro-subject-wrap');
    const subjectEl = document.getElementById('intro-subject');
    const flashEl = document.getElementById('intro-flash');
    const cardEl = document.getElementById('intro-card');
    const kickerEl = document.getElementById('intro-kicker');
    const titleEl = document.getElementById('intro-title');
    const taglineEl = document.getElementById('intro-tagline');
    const captionEl = document.getElementById('intro-caption');
    const captionLine = document.getElementById('intro-caption-line');
    const captionSub = document.getElementById('intro-caption-sub');
    const barEl = document.getElementById('intro-progress');
    const skipBtn = document.getElementById('intro-skip');
    const muteBtn = document.getElementById('intro-mute');

    let playing = false;
    let rafId = 0;
    let startAt = 0;
    let lastFrameAt = 0;
    let currentIndex = -1;
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
        // Subtle beat-reactive anamorphic light streak
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

    function applyScene(scene) {
      activeAccent = scene.accent || '#e4b158';
      overlay.style.setProperty('--intro-accent', activeAccent);
      overlay.setAttribute('data-kind', scene.kind);

      bgEl.style.backgroundImage = `url('${scene.bg}')`;
      bgEl.classList.remove('is-zooming');
      void bgEl.offsetWidth;
      bgEl.classList.add('is-zooming');

      if (scene.slam) {
        flarePulse = 1.0;
        flashEl.classList.remove('is-flash');
        void flashEl.offsetWidth;
        flashEl.classList.add('is-flash');
        groove.slamStinger();
      }
      if (scene.finaleChord) {
        groove.finaleChord();
      }

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

    function stopIntro() {
      if (!playing) return;
      playing = false;
      cancelAnimationFrame(rafId);
      groove.stop();
      overlay.hidden = true;
      overlay.classList.remove('is-ending');
      launchBtn.focus();
    }

    function tick(now) {
      if (!playing) return;
      const dt = Math.min(0.05, Math.max(0.001, (now - lastFrameAt) / 1000));
      lastFrameAt = now;
      const elapsed = (now - startAt) / 1000;
      const pct = Math.min(100, (elapsed / DURATION) * 100);
      barEl.style.width = pct.toFixed(2) + '%';

      let idx = 0;
      for (let i = 0; i < SCENES.length; i++) {
        if (elapsed >= SCENES[i].at) idx = i;
      }
      if (idx !== currentIndex) {
        currentIndex = idx;
        applyScene(SCENES[idx]);
      }
      const { energy, bins } = groove.sampleWave();
      drawFx(dt, energy);
      drawWave(bins);
      if (elapsed >= DURATION - 0.9) {
        overlay.classList.add('is-ending');
      }
      if (elapsed >= DURATION) {
        stopIntro();
        return;
      }
      rafId = requestAnimationFrame(tick);
    }

    function startIntro() {
      if (playing) return;
      playing = true;
      currentIndex = -1;
      flarePulse = 0.6;
      overlay.classList.remove('is-ending');
      overlay.hidden = false;
      barEl.style.width = '0%';
      groove.setMuted(false);
      muteBtn.textContent = 'Ton aus';
      groove.start();
      startAt = performance.now();
      lastFrameAt = startAt;
      skipBtn.focus();
      rafId = requestAnimationFrame(tick);
    }

    launchBtn.addEventListener('click', startIntro);
    skipBtn.addEventListener('click', stopIntro);
    muteBtn.addEventListener('click', () => {
      const next = !groove.muted;
      groove.setMuted(next);
      muteBtn.textContent = next ? 'Ton an' : 'Ton aus';
    });

    // Block game keybinds while intro cinema is active
    window.addEventListener('keydown', (e) => {
      if (!playing) return;
      if (e.code === 'Escape' || e.code === 'Space' || e.code === 'Enter') {
        e.preventDefault();
        e.stopPropagation();
        stopIntro();
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
