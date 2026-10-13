// index.html eines Laufs: je Route eine Zeile, je Variante ein Vorschaubild mit Funden und Vergleich.
// Reines HTML + ein paar Zeilen JS (Filter), keine Abhängigkeiten — öffnet sich direkt aus dem Ordner.

import fs from 'node:fs';
import path from 'node:path';
import { FOLD, FOLD_KINDS } from './checks.mjs';

const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

const KIND_LABEL = {
  'overflow-x': 'Seite zu breit', offscreen: 'ragt raus', 'clipped-text': 'Text abgeschnitten',
  'i18n-key': 'Schlüssel statt Text', spinner: 'lädt noch',
  'state-step': 'Bedienschritt gescheitert', 'state-missed': 'Zustand nicht erreicht', ...FOLD_KINDS,
};
const isFold = f => f.kind.startsWith('fold-') && f.level !== 'hint';
const real = s => !!(s.findings?.some(f => f.level !== 'hint') || s.error || s.console?.length || s.failedRequests?.length);

const AUTH_LABEL = { anon: 'abgemeldet', user: 'normales Konto', admin: 'Admin', empty: 'leeres Konto', trainer: 'Trainer' };

export function writeIndex(runDir, report) {
  const byRoute = new Map();
  for (const s of report.shots) {
    if (!byRoute.has(s.id)) byRoute.set(s.id, []);
    byRoute.get(s.id).push(s);
  }

  const card = s => {
    const problems = [
      ...(s.findings ?? []).map(f => `<li class="${f.level === 'hint' ? 'h' : 'f'}">${esc(KIND_LABEL[f.kind] ?? f.kind)}: ${esc(f.detail)}</li>`),
      ...(s.console ?? []).map(c => `<li class="c">Konsole: ${esc(c)}</li>`),
      ...(s.failedRequests ?? []).map(c => `<li class="c">Anfrage: ${esc(c)}</li>`),
      ...(s.error ? [`<li class="f">Aufnahme gescheitert: ${esc(s.error)}</li>`] : []),
      ...(s.redirected ? [`<li class="i">weitergeleitet nach ${esc(s.finalUrl)}</li>`] : []),
      ...(s.toolDelayMs ? [`<li class="i">Werkzeug-Drossel hielt Anfragen ${Math.round(s.toolDelayMs / 1000)} s zurück — Bild evtl. vor den Daten</li>`] : []),
    ];
    const diff = s.diff && s.diff.status !== 'same'
      ? `<div class="diff">${s.diff.status === 'new' ? 'neu' : `verändert${s.diff.ratio != null ? ` (${(s.diff.ratio * 100).toFixed(1)} %)` : ''}${s.diff.note ? ` — ${esc(s.diff.note)}` : ''}`}
         ${s.diff.old ? ` · <a href="${esc(s.diff.old)}" target="_blank">vorher</a>` : ''}${s.diff.file ? ` · <a href="${esc(s.diff.file)}" target="_blank">Unterschied</a>` : ''}</div>`
      : '';
    const flags = [real(s) ? 'has-problems' : '', s.diff && s.diff.status !== 'same' ? 'has-diff' : '',
      s.findings?.some(isFold) ? 'has-fold' : ''].join(' ');
    const m = s.fold;
    const foldLine = m ? `<div class="fold">${m.exempt ? 'Falz: Ausnahme · ' : ''}${m.main
      ? `Hauptsache ${m.mainTop}px (${m.leadPct} %)`
      : m.main === null && !m.exempt ? 'Hauptsache —' : ''} · ${m.screens} Bildsch. · Banner ${m.banners ?? '–'}${m.stickyTop != null ? ` · klebt ${m.stickyTop}px` : ''}${m.covered?.length ? ` · verdeckt ${m.covered.length}` : ''}</div>` : '';
    const strips = s.strips?.length ? ` · Streifen ${s.strips.map((f, i) => `<a href="${esc(f)}" target="_blank">${i + 1}</a>`).join(' ')}` : '';
    const links = s.file ? `<div class="links"><a href="${esc(s.file)}" target="_blank">Ganzseite</a>${strips}</div>` : '';
    return `<figure class="shot ${flags}">
      <figcaption>${esc(AUTH_LABEL[s.auth] ?? s.auth)} · ${esc(s.viewport)} · ${esc(s.theme)}${s.lang && s.lang !== 'de' ? ` · ${esc(s.lang)}` : ''}</figcaption>
      ${s.file ? `<a href="${esc(s.firstShot ?? s.file)}" target="_blank"><img loading="lazy" src="${esc(s.firstShot ?? s.file)}" alt=""></a>` : '<div class="noimg">kein Bild</div>'}
      ${links}${foldLine}
      ${diff}
      ${problems.length ? `<ul>${problems.join('')}</ul>` : ''}
    </figure>`;
  };

  const rows = [...byRoute.entries()].map(([id, list]) => {
    const flags = [list.some(real) ? 'has-problems' : '',
      list.some(s => s.diff && s.diff.status !== 'same') ? 'has-diff' : '', list.some(s => s.findings?.some(isFold)) ? 'has-fold' : ''].join(' ');
    return `<section class="route ${flags}" id="${esc(id)}">
      <h2>${esc(id.replace('__', ' · '))} <small>${esc(list[0].url)}</small></h2>
      <div class="shots">${list.map(card).join('')}</div></section>`;
  }).join('\n');

  const total = report.shots.length;
  const problems = report.shots.filter(real).length;
  const foldN = report.shots.filter(s => s.findings?.some(isFold)).length;
  const foldKinds = {};
  for (const s of report.shots) for (const f of s.findings ?? []) if (f.kind.startsWith('fold-')) foldKinds[f.kind] = (foldKinds[f.kind] ?? 0) + 1;
  const foldText = Object.entries(foldKinds).map(([k, n]) => `${esc(FOLD_KINDS[k] ?? k)} ${n}`).join(' · ');
  const changed = report.shots.filter(s => s.diff && s.diff.status !== 'same').length;
  const unresolved = report.unresolved.map(u => `<li>${esc(u.id)} <code>${esc(u.url)}</code> — fehlt: ${esc(u.missing.join(', '))}</li>`).join('');

  fs.writeFileSync(path.join(runDir, 'index.html'), `<!doctype html><html lang="de"><head><meta charset="utf-8">
<title>UI-Sweep ${esc(report.run)}</title>
<style>
  body { font: 14px/1.4 system-ui, sans-serif; margin: 0; background: #16181d; color: #e6e6e6; }
  header { position: sticky; top: 0; z-index: 2; background: #1f232b; padding: 10px 18px; border-bottom: 1px solid #333; }
  header h1 { font-size: 17px; margin: 0 0 4px; }
  header .mode { font-size: 12px; color: #8a93a3; margin-bottom: 2px; }
  header label { margin-right: 14px; }
  main { padding: 10px 18px 40px; }
  .route { border-top: 1px solid #2c313a; padding: 10px 0; }
  .route h2 { font-size: 15px; margin: 0 0 8px; } .route h2 small { color: #8a93a3; font-weight: 400; margin-left: 8px; }
  .shots { display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-start; }
  .shot { margin: 0; width: 240px; background: #1f232b; border: 1px solid #2c313a; border-radius: 8px; padding: 6px; }
  .shot.has-problems { border-color: #e5534b; } .shot.has-diff { box-shadow: 0 0 0 2px #d29922; }
  .shot figcaption { font-size: 12px; color: #8a93a3; margin-bottom: 4px; }
  .shot img { width: 100%; max-height: 360px; object-fit: cover; object-position: top; border-radius: 4px; display: block; }
  .shot ul { margin: 6px 0 0; padding-left: 16px; font-size: 12px; } .shot li.f { color: #ff7b72; } .shot li.c { color: #d29922; } .shot li.i, .shot li.h { color: #8a93a3; }
  .shot .links, .shot .fold { font-size: 11px; color: #8a93a3; margin-top: 3px; } .shot.has-fold { outline: 2px dashed #a371f7; outline-offset: 1px; }
  body.only-fold .route:not(.has-fold), body.only-fold .shot:not(.has-fold) { display: none; }
  .diff { font-size: 12px; color: #d29922; margin-top: 4px; } a { color: #58a6ff; }
  .noimg { height: 80px; display: grid; place-items: center; color: #ff7b72; }
  body.only-problems .route:not(.has-problems), body.only-problems .shot:not(.has-problems) { display: none; }
  body.only-diff .route:not(.has-diff), body.only-diff .shot:not(.has-diff) { display: none; }
  details { margin-top: 6px; color: #8a93a3; }
</style></head><body>
<header>
  <h1>UI-Sweep ${esc(report.run)} — ${esc(report.env)}${report.local ? ' (lokaler Build)' : ''}${report.fast ? ' · --fast' : ''}</h1>
  <div class="mode">${report.fast ? 'Modus <b>--fast</b>: jede Seite einmal geladen, Breiten durch Umstellen der Fenstergröße — Handy-Ansicht nur über die Breite, ohne Touch' : 'Modus normal: jede Aufnahme frisch geladen, Handy/Tablet mit Touch-Emulation'}${report.rate ? ` · ${report.rate} API-Anfragen/min, ${report.concurrency} gleichzeitig` : ''}${report.pauses429 != null ? ` · ${report.pauses429} Pausen wegen 429` : ''}</div>
  <div>${total} Aufnahmen · <b>${problems}</b> mit Auffälligkeiten${report.comparedWith ? ` · <b>${changed}</b> verändert gegenüber ${esc(report.comparedWith)}` : ''} · ${report.seconds}s
  &nbsp;&nbsp;<label><input type="checkbox" id="p"> nur Auffällige</label><label><input type="checkbox" id="f"> nur Falz-Befunde (${foldN})</label>${report.comparedWith ? '<label><input type="checkbox" id="d"> nur Veränderte</label>' : ''}</div>
  ${foldText ? `<div class="mode">Falz (DESIGN.md 4.6): ${foldText} — Vorschau = „Erster Blick" (ein Fenster), Ganzseite/Streifen als Links</div>` : ''}
  ${unresolved ? `<details><summary>${report.unresolved.length} Routen nicht auflösbar</summary><ul>${unresolved}</ul></details>` : ''}
</header>
<main>${rows}</main>
<script>
  for (const [id, cls] of [['p', 'only-problems'], ['d', 'only-diff'], ['f', 'only-fold']]) {
    const el = document.getElementById(id);
    if (el) el.addEventListener('change', () => document.body.classList.toggle(cls, el.checked));
  }
</script></body></html>`);
}

/** fold-summary.json: Falz-Befunde je Art × Ansicht × Oberfläche, Routen mit Falz-Befunden samt Messwerten, Bildzahlen. */
export function foldSummary(report) {
  const byKind = {}; const byKindViewport = {}; const byKindViewportApp = {}; const routes = [];
  for (const s of report.shots) {
    const ff = (s.findings ?? []).filter(f => f.kind.startsWith('fold-'));
    for (const f of ff) {
      byKind[f.kind] = (byKind[f.kind] ?? 0) + 1;
      ((byKindViewport[f.kind] ??= {})[s.viewport] = (byKindViewport[f.kind][s.viewport] ?? 0) + 1);
      const a = ((byKindViewportApp[f.kind] ??= {})[s.viewport] ??= {});
      a[s.app] = (a[s.app] ?? 0) + 1;
    }
    if (ff.length) {
      const m = s.fold ?? {};
      routes.push({ id: s.id, path: s.path, viewport: s.viewport, auth: s.auth, theme: s.theme, lang: s.lang,
        kinds: ff.map(f => f.kind), details: ff.map(f => `${FOLD_KINDS[f.kind] ?? f.kind}: ${f.detail}`),
        fold: { main: m.main, mainTop: m.mainTop, mainBottom: m.mainBottom, leadPct: m.leadPct, vh: m.vh, screens: m.screens,
          banners: m.banners, stickyTop: m.stickyTop, covered: m.covered?.length ?? 0, exempt: m.exempt },
        firstShot: s.firstShot });
    }
  }
  return {
    run: report.run, kinds: FOLD_KINDS, thresholds: FOLD, shots: report.shots.length,
    byKind, byKindViewport, byKindViewportApp,
    images: {
      full: report.shots.filter(s => s.file).length,
      first: report.shots.filter(s => s.firstShot).length,
      strips: report.shots.reduce((n, s) => n + (s.strips?.length ?? 0), 0),
    },
    routes,
  };
}
