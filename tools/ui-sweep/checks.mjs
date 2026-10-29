// Automatische Prüfungen — laufen IM Browser (page.evaluate) auf der fertig geladenen Seite.
// Sie ersetzen das Hinsehen nicht, finden aber die Fehler, die man auf 500 Bildern übersieht.
//
// Ergebnis: [{ kind, detail }] — kind ∈ overflow-x | offscreen | clipped-text | i18n-key | spinner
// (dazu aus sweep.mjs: not-ready = Werkzeug-Befund „noch geladen“, waitShellReady unten)
// (Konsolenfehler und fehlgeschlagene Anfragen sammelt sweep.mjs außerhalb der Seite.)
//
// Dazu „Erster Blick und Scrollen" (docs/DESIGN.md 4.6): foldTop/foldScroll messen im Browser, foldFindings bewertet
// die Messwerte in Node gegen die Schwellen unten. Befunde mit `level: 'hint'` sind Hinweise (Katalog, Seitenlänge ≥ 6),
// keine Auffälligkeit der Seite.

/** Schwellen aus DESIGN.md 4.6 (Regelnummer in Klammern). */
export const FOLD = {
  leadMaxRatio: 0.40,                                     // (3) Vorlauf höchstens 40 % der Fensterhöhe
  maxBanners: 1,                                          // (6) höchstens ein Banner über der Hauptsache
  stickyMaxPx: { mobile: 64, tablet: 64, laptop: 72, wide: 72, ultrawide: 72 },   // (9) klebende Leisten oben zusammen
  screensHint: 6,                                         // (11) ab 6 Bildschirmen: Gliederung nötig (Hinweis)
  screensFinding: 12,                                     // (11) ab 12 Bildschirmen: Befund
  stripsMinScreens: 2,                                    // Streifen erst bei Seiten über 2 Bildschirmen
  stripOverlap: 0.10,                                     // Streifen überlappen sich um 10 %
  maxStrips: 8,                                           // danach nur noch ein Streifen am Seitenende
  scrollWaitMs: 200,                                      // nach dem Scrollen: Leisten (Scroll-Ereignisse) nachziehen lassen
};

/**
 * Warten vor dem Foto (Fehlbefund 29.10.2026: unter Last fotografierte der Sweep 4 von 2 232 Seiten, bevor die App
 * stand — leere Seite, Fußzeile voller Schlüssel wie `nav.help`, 0 Seiten-Anfragen). Netzruhe allein reicht nicht:
 * die Übersetzungen und die Seiten-Komponente können nach 800 ms Stille noch fehlen.
 * `probe()` liefert { keys, outlet, routed } (pageChecks im Hüllen-Modus). Bereit = keine Schlüssel in der Hülle UND
 * die Seiten-Komponente steht hinter dem router-outlet (Seiten ohne router-outlet gelten als bereit).
 * Ergebnis: { ready, loading, waitedMs, detail } — detail beschreibt bei Ablauf, was noch fehlte; loading = die
 * Seiten-Komponente fehlt (dann ist das Bild unbrauchbar, nicht die App fehlerhaft).
 */
export async function waitShellReady(probe, { maxMs = 10000, intervalMs = 200, sleep = ms => new Promise(r => setTimeout(r, ms)), now = Date.now } = {}) {
  const t0 = now();
  let last = null;
  for (;;) {
    last = await probe().catch(() => null);
    if (last && !last.keys.length && (!last.outlet || last.routed)) return { ready: true, loading: false, waitedMs: now() - t0, detail: null };
    if (now() - t0 >= maxMs) break;
    await sleep(intervalMs);
  }
  // loading = die Seite selbst fehlt noch (oder ließ sich nicht prüfen) → Werkzeug-Befund „noch geladen“. Steht die Seite,
  // und nur die Hülle zeigt nach Ablauf noch Schlüssel, ist das ein echter Übersetzungsfehler (bleibt „i18n-key“).
  const loading = !last || (last.outlet && !last.routed);
  const why = !last ? 'Seite nicht prüfbar'
    : [last.keys.length ? `Hülle noch unübersetzt (${last.keys.slice(0, 4).join(', ')})` : null,
      last.outlet && !last.routed ? 'Seiteninhalt hinter dem router-outlet fehlt' : null].filter(Boolean).join('; ');
  return { ready: false, loading, waitedMs: now() - t0, detail: `nach ${Math.round((now() - t0) / 1000)} s noch nicht fertig geladen: ${why}` };
}

