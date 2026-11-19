(() => {
  const STORAGE_KEY = "space2601ae.pilot";
  const EMBLEMS = [
    { id: "kestrel", label: "Kestrel-Schwinge", file: "/assets/emblems/emblem-kestrel.svg" },
    { id: "eclipse", label: "2601AE Eclipse",   file: "/assets/emblems/emblem-eclipse.svg" },
    { id: "rt9",     label: "RT-9 Analogpuls",  file: "/assets/emblems/emblem-rt9.svg" },
    { id: "skull",   label: "Gestrichene ID",   file: "/assets/emblems/emblem-skull.svg" },
    { id: "hex",     label: "Drift-Wabe",       file: "/assets/emblems/emblem-hex.svg" },
    { id: "lance",   label: "Blockadebrecher",  file: "/assets/emblems/emblem-lance.svg" }
  ];
  const ORIGINS = [
    { id: "miner",   label: "Gürtel-Schürfer",     tag: "SIN: ABGELAUFEN · KESTREL",        hint: "Dein letzter Ausweis trägt noch den Stempel der Erzschicht auf Kestrel." },
    { id: "smuggler",label: "Drift-Schmuggler",    tag: "SIN: UMGELÖTET · DRIFT-9",         hint: "Kipper hat deinen Transponder auf Drift-9 umgelötet. Hält, solange keiner genau hinsieht." },
    { id: "analyst", label: "Ehem. Analyst",       tag: "SIN: GELÖSCHT · MORGENLICHT",      hint: "Dein alter Dienstausweis stammt aus Morgenlicht. Die Kennung wurde über Nacht gesperrt." }
  ];
  const COLORS = [
    { id: "cyan",    hex: "#76e5f9", label: "Kurier-Cyan" },
    { id: "amber",   hex: "#f4a24b", label: "RT-9 Bernstein" },
    { id: "phosphor",hex: "#5ef29d", label: "Drift-Phosphor" },
    { id: "crimson", hex: "#ff5a67", label: "AETHER-Karmin" }
  ];
  const PRESETS = [
    { callsign: "KESTREL-04", origin: "miner",    emblem: "kestrel", color: "#76e5f9", label: "KESTREL-04 · Schürfer" },
    { callsign: "ASH-7",      origin: "smuggler", emblem: "hex",     color: "#5ef29d", label: "ASH-7 · Drift-Kurier" },
    { callsign: "RT9-ECHO",   origin: "analyst",  emblem: "rt9",     color: "#f4a24b", label: "RT9-ECHO · Analog" },
    { callsign: "STATIC",     origin: "smuggler", emblem: "skull",   color: "#ff5a67", label: "STATIC · Ungelistet" }
  ];

  function load() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (parsed && parsed.callsign) return parsed;
      }
    } catch (_) {}
    return { callsign: "KESTREL-04", origin: "miner", emblem: "kestrel", color: "#76e5f9" };
  }

  function save(p) {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(p)); } catch (_) {}
    window.__spacePilot = p;
  }

  function init() {
    const root = document.getElementById("menu-pilot");
    if (!root) return;

    let state = load();
    save(state);

    const badgeImg = document.getElementById("pilot-badge-img");
    const badgeCall = document.getElementById("pilot-badge-call");
    const badgeMeta = document.getElementById("pilot-badge-meta");
    const originHint = document.getElementById("pilot-origin-hint");
    const input = document.getElementById("pilot-callsign");
    const originSel = document.getElementById("pilot-origin");
    const presetBox = document.getElementById("pilot-presets");
    const emblemBox = document.getElementById("pilot-emblems");
    const colorBox = document.getElementById("pilot-colors");

    function render(editingCallsign = false) {
      const emb = EMBLEMS.find((e) => e.id === state.emblem) || EMBLEMS[0];
      const org = ORIGINS.find((o) => o.id === state.origin) || ORIGINS[0];
      document.documentElement.style.setProperty("--pilot-accent", state.color);
      if (badgeImg) badgeImg.src = emb.file;
      if (badgeCall) badgeCall.textContent = state.callsign || "KESTREL-04";
      if (badgeMeta) badgeMeta.textContent = org.tag;
      if (originHint) originHint.textContent = org.hint;
      if (input && !editingCallsign && input.value !== state.callsign) input.value = state.callsign;
      if (originSel) originSel.value = state.origin;

      emblemBox.querySelectorAll("button").forEach((b) => {
        const on = b.dataset.emblem === state.emblem;
        b.classList.toggle("active", on);
        b.setAttribute("aria-pressed", String(on));
      });
      colorBox.querySelectorAll("button").forEach((b) => {
        const on = b.dataset.color === state.color;
        b.classList.toggle("active", on);
        b.setAttribute("aria-pressed", String(on));
      });
    }

    PRESETS.forEach((p) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.textContent = p.label;
      btn.addEventListener("click", () => {
        state = { callsign: p.callsign, origin: p.origin, emblem: p.emblem, color: p.color };
        save(state);
        render();
      });
      presetBox.appendChild(btn);
    });

    EMBLEMS.forEach((e) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.dataset.emblem = e.id;
      btn.title = e.label;
      btn.setAttribute("aria-label", e.label);
      const img = document.createElement("img");
      img.src = e.file;
      img.alt = e.label;
      btn.appendChild(img);
      btn.addEventListener("click", () => {
        state.emblem = e.id;
        save(state);
        render();
      });
      emblemBox.appendChild(btn);
    });

    COLORS.forEach((c) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.dataset.color = c.hex;
      btn.title = c.label;
      btn.setAttribute("aria-label", c.label);
      btn.style.setProperty("--swatch", c.hex);
      btn.addEventListener("click", () => {
        state.color = c.hex;
        save(state);
        render();
      });
      colorBox.appendChild(btn);
    });

    if (input) {
      input.addEventListener("input", () => {
        const clean = input.value.toUpperCase().replace(/[^A-Z0-9\- ]/g, "").slice(0, 14);
        if (input.value !== clean) {
          const caret = input.value.slice(0, input.selectionStart).replace(/[^A-Za-z0-9\- ]/g, "").length;
          input.value = clean;
          input.setSelectionRange(caret, caret);
        }
        state.callsign = clean || "KESTREL-04";
        save(state);
        render(true);
      });
      input.addEventListener("blur", () => {
        state.callsign = input.value.trim() || "KESTREL-04";
        save(state);
        render();
      });
    }

    if (originSel) {
      originSel.addEventListener("change", () => {
        state.origin = originSel.value;
        save(state);
        render();
      });
    }

    render();
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})();
