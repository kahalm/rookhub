// Automatische Prüfungen — laufen IM Browser (page.evaluate) auf der fertig geladenen Seite.
// Sie ersetzen das Hinsehen nicht, finden aber die Fehler, die man auf 500 Bildern übersieht.
//
// Ergebnis: [{ kind, detail }] — kind ∈ overflow-x | offscreen | clipped-text | i18n-key | spinner
// (Konsolenfehler und fehlgeschlagene Anfragen sammelt sweep.mjs außerhalb der Seite.)

export function pageChecks(i18nNamespaces) {
  const findings = [];
  const vw = document.documentElement.clientWidth;

  const describe = el => {
    let s = el.tagName.toLowerCase();
    if (el.id) s += `#${el.id}`;
    const cls = [...el.classList].filter(c => !c.startsWith('ng-') && !c.startsWith('mat-mdc') && !c.startsWith('mdc-')).slice(0, 3);
    if (cls.length) s += '.' + cls.join('.');
    const text = (el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 40);
    return text ? `${s} „${text}"` : s;
  };
  const visible = el => {
    const st = getComputedStyle(el);
    if (st.visibility === 'hidden' || st.display === 'none' || Number(st.opacity) === 0) return false;
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  };
  // Liegt das Element in einem Vorfahren, der waagerecht selbst scrollt oder abschneidet? Dann ist ein Überstand Absicht.
  const clippedByAncestor = el => {
    for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
      const ox = getComputedStyle(p).overflowX;
      if (ox === 'auto' || ox === 'scroll' || ox === 'hidden' || ox === 'clip') return true;
    }
    return false;
  };

  // 1. Die Seite selbst scrollt waagerecht.
  const sw = document.documentElement.scrollWidth;
  if (sw > vw + 1) findings.push({ kind: 'overflow-x', detail: `Seite ${sw}px breit bei ${vw}px Fenster` });

  // 2. Sichtbare Elemente, die rechts/links aus dem Fenster ragen, ohne dass ein Vorfahre sie einfängt.
  const off = [];
  for (const el of document.body.querySelectorAll('*')) {
    if (el.closest('.cdk-overlay-container, svg, cg-container, cg-board')) continue;
    const r = el.getBoundingClientRect();
    if (r.width === 0 || (r.right <= vw + 1 && r.left >= -1)) continue;
    if (!visible(el) || clippedByAncestor(el)) continue;
    // Nur das äußerste überstehende Element melden, nicht seine Kinder.
    if (off.some(o => o.contains(el))) continue;
    off.push(el);
  }
  for (const el of off.slice(0, 5)) {
    const r = el.getBoundingClientRect();
    findings.push({ kind: 'offscreen', detail: `${describe(el)} reicht bis ${Math.round(r.right)}px (Fenster ${vw}px)` });
  }

  // 3. Abgeschnittener Text: Element schneidet ab (overflow hidden/clip) und sein Inhalt ist breiter — ohne Auslassungspunkte.
  let clipped = 0;
  for (const el of document.body.querySelectorAll('*')) {
    if (clipped >= 5) break;
    if (!el.childNodes.length || el.closest('.cdk-overlay-container, svg, cg-container, mat-icon, .mat-icon')) continue;
    // Vorlesetexte (sr-only, 1×1 px mit clip) sind absichtlich unsichtbar abgeschnitten.
    if (el.clientWidth <= 2 || el.clientHeight <= 2) continue;
    const hasText = [...el.childNodes].some(n => n.nodeType === 3 && n.textContent.trim().length > 1);
    if (!hasText) continue;
    const st = getComputedStyle(el);
    if (!(st.overflowX === 'hidden' || st.overflowX === 'clip' || st.overflow === 'hidden')) continue;
    if (st.textOverflow === 'ellipsis') continue;
    if (el.scrollWidth > el.clientWidth + 2 && visible(el)) {
      clipped++;
      findings.push({ kind: 'clipped-text', detail: `${describe(el)} (${el.scrollWidth}px Inhalt in ${el.clientWidth}px)` });
    }
  }

  // 4. Sichtbare Übersetzungsschlüssel („games.review.title" statt Text).
  const ns = new Set(i18nNamespaces);
  const keyRe = /^([a-z][a-zA-Z0-9]*)(\.[a-zA-Z0-9_-]+)+$/;
  const keys = new Set();
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    const t = n.textContent.trim();
    const m = keyRe.exec(t);
    if (m && ns.has(m[1]) && n.parentElement && visible(n.parentElement)) keys.add(t);
  }
  for (const el of document.querySelectorAll('[placeholder],[aria-label],[title]')) {
    for (const attr of ['placeholder', 'title']) {
      const t = (el.getAttribute(attr) || '').trim(); const m = keyRe.exec(t);
      if (m && ns.has(m[1])) keys.add(`${t} (${attr})`);
    }
  }
  for (const k of [...keys].slice(0, 8)) findings.push({ kind: 'i18n-key', detail: k });

  // 5. Spinner, der nach dem Laden noch steht.
  const spinner = [...document.querySelectorAll('mat-spinner, mat-progress-spinner, .mat-mdc-progress-spinner')].find(visible);
  if (spinner) findings.push({ kind: 'spinner', detail: `noch ein Ladekreis sichtbar: ${describe(spinner.parentElement || spinner)}` });

  return findings;
}