/** Befund-Arten der Falz-Prüfung mit Klartext (report.mjs, fold-summary.json). */
export const FOLD_KINDS = {
  'fold-main-missing': 'Hauptsache nicht gefunden',
  'fold-below': 'Hauptsache unter der Falz',
  'fold-lead': 'Vorlauf zu hoch',
  'fold-banners': 'mehrere Banner über der Hauptsache',
  'fold-sticky': 'klebende Leisten zu hoch',
  'fold-covered': 'unten verdeckt',
  'fold-long': 'Seite lang (Gliederung nötig)',
  'fold-too-long': 'Seite zu lang',
};

export function pageChecks(arg) {
  // arg = Liste der i18n-Namensräume (volle Prüfung) oder { ns, shell: true } — dann NUR die Hülle prüfen (shellProbe,
  // Warten vor dem Foto): Schlüssel in Kopfleiste/Fußzeile (Text außerhalb von <main>) und ob das erste router-outlet
  // einen Nachbarn hat (die Seiten-Komponente). Dieselbe Schlüssel-Erkennung wie Prüfung 4 unten.
  const shellMode = !Array.isArray(arg);
  const i18nNamespaces = shellMode ? arg.ns : arg;
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

  // Sichtbare Übersetzungsschlüssel; shellOnly = nur Text außerhalb von <main> (Kopfleiste, Fußzeile, Navigation).
  function i18nKeys(shellOnly) {
    const ns = new Set(i18nNamespaces);
    const keyRe = /^([a-z][a-zA-Z0-9]*)(\.[a-zA-Z0-9_-]+)+$/;
    const keys = new Set();
    const inShell = el => !el.closest('main, .cdk-overlay-container');
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      const t = n.textContent.trim();
      const m = keyRe.exec(t);
      if (m && ns.has(m[1]) && n.parentElement && visible(n.parentElement) && (!shellOnly || inShell(n.parentElement))) keys.add(t);
    }
    for (const el of document.querySelectorAll('[placeholder],[aria-label],[title]')) {
      if (shellOnly && !inShell(el)) continue;
      for (const attr of ['placeholder', 'title']) {
        const t = (el.getAttribute(attr) || '').trim(); const m = keyRe.exec(t);
        if (m && ns.has(m[1])) keys.add(`${t} (${attr})`);
      }
    }
    return [...keys];
  }
  if (shellMode) {
    // Alle fünf Hüllen (RookHub, Turnier, KidHub, LeagueHub, ClubHub) tragen GENAU ein router-outlet direkt in <main>;
    // Angular hängt die Seiten-Komponente als nächsten Geschwister-Knoten dahinter.
    const outlet = document.querySelector('router-outlet');
    return { keys: i18nKeys(true), outlet: !!outlet, routed: !!outlet?.nextElementSibling };
  }

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
  for (const k of i18nKeys(false).slice(0, 8)) findings.push({ kind: 'i18n-key', detail: k });

  // 5. Spinner, der nach dem Laden noch steht.
  const spinner = [...document.querySelectorAll('mat-spinner, mat-progress-spinner, .mat-mdc-progress-spinner')].find(visible);
  if (spinner) findings.push({ kind: 'spinner', detail: `noch ein Ladekreis sichtbar: ${describe(spinner.parentElement || spinner)}` });

  return findings;
}

// ------------------------------------------------------------------------------- Erster Blick und Scrollen

