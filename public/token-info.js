// KI-Token & Kosten: Info-Schalter im Hauptmenü. Zeigt, wie viele Token die beteiligten KI-Modelle für
// dieses Spiel verbraucht haben und was das zu öffentlichen API-Listenpreisen effektiv gekostet hätte.
// Daten: /token-info.json, erzeugt von `node tools/kostenbuch/kostenbuch.mjs --write` (nur Summen je
// Modell, keine Sitzungsdaten). Eigenständig, damit index.html nur diese eine Skriptzeile braucht.
(() => {
  const DATA_URL = '/token-info.json';
  const AI = {
    Anthropic: { ki: 'Claude', farbe: '#e8a96a' },
    OpenAI: { ki: 'GPT', farbe: '#69e2e3' },
    Google: { ki: 'Gemini', farbe: '#a9b6ff' },
  };
  const aiOf = name => AI[name] ?? { ki: name, farbe: '#b8c4c6' };

  const de = (n, d = 0) => Number(n).toLocaleString('de-DE', { minimumFractionDigits: d, maximumFractionDigits: d });
  const tokens = n => n >= 1e9 ? `${de(n / 1e9, 2)} Mrd.` : n >= 1e6 ? `${de(n / 1e6, n >= 1e8 ? 0 : 1)} Mio.`
    : n >= 1e3 ? `${de(n / 1e3)} Tsd.` : de(n);
  const usd = v => `$${de(v, v < 10 ? 2 : 0)}`;
  const eur = (v, rate) => `${de(v / rate, v / rate < 10 ? 2 : 0)} €`;
  const perMio = (v, n) => n > 0 ? `$${de(v * 1e6 / n, 2)}` : '–';
  const pct = (a, b) => b > 0 ? a / b * 100 : 0;
  const dauer = ms => { const m = Math.round(ms / 6e4); return m < 60 ? `${m} Min.` : `${Math.floor(m / 60)} Std. ${String(m % 60).padStart(2, '0')} Min.`; };
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const price = v => `$${Number(v).toLocaleString('de-DE', { minimumFractionDigits: Number.isInteger(v) ? 0 : 2, maximumFractionDigits: 2 })}`;
  const listPrice = p => p ? `${price(p.eingabe)} / ${price(p.ausgabe)}` : '–';

  let data = null;
  let failed = false;

  function shareBar(label, parts, total, key) {
    const segs = parts.map(p => ({ ...p, w: pct(p[key], total) })).filter(p => p.w > 0);
    const text = segs.map(p => `${aiOf(p.name).ki} ${de(p.w)} %`).join(', ');
    return `<div class="ti-share-row"><span class="ti-share-label">${label}</span>
      <div class="ti-share" role="img" aria-label="${esc(`${label}: ${text}`)}">${segs.map(p =>
        `<span style="--w:${p.w.toFixed(2)}%;--c:${aiOf(p.name).farbe}" title="${esc(`${aiOf(p.name).ki}: ${de(p.w, 1)} %`)}">${p.w >= 7 ? `<b>${esc(aiOf(p.name).ki)}</b> ${de(p.w)} %` : ''}</span>`).join('')}</div></div>`;
  }

  function card(a, d) {
    const ai = aiOf(a.name);
    const models = d.modelle.filter(m => m.anbieter === a.name);
    const tools = [...new Set(models.map(m => m.werkzeug))].join(' · ');
    return `<article class="ti-ai" style="--c:${ai.farbe}">
      <h3>${esc(ai.ki)} <small>${esc(a.name)} · ${esc(tools)}</small></h3>
      <p class="ti-big">${tokens(a.token)} <span>Token</span></p>
      <dl>
        <div><dt>Effektive Kosten</dt><dd>${usd(a.usd)} <small>≈ ${eur(a.usd, d.usdProEur)}</small></dd></div>
        <div><dt>Ø je 1 Mio. Token</dt><dd>${perMio(a.usd, a.token)}</dd></div>
        <div><dt>Ausgabe-Token</dt><dd>${tokens(a.ausgabe)}</dd></div>
        <div><dt>Anfragen</dt><dd>${de(a.anfragen)}</dd></div>
        <div><dt>Arbeitszeit</dt><dd>${dauer(a.arbeitMs)}</dd></div>
      </dl>
      <p class="ti-models">${models.map(m => esc(m.name.replace(/^Claude /, ''))).join(' · ')}</p>
    </article>`;
  }

  function row(m) {
    const input = m.eingabe + m.cacheLesen + m.cacheSchreiben;
    return `<tr style="--c:${aiOf(m.anbieter).farbe}">
      <th scope="row"><span>${esc(m.name)}</span><small>${esc(m.werkzeug)}</small></th>
      <td>${de(m.anfragen)}</td>
      <td>${tokens(m.token)}</td>
      <td>${de(pct(m.cacheLesen, input))} %</td>
      <td>${tokens(m.ausgabe)}</td>
      <td>${usd(m.usd)}</td>
      <td><strong>${perMio(m.usd, m.token)}</strong></td>
      <td>${listPrice(m.liste)}</td>
    </tr>`;
  }

  function content(d) {
    const s = d.summe;
    const stand = new Date(d.stand);
    const standText = Number.isFinite(stand.getTime())
      ? stand.toLocaleString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '–';
    return `
      <section class="ti-total" aria-label="Gesamt">
        <div><span>Token gesamt</span><strong>${tokens(s.token)}</strong></div>
        <div><span>Effektive Kosten</span><strong>${usd(s.usd)}</strong><small>≈ ${eur(s.usd, d.usdProEur)}</small></div>
        <div><span>Ø je 1 Mio. Token</span><strong>${perMio(s.usd, s.token)}</strong></div>
        <div><span>Arbeitszeit der Agenten</span><strong>${dauer(s.arbeitMs)}</strong></div>
      </section>
      <section class="ti-shares" aria-label="Anteile der KIs">
        ${shareBar('Token', d.anbieter, s.token, 'token')}
        ${shareBar('Kosten', d.anbieter, s.usd, 'usd')}
      </section>
      <section class="ti-ais">${d.anbieter.map(a => card(a, d)).join('')}</section>
      <h3 class="ti-subhead">Je Modell</h3>
      <div class="ti-table"><table>
        <thead><tr><th scope="col">Modell</th><th scope="col">Anfragen</th><th scope="col">Token</th><th scope="col">Cache-Anteil</th>
          <th scope="col">Ausgabe</th><th scope="col">Kosten</th><th scope="col">Ø je 1 Mio.</th><th scope="col">Liste Ein / Aus</th></tr></thead>
        <tbody>${d.modelle.map(row).join('')}</tbody>
      </table></div>
      <p class="ti-note"><strong>So wird gerechnet:</strong> Token = Eingabe (neu, aus dem Prompt-Cache gelesen und in ihn geschrieben)
        + Ausgabe einschließlich Denk-Token. Jede Tokenart wird zu ihrem API-Listenpreis in US-Dollar bewertet
        („Liste“: Eingabe / Ausgabe je 1 Mio. Token). Der Durchschnitt liegt weit darunter, weil fast jede Anfrage den
        bisherigen Verlauf aus dem Cache liest, der nur einen Bruchteil kostet. Bezahlt wurden tatsächlich Abos.
        Grobe Schätzung, keine Rechnung.</p>
      <p class="ti-note">Stand ${esc(standText)} · Quelle: lokale Sitzungsprotokolle von Claude Code, Codex und Gemini,
        ausgewertet von <code>tools/kostenbuch</code> · 1 € = ${de(d.usdProEur, 4)} $</p>`;
  }

  function buildDialog(menu) {
    const dlg = document.createElement('dialog');
    dlg.id = 'token-info';
    dlg.setAttribute('aria-labelledby', 'token-info-title');
    dlg.innerHTML = `
      <header class="ti-head">
        <div>
          <p class="ti-eyebrow">Vibe-Coding-Benchmark</p>
          <h2 id="token-info-title">Welche KI hat wie viele Token verbraucht?</h2>
          <p>Space 2601AE wurde vollständig von KI-Modellen programmiert. Hier steht, wie viele Token jede KI
            dafür gebraucht hat und was das zu API-Listenpreisen gekostet hätte.</p>
        </div>
        <form method="dialog"><button type="submit" class="ti-close">Schließen <kbd>Esc</kbd></button></form>
      </header>
      <div class="ti-body" aria-live="polite"></div>`;
    // Tasten im Fenster gehören dem Fenster: Enter, F2, F8 usw. dürfen nicht den Flug starten.
    dlg.addEventListener('keydown', e => e.stopPropagation());
    dlg.addEventListener('click', e => { if (e.target === dlg) dlg.close(); });
    menu.appendChild(dlg);
    return dlg;
  }

  function fill(dlg) {
    const body = dlg.querySelector('.ti-body');
    if (data) body.innerHTML = content(data);
    else body.innerHTML = `<p class="ti-note">${failed ? 'Die Token-Auswertung (token-info.json) wurde nicht gefunden.' : 'Lade Token-Auswertung …'}</p>`;
  }

  function init() {
    const menu = document.getElementById('flight-menu');
    if (!menu || document.getElementById('menu-token-info')) return;
    const css = document.createElement('link');
    css.rel = 'stylesheet';
    css.href = '/token-info.css';
    document.head.appendChild(css);

    const btn = document.createElement('button');
    btn.id = 'menu-token-info';
    btn.type = 'button';
    btn.className = 'token-info-trigger';
    btn.setAttribute('aria-haspopup', 'dialog');
    btn.innerHTML = '<span class="ti-icon" aria-hidden="true">i</span><span class="ti-label"><strong>KI-Token &amp; Kosten</strong><small id="menu-token-info-sum">Benchmark</small></span>';
    menu.appendChild(btn);
    const dlg = buildDialog(menu);

    btn.addEventListener('click', () => { fill(dlg); dlg.showModal(); });
    // Verschwindet das Menü (Spielstart, Spielstand geladen), schließt auch das Fenster.
    new MutationObserver(() => { if (menu.hidden && dlg.open) dlg.close(); }).observe(menu, { attributes: true, attributeFilter: ['hidden'] });

    fetch(DATA_URL, { cache: 'no-cache' })
      .then(r => { if (!r.ok) throw new Error(String(r.status)); return r.json(); })
      .then(d => {
        if (!d?.summe || !Array.isArray(d.modelle) || !Array.isArray(d.anbieter)) throw new Error('Format');
        data = d;
        document.getElementById('menu-token-info-sum').textContent = `${tokens(d.summe.token)} Token · ≈ ${eur(d.summe.usd, d.usdProEur)}`;
      })
      .catch(() => { failed = true; })
      .finally(() => { if (dlg.open) fill(dlg); });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
