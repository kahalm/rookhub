// Zustände NACH einer Bedienung: Menüs, Dialoge, Reiter, Formularfehler, Brett nach einem Zug …
//
// Der Routen-Katalog (routes.mjs) fotografiert jede Seite so, wie sie nach dem Laden aussieht. Ein Zustand hier hängt an
// einer Katalog-Route (`of` = deren Id, siehe `node sweep.mjs --list`), lädt dieselbe Adresse, führt `steps` aus und
// fotografiert dann — als eigene Aufnahme mit der Id `<Route>--<name>` (Bericht, Vergleich, --route-Filter wie gewohnt).
//
//   of     — Id der Katalog-Route
//   name   — kurzer Name des Zustands (Teil der Bild-Datei)
//   auth   — nur diese Rollen (Vorgabe: die der Route). Zustände, die etwas SCHREIBEN würden, gibt es hier nicht —
//            Dialoge werden geöffnet, nie bestätigt; Formulare werden nie abgeschickt, außer sie scheitern absichtlich
//            (falsches Kennwort).
//   steps  — der Reihe nach:
//              { click: sel }           erstes passendes Element anklicken
//              { fill: sel, text }      Feld füllen
//              { press: key }           Taste (z. B. 'Escape', 'Enter'); mit sel: auf diesem Element
//              { hover: sel }
//              { blur: sel }            Fokus weg (Pflichtfeld-Fehler erscheinen erst danach)
//              { waitFor: sel }         warten, bis sichtbar (höchstens 8 s)
//              { wait: ms }
//              { move: 'e2e4', board?: sel }  Zug auf dem Brett (chessground: Klick Feld → Feld, Ausrichtung beachtet)
//            Selektoren sind Playwright-Selektoren (CSS, `:has-text("…")`, `>> nth=1` …).
//   expect — Selektor, der danach sichtbar sein muss; fehlt er, steht „Zustand nicht erreicht" im Bericht (die Aufnahme
//            gibt es trotzdem — sie zeigt, was stattdessen da ist).
//   main   — Hauptsache für die Falz-Prüfung (Vorgabe: keine; Zustände sind von der Falz-Prüfung ausgenommen).
//
// Ein Schritt, der scheitert (Selektor fehlt), bricht die Schritte ab; die Aufnahme zeigt den Stand bis dahin.

const MENU = '.cdk-overlay-pane .mat-mdc-menu-panel';
const DIALOG = 'mat-dialog-container';

export const STATES = [
  { of: 'rookhub__games-gameId', name: 'menue', steps: [{ click: '.game-menu' }], expect: MENU },
  { of: 'rookhub__analysis', name: 'zug-e4', steps: [{ move: 'e2e4' }], expect: 'cg-board piece.white.pawn' },
];

/** Katalog-Routen um die Zustände erweitert: je Zustand eine abgeleitete Route mit `steps`. */
export function withStates(routes, routeId) {
  const byId = new Map(routes.map(r => [routeId(r), r]));
  const out = [];
  for (const s of STATES) {
    const base = byId.get(s.of);
    if (!base) throw new Error(`states.mjs: Zustand „${s.name}" hängt an unbekannter Route ${s.of}`);
    const baseLabel = s.of.slice(base.app.length + 2);
    out.push({ ...base, label: `${baseLabel}--${s.name}`, state: s.name, stateOf: s.of, steps: s.steps,
      stateAuth: s.auth ?? null, expect: s.expect ?? null, main: s.main ?? null, foldExempt: s.main == null });
  }
  return out;
}

/**
 * Führt die Schritte aus. Gibt `null` zurück oder eine Fehlermeldung („Schritt 2 (click …): …").
 * `settle` wartet auf Netzruhe (wie nach dem Laden), damit Nachlade-Zustände (Lösung, Fehlermeldung) im Bild sind.
 */
export async function runSteps(page, steps, settle) {
  for (const [i, st] of steps.entries()) {
    const what = Object.entries(st).map(([k, v]) => `${k} ${v}`).join(', ');
    try {
      if (st.click) await page.locator(st.click).first().click({ timeout: 8000 });
      else if (st.fill) await page.locator(st.fill).first().fill(st.text ?? '', { timeout: 8000 });
      else if (st.press) await (st.sel ? page.locator(st.sel).first().press(st.press, { timeout: 8000 }) : page.keyboard.press(st.press));
      else if (st.hover) await page.locator(st.hover).first().hover({ timeout: 8000 });
      else if (st.blur) await page.locator(st.blur).first().evaluate(el => el.blur());
      else if (st.waitFor) await page.locator(st.waitFor).first().waitFor({ state: 'visible', timeout: 8000 });
      else if (st.wait) await page.waitForTimeout(st.wait);
      else if (st.move) await boardMove(page, st.move, st.board);
      else throw new Error('unbekannter Schritt');
      await settle();
      await page.waitForTimeout(250);
    } catch (e) {
      return `Schritt ${i + 1} (${what}): ${String(e.message).split('\n')[0].slice(0, 200)}`;
    }
  }
  return null;
}

/** Zug auf einem chessground-Brett: Feld anklicken, Zielfeld anklicken. Schwarz unten = `.orientation-black`. */
async function boardMove(page, uci, boardSel = 'cg-board') {
  const board = page.locator(boardSel).first();
  const box = await board.boundingBox({ timeout: 8000 });
  if (!box) throw new Error('Brett nicht sichtbar');
  const black = await board.evaluate(el => !!el.closest('.orientation-black'));
  for (const s of [uci.slice(0, 2), uci.slice(2, 4)]) {
    const p = squareCenter(box, s, black);
    await page.mouse.click(p.x, p.y);
    await page.waitForTimeout(150);
  }
}

/** Mitte eines Felds („e2") in Seitenkoordinaten; `black` = Brett von Schwarz aus gesehen (a1 oben rechts). */
export function squareCenter(box, square, black = false) {
  const f = square.charCodeAt(0) - 97; const r = Number(square[1]) - 1;
  if (!(f >= 0 && f < 8 && r >= 0 && r < 8)) throw new Error(`kein Feld: ${square}`);
  const col = black ? 7 - f : f; const row = black ? r : 7 - r;
  return { x: box.x + (col + 0.5) * box.width / 8, y: box.y + (row + 0.5) * box.height / 8 };
}