/** Im Browser, Seite oben (scrollY 0): Hauptsache, Vorlauf, Banner darüber, Seitenlänge.
 *  arg = { main: string[] } — Selektoren der Hauptsache, der erste mit sichtbarem Treffer gilt. */
export function foldTop(arg) {
  window.scrollTo(0, 0);
  const vh = window.innerHeight, vw = document.documentElement.clientWidth;
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
  const scrollHeight = Math.max(document.documentElement.scrollHeight, document.body?.scrollHeight ?? 0);
  const out = { vh, vw, scrollHeight, screens: Math.round(scrollHeight / vh * 100) / 100, main: null };

  let mainEl = null;
  for (const sel of arg.main ?? []) {
    let list = [];
    try { list = [...document.querySelectorAll(sel)]; } catch { out.badSelector = sel; continue; }
    // Hosts mit `display: contents` (z. B. app-course-card) haben keinen eigenen Kasten — dann das erste Kind mit Fläche.
    const boxed = el => getComputedStyle(el).display !== 'contents' ? el
      : [...el.querySelectorAll('*')].find(c => getComputedStyle(c).display !== 'contents' && visible(c)) ?? el;
    mainEl = list.map(boxed).find(el => visible(el) && !el.closest('.cdk-overlay-container'));
    if (mainEl) { out.main = sel; break; }
  }
  if (mainEl) {
    const r = mainEl.getBoundingClientRect();
    out.mainTop = Math.round(r.top + window.scrollY);
    out.mainBottom = Math.round(r.bottom + window.scrollY);
    out.mainIsButton = mainEl.matches('button, [role=button], input[type=submit], a.mat-mdc-button-base');
    out.mainDesc = describe(mainEl);
    out.leadPct = Math.round(out.mainTop / vh * 1000) / 10;
  }

  // Banner/Hinweise über der Hauptsache (Regel 6): Klassen mit banner/notice/hint/alert/warn/gate als eigenem
  // Wortteil oder role=alert/status, mit sichtbarer, getönter oder umrandeter Fläche, ganz über der Oberkante der Hauptsache.
  // Nur die äußersten; Formularfeld-Hinweise und der kleine ⓘ-Knopf (app-help-hint) zählen nicht.
  const word = /(^|[-_])(banner|notice|hint|alert|warn|warning|gate)([-_]|$)/i;
  const limit = mainEl ? out.mainTop : null;
  const banners = [];
  if (limit != null) {
    for (const el of document.body.querySelectorAll('*')) {
      const isBanner = [...el.classList].some(c => word.test(c)) || ['alert', 'status'].includes(el.getAttribute('role'));
      if (!isBanner) continue;
      if (el.closest('mat-form-field, .mat-mdc-form-field, app-help-hint, .cdk-overlay-container, .cdk-visually-hidden, svg')) continue;
      if (el.contains(mainEl) || mainEl.contains(el)) continue;
      if (!visible(el)) continue;
      const r = el.getBoundingClientRect();
      if (r.height < 20 || r.width < Math.min(120, vw * 0.3)) continue;
      // Banner = eigene Fläche (DESIGN.md 5.11: „Fläche getönt"): Hintergrund oder Rahmen. Bloßer Erklärtext
      // (p.hint ohne Fläche) ist kein Banner — der fällt unter Regel 5 (Erklärtexte), die das Modell am Bild prüft.
      const st = getComputedStyle(el);
      const tinted = !/^(transparent|rgba\(0, 0, 0, 0\))$/.test(st.backgroundColor) || st.backgroundImage !== 'none'
        || ['Top', 'Right', 'Bottom', 'Left'].some(side => parseFloat(st[`border${side}Width`]) > 0 && st[`border${side}Style`] !== 'none');
      if (!tinted) continue;
      if (r.bottom + window.scrollY > limit + 1) continue;
      if (banners.some(b => b.contains(el))) continue;
      for (let i = banners.length - 1; i >= 0; i--) if (el.contains(banners[i])) banners.splice(i, 1);
      banners.push(el);
    }
  }
  out.banners = banners.length;
  out.bannerList = banners.slice(0, 5).map(describe);
  return out;
}

