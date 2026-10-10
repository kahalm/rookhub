// index.html eines Laufs: je Route eine Zeile, je Variante ein Vorschaubild mit Funden und Vergleich.
// Reines HTML + ein paar Zeilen JS (Filter), keine Abhängigkeiten — öffnet sich direkt aus dem Ordner.

import fs from 'node:fs';
import path from 'node:path';

const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

const KIND_LABEL = {
  'overflow-x': 'Seite zu breit', offscreen: 'ragt raus', 'clipped-text': 'Text abgeschnitten',
  'i18n-key': 'Schlüssel statt Text', spinner: 'lädt noch',
};

export function writeIndex(runDir, report) {
  const byRoute = new Map();
  for (const s of report.shots) {
    if (!byRoute.has(s.id)) byRoute.set(s.id, []);
    byRoute.get(s.id).push(s);
  }

  const card = s => {
    const problems = [
      ...(s.findings ?? []).map(f => `<li class="f">${esc(KIND_LABEL[f.kind] ?? f.kind)}: ${esc(f.detail)}</li>`),
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
    const real = problems.filter(p => !p.startsWith('<li class="i">'));
    const flags = [real.length ? 'has-problems' : '', s.diff && s.diff.status !== 'same' ? 'has-diff' : ''].join(' ');
    return `<figure class="shot ${flags}">
      <figcaption>${esc(s.auth === 'user' ? 'angemeldet' : 'abgemeldet')} · ${esc(s.viewport)} · ${esc(s.theme)}</figcaption>
      ${s.file ? `<a href="${esc(s.file)}" target="_blank"><img loading="lazy" src="${esc(s.file)}" alt=""></a>` : '<div class="noimg">kein Bild</div>'}
      ${diff}
      ${problems.length ? `<ul>${problems.join('')}</ul>` : ''}
    </figure>`;
  };

  const rows = [...byRoute.entries()].map(([id, list]) => {
    const flags = [list.some(s => s.findings?.length || s.error || s.console?.length || s.failedRequests?.length) ? 'has-problems' : '',
      list.some(s => s.diff && s.diff.status !== 'same') ? 'has-diff' : ''].join(' ');
    return `<section class="route ${flags}" id="${esc(id)}">
      <h2>${esc(id.replace('__', ' · '))} <small>${esc(list[0].url)}</small></h2>
      <div class="shots">${list.map(card).join('')}</div></section>`;
  }).join('\n');

  const total = report.shots.length;
  const problems = report.shots.filter(s => s.findings?.length || s.error || s.console?.length || s.failedRequests?.length).length;
  const changed = report.shots.filter(s => s.diff && s.diff.status !== 'same').length;
  const unresolved = report.unresolved.map(u => `<li>${esc(u.id)} <code>${esc(u.url)}</code> — fehlt: ${esc(u.missing.join(', '))}</li>`).join('');

  fs.writeFileSync(path.join(runDir, 'index.html'), `<!doctype html><html lang="de"><head><meta charset="utf-8">
<title>UI-Sweep ${esc(report.run)}</title>
<style>
  body { font: 14px/1.4 system-ui, sans-serif; margin: 0; background: #16181d; color: #e6e6e6; }
  header { position: sticky; top: 0; z-index: 2; background: #1f232b; padding: 10px 18px; border-bottom: 1px solid #333; }
  header h1 { font-size: 17px; margin: 0 0 4px; }
  header label { margin-right: 14px; }
  main { padding: 10px 18px 40px; }
  .route { border-top: 1px solid #2c313a; padding: 10px 0; }
  .route h2 { font-size: 15px; margin: 0 0 8px; } .route h2 small { color: #8a93a3; font-weight: 400; margin-left: 8px; }
  .shots { display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-start; }
  .shot { margin: 0; width: 240px; background: #1f232b; border: 1px solid #2c313a; border-radius: 8px; padding: 6px; }
  .shot.has-problems { border-color: #e5534b; } .shot.has-diff { box-shadow: 0 0 0 2px #d29922; }
  .shot figcaption { font-size: 12px; color: #8a93a3; margin-bottom: 4px; }
  .shot img { width: 100%; max-height: 360px; object-fit: cover; object-position: top; border-radius: 4px; display: block; }
  .shot ul { margin: 6px 0 0; padding-left: 16px; font-size: 12px; } .shot li.f { color: #ff7b72; } .shot li.c { color: #d29922; } .shot li.i { color: #8a93a3; }
  .diff { font-size: 12px; color: #d29922; margin-top: 4px; } a { color: #58a6ff; }
  .noimg { height: 80px; display: grid; place-items: center; color: #ff7b72; }
  body.only-problems .route:not(.has-problems), body.only-problems .shot:not(.has-problems) { display: none; }
  body.only-diff .route:not(.has-diff), body.only-diff .shot:not(.has-diff) { display: none; }
  details { margin-top: 6px; color: #8a93a3; }
</style></head><body>
<header>
  <h1>UI-Sweep ${esc(report.run)} — ${esc(report.env)}${report.local ? ' (lokaler Build)' : ''}</h1>
  <div>${total} Aufnahmen · <b>${problems}</b> mit Auffälligkeiten${report.comparedWith ? ` · <b>${changed}</b> verändert gegenüber ${esc(report.comparedWith)}` : ''} · ${report.seconds}s
  &nbsp;&nbsp;<label><input type="checkbox" id="p"> nur Auffällige</label>${report.comparedWith ? '<label><input type="checkbox" id="d"> nur Veränderte</label>' : ''}</div>
  ${unresolved ? `<details><summary>${report.unresolved.length} Routen nicht auflösbar</summary><ul>${unresolved}</ul></details>` : ''}
</header>
<main>${rows}</main>
<script>
  for (const [id, cls] of [['p', 'only-problems'], ['d', 'only-diff']]) {
    const el = document.getElementById(id);
    if (el) el.addEventListener('change', () => document.body.classList.toggle(cls, el.checked));
  }
</script></body></html>`);
}
