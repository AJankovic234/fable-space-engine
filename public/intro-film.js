// The picture and the stereo cue share the media clock, including after a seek.
(() => {
  const byId = id => document.getElementById(id);
  const dialog = byId('intro-film');
  const titleEl = byId('intro-title');
  const video = byId('intro-video');
  const toggle = byId('intro-toggle');
  const mute = byId('intro-mute');
  const seek = byId('intro-seek');
  const timecode = byId('intro-timecode');
  const finish = byId('intro-finish');
  const message = byId('intro-message');
  const replay = byId('intro-replay');
  const launch = byId('intro-play-cta');

  const OMNI_PART1 = '/assets/intro/space2601-omni-part1.mp4';
  const OMNI_PART2 = '/assets/intro/space2601-omni.mp4';

  const FALLBACK_FILM = {
    reels: ['/assets/intro/ruecklauf.mp4'],
    reelDurations: [26],
    title: 'Rücklauf',
    endLabel: 'Rücklauf · Ende',
    shots: [0, 4.5, 9, 14, 18.5, 22.5],
    isOmni: false,
  };

  let activeFilm = FALLBACK_FILM;
  let sourceResolved = false;
  let reelIndex = 0;
  let prefetcher = null;

  async function hasVideo(url) {
    try {
      const res = await fetch(url, { method: 'HEAD', cache: 'no-store' });
      const contentType = (res.headers.get('content-type') || '').toLowerCase();
      return res.ok && contentType.startsWith('video/');
    } catch {
      return false;
    }
  }

  async function resolveFilm() {
    if (sourceResolved) return activeFilm;
    sourceResolved = true;
    const [hasP1, hasP2] = await Promise.all([hasVideo(OMNI_PART1), hasVideo(OMNI_PART2)]);
    if (hasP1 && hasP2) {
      activeFilm = {
        reels: [OMNI_PART1, OMNI_PART2],
        reelDurations: [15.04, 10.03],
        title: 'Space 2601AE · Prolog (4K)',
        endLabel: 'Space 2601AE · Ende',
        shots: [0, 4, 10, 15.04, 20, 23.5],
        isOmni: true,
      };
    } else if (hasP2) {
      activeFilm = {
        reels: [OMNI_PART2],
        reelDurations: [15],
        title: 'Space 2601AE · Prolog',
        endLabel: 'Space 2601AE · Ende',
        shots: [0, 4, 9, 13],
        isOmni: true,
      };
    } else if (hasP1) {
      activeFilm = {
        reels: [OMNI_PART1],
        reelDurations: [15.04],
        title: 'Space 2601AE · Prolog (4K)',
        endLabel: 'Space 2601AE · Ende',
        shots: [0, 4, 10, 13.5],
        isOmni: true,
      };
    }
    return activeFilm;
  }

  function applyFilmMeta(film) {
    if (titleEl) titleEl.textContent = film.title;
  }

  function warmNextReel(nextUrl) {
    if (!nextUrl) return;
    if (!prefetcher) {
      prefetcher = document.createElement('video');
      prefetcher.preload = 'auto';
      prefetcher.muted = true;
    }
    if (prefetcher.getAttribute('src') !== nextUrl) {
      prefetcher.src = nextUrl;
      prefetcher.load();
    }
  }

  function reelOffset(idx) {
    let sum = 0;
    for (let i = 0; i < idx; i++) sum += activeFilm.reelDurations[i] || 0;
    return sum;
  }

  function totalDuration() {
    let sum = 0;
    for (let i = 0; i < activeFilm.reels.length; i++) sum += activeFilm.reelDurations[i] || 0;
    return sum || 26;
  }

  function globalTime() {
    return reelOffset(reelIndex) + (Number.isFinite(video.currentTime) ? video.currentTime : 0);
  }

  function masterVolume() {
    try {
      const master = JSON.parse(localStorage.getItem('space2601.audio'))?.master;
      if (Number.isFinite(master)) return Math.min(1, Math.max(0, master));
    } catch { /* Storage is optional. */ }
    return 1;
  }

  function clock(seconds) {
    const whole = Math.max(0, Math.floor(seconds));
    return Math.floor(whole / 60) + ':' + String(whole % 60).padStart(2, '0');
  }

  function updatePosition() {
    if (Number.isFinite(video.duration) && video.duration > 0) {
      activeFilm.reelDurations[reelIndex] = video.duration;
    }
    const duration = totalDuration();
    const pos = Math.min(duration, globalTime());
    seek.max = duration;
    seek.value = pos;
    seek.setAttribute('aria-valuetext', clock(pos) + ' von ' + clock(duration));
    timecode.textContent = clock(pos) + ' / ' + clock(duration);
  }

  function updateButtons() {
    toggle.textContent = video.paused ? 'Abspielen' : 'Pause';
    mute.textContent = video.muted ? 'Ton an' : 'Ton aus';
  }

  function endScreen(failed) {
    if (!dialog.open) return;
    finish.hidden = false;
    message.textContent = failed ? 'Der Film konnte nicht abgespielt werden.' : activeFilm.endLabel;
    replay.textContent = failed ? 'Erneut laden' : 'Noch einmal';
    toggle.disabled = failed;
    seek.disabled = failed;
    launch.focus();
  }

  async function play() {
    try {
      await video.play();
    } catch (error) {
      if (error.name === 'AbortError' || !dialog.open) return;
      if (error.name === 'NotAllowedError') toggle.focus();
      else endScreen(true);
    }
  }

  function loadReel(idx, localTime, autoplay) {
    reelIndex = Math.max(0, Math.min(activeFilm.reels.length - 1, idx));
    const url = activeFilm.reels[reelIndex];
    const nextUrl = activeFilm.reels[reelIndex + 1];
    if (nextUrl) warmNextReel(nextUrl);

    if (video.getAttribute('src') !== url) {
      video.src = url;
      if (localTime > 0) {
        video.addEventListener('loadedmetadata', () => {
          video.currentTime = Math.min(localTime, video.duration || localTime);
          updatePosition();
        }, { once: true });
      }
    } else if (video.error) {
      video.load();
    } else {
      video.currentTime = localTime;
    }
    updatePosition();
    if (autoplay) play();
  }

  function seekToGlobal(targetTime) {
    const clamped = Math.max(0, Math.min(totalDuration(), targetTime));
    let acc = 0;
    for (let i = 0; i < activeFilm.reels.length; i++) {
      const dur = activeFilm.reelDurations[i] || 0;
      const isLast = i === activeFilm.reels.length - 1;
      if (clamped < acc + dur || isLast) {
        const local = Math.max(0, clamped - acc);
        const wasPlaying = !video.paused && !video.ended;
        finish.hidden = true;
        loadReel(i, local, wasPlaying);
        return;
      }
      acc += dur;
    }
  }

  async function start() {
    finish.hidden = true;
    seek.disabled = false;
    toggle.disabled = false;
    const film = await resolveFilm();
    if (!dialog.open) return;
    applyFilmMeta(film);
    toggle.focus();
    loadReel(0, 0, true);
  }

  function togglePlayback() {
    if (toggle.disabled) return;
    if (video.ended && reelIndex >= activeFilm.reels.length - 1) start();
    else if (video.paused) play();
    else video.pause();
  }

  function close() {
    video.pause();
    dialog.close();
  }

  byId('menu-intro').addEventListener('click', () => {
    if (dialog.open) return;
    dialog.showModal();
    video.volume = masterVolume();
    start();
  });
  byId('intro-skip').addEventListener('click', close);
  launch.addEventListener('click', () => {
    close();
    byId('menu-launch').click();
  });
  replay.addEventListener('click', start);
  toggle.addEventListener('click', togglePlayback);
  mute.addEventListener('click', () => {
    video.muted = !video.muted;
    updateButtons();
  });
  seek.addEventListener('input', () => {
    if (video.readyState > 0 || activeFilm.reels.length > 1) seekToGlobal(Number(seek.value));
  });
  video.addEventListener('seeked', () => {
    if (!video.ended && !video.error) finish.hidden = true;
    updatePosition();
  });
  video.addEventListener('timeupdate', updatePosition);
  video.addEventListener('loadedmetadata', updatePosition);
  video.addEventListener('play', updateButtons);
  video.addEventListener('pause', updateButtons);
  video.addEventListener('ended', () => {
    if (reelIndex + 1 < activeFilm.reels.length) {
      loadReel(reelIndex + 1, 0, true);
      return;
    }
    endScreen(false);
  });
  video.addEventListener('error', () => {
    if (activeFilm.isOmni) {
      activeFilm = FALLBACK_FILM;
      applyFilmMeta(activeFilm);
      loadReel(0, 0, dialog.open);
      return;
    }
    endScreen(true);
  });
  dialog.addEventListener('cancel', event => {
    event.preventDefault();
    close();
  });
  dialog.addEventListener('keydown', event => {
    event.stopPropagation();
    if (event.altKey || event.ctrlKey || event.metaKey) return;
    if (event.code === 'KeyM') {
      event.preventDefault();
      if (!event.repeat) mute.click();
    } else if (event.code === 'Space' && !event.target.closest('button')) {
      event.preventDefault();
      if (!event.repeat) togglePlayback();
    } else if (event.target !== seek && ['ArrowLeft', 'ArrowRight'].includes(event.key) && video.readyState > 0) {
      event.preventDefault();
      const now = globalTime();
      const marks = activeFilm.shots;
      const target = event.key === 'ArrowLeft'
        ? (marks.findLast(t => t < now - 0.4) ?? 0)
        : (marks.find(t => t > now + 0.1) ?? totalDuration());
      seekToGlobal(target);
    }
  });
  dialog.addEventListener('keyup', event => event.stopPropagation());
  document.addEventListener('visibilitychange', () => {
    if (document.hidden && dialog.open) video.pause();
  });
})();