/** Im Browser, nach foldTop: um einen Bildschirm scrollen → klebende Leisten oben (Regel 9); ans Seitenende →
 *  Verdeckung des letzten Inhalts durch schwebende/klebende Elemente unten (Regel 10). Danach wieder nach oben. */
export async function foldScroll(arg) {
  const wait = ms => new Promise(res => setTimeout(res, ms));
  const frames = () => new Promise(res => requestAnimationFrame(() => requestAnimationFrame(res)));
  const vh = window.innerHeight, vw = document.documentElement.clientWidth;
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
  const pinned = () => [...document.body.querySelectorAll('*')].filter(el => {
    const p = getComputedStyle(el).position;
    return (p === 'fixed' || p === 'sticky') && !el.closest('.cdk-overlay-container, .cdk-visually-hidden') && visible(el);
  });
  const out = {};
  const maxScroll = Math.max(document.documentElement.scrollHeight, document.body?.scrollHeight ?? 0) - vh;
  if (maxScroll <= 4) { out.scrollable = false; return out; }
  out.scrollable = true;

  // Klebende Leisten oben: um einen Bildschirm scrollen; fixe/klebende Elemente, die oben anliegen (oder an eine
  // solche Leiste anschließen), mindestens halb so breit wie das Fenster, höchstens halb so hoch. Gemessen wird das
  // zusammenhängende Band von 0 abwärts — verschachtelte Leisten zählen so nicht doppelt.
  window.scrollTo(0, Math.min(vh, maxScroll));
  await frames(); await wait(arg.waitMs ?? 200);
  const tops = pinned().map(el => ({ el, r: el.getBoundingClientRect() }))
    .filter(({ r }) => r.width >= vw * 0.5 && r.height < vh * 0.5 && r.bottom > 0 && r.top < vh * 0.5)
    .sort((a, b) => a.r.top - b.r.top);
  let band = 0; const used = [];
  for (let changed = true; changed;) {
    changed = false;
    for (const t of tops) {
      if (used.includes(t)) continue;
      if (t.r.top <= band + 12 && t.r.bottom > band) { band = t.r.bottom; used.push(t); changed = true; }
    }
  }
  out.stickyTop = Math.round(Math.max(0, band));
  out.stickyList = used.filter(u => !used.some(o => o !== u && o.el.contains(u.el))).slice(0, 5)
    .map(u => `${describe(u.el)} ${Math.round(u.r.height)}px`);

  // Unten: ans Ende scrollen; fixe/klebende Elemente in der unteren Fensterhälfte; deckt eines davon ein
  // Inhaltselement (eigener Text, Knopf, Feld, Bild, Brett) ab, das nicht selbst darin oder in einer Leiste liegt?
  window.scrollTo(0, maxScroll);
  await frames(); await wait(arg.waitMs ?? 200);
  const all = pinned();
  const bottoms = all.map(el => ({ el, r: el.getBoundingClientRect() }))
    .filter(({ r }) => r.top > vh * 0.5 && r.top < vh && r.height < vh * 0.5)
    .filter(({ el }) => getComputedStyle(el).position === 'fixed' || el.getBoundingClientRect().bottom >= vh - 2);
  const covered = [];
  if (bottoms.length) {
    const content = [...document.body.querySelectorAll('button, a, input, select, textarea, img, canvas, cg-board, p, li, td, th, h1, h2, h3, h4, span, div, label')]
      .filter(el => {
        if (el.closest('.cdk-overlay-container')) return false;
        if (all.some(p => p.contains(el))) return false;
        const own = el.matches('button, a, input, select, textarea, img, canvas, cg-board')
          || [...el.childNodes].some(n => n.nodeType === 3 && n.textContent.trim().length > 1);
        // große Behälter mit etwas eigenem Text (Höhe ≥ halbes Fenster) laufen nur dahinter durch — zählen nicht
        return own && visible(el) && el.getBoundingClientRect().height < vh * 0.5;
      });
    for (const b of bottoms) {
      if (b.el.matches('footer, app-footer') && !b.el.querySelector('button, a')) continue;
      for (const el of content) {
        if (el.contains(b.el)) continue;
        const r = el.getBoundingClientRect();
        const ix = Math.min(r.right, b.r.right) - Math.max(r.left, b.r.left);
        const iy = Math.min(r.bottom, b.r.bottom) - Math.max(r.top, b.r.top);
        if (ix > 4 && iy > 4 && r.bottom > 0) {
          covered.push(`${describe(el)} unter ${describe(b.el)} (${Math.round(iy)}px)`);
          if (covered.length >= 5) break;
        }
      }
      if (covered.length >= 5) break;
    }
  }
  out.bottomPinned = bottoms.slice(0, 5).map(b => `${describe(b.el)} ${Math.round(b.r.height)}px`);
  out.covered = covered;
  window.scrollTo(0, 0);
  await frames();
  return out;
}

/** Bewertet die Messwerte einer Aufnahme (Node-Seite). viewport = mobile|tablet|laptop|wide|ultrawide. */
export function foldFindings(fold, viewport, { exempt = false, mainDeclared = true } = {}) {
  const f = [];
  if (!fold) return f;
  const pct = n => `${Math.round(n)} %`;
  if (fold.screens >= FOLD.screensFinding) f.push({ kind: 'fold-too-long', detail: `${fold.screens} Bildschirme (${fold.scrollHeight}px bei ${fold.vh}px Fenster, Befund ab ${FOLD.screensFinding})` });
  else if (fold.screens >= FOLD.screensHint) f.push({ kind: 'fold-long', level: 'hint', detail: `${fold.screens} Bildschirme — braucht Gliederung (ab ${FOLD.screensHint})` });
  if (exempt) return f;
  if (!mainDeclared) {
    // bewusst ohne Hauptsache (reine Textseite) — nur die Messungen ohne Hauptsache
  } else if (!fold.main) {
    f.push({ kind: 'fold-main-missing', level: 'hint', detail: fold.badSelector ? `Selektor ungültig: ${fold.badSelector}` : 'kein Selektor aus dem Katalog trifft (routes.mjs `main`)' });
  } else {
    const below = fold.mainIsButton ? fold.mainBottom > fold.vh : fold.mainTop >= fold.vh;
    if (below) f.push({ kind: 'fold-below', detail: `${fold.mainDesc} beginnt bei ${fold.mainTop}px${fold.mainIsButton ? ` (Unterkante ${fold.mainBottom}px)` : ''}, Fenster ${fold.vh}px` });
    if (fold.mainTop > fold.vh * FOLD.leadMaxRatio) f.push({ kind: 'fold-lead', detail: `Vorlauf ${fold.mainTop}px = ${pct(fold.leadPct)} der Höhe (höchstens ${pct(FOLD.leadMaxRatio * 100)} ≈ ${Math.round(fold.vh * FOLD.leadMaxRatio)}px)` });
    if (fold.banners > FOLD.maxBanners) f.push({ kind: 'fold-banners', detail: `${fold.banners} Banner über der Hauptsache: ${fold.bannerList.join(' · ')}` });
  }
  const maxSticky = FOLD.stickyMaxPx[viewport] ?? 72;
  if (fold.stickyTop > maxSticky) f.push({ kind: 'fold-sticky', detail: `${fold.stickyTop}px klebend oben (höchstens ${maxSticky}): ${fold.stickyList.join(' · ')}` });
  if (fold.covered?.length) f.push({ kind: 'fold-covered', detail: `${fold.covered.length > 4 ? '≥ 5' : fold.covered.length}× verdeckt: ${fold.covered.slice(0, 3).join(' · ')}` });
  return f;
}
