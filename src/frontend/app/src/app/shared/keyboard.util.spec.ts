import { isBoardHotkey } from './keyboard.util';

describe('isBoardHotkey', () => {
  const made: HTMLElement[] = [];
  afterEach(() => { made.splice(0).forEach(el => el.remove()); });

  /** Ein Element im Dokument (isContentEditable braucht ein eingehaengtes Element). */
  function el<K extends keyof HTMLElementTagNameMap>(tag: K, parent: HTMLElement = document.body): HTMLElementTagNameMap[K] {
    const e = document.createElement(tag);
    parent.appendChild(e);
    if (parent === document.body) made.push(e);
    return e;
  }

  function press(target: unknown, init: KeyboardEventInit = {}): KeyboardEvent {
    const e = new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true, ...init });
    if (target !== undefined) Object.defineProperty(e, 'target', { value: target });
    return e;
  }

  /** Ein offenes Overlay-Fenster wie von MatDialog/MatMenu: Container > Fenster > Inhalt. */
  function pane(): HTMLElement {
    const container = el('div');
    container.className = 'cdk-overlay-container';
    const p = el('div', container);
    p.className = 'cdk-overlay-pane';
    return p;
  }

  it('ohne Ziel, auf dem Rumpf der Seite oder einem Knopf der Seite gehoert die Taste dem Brett', () => {
    expect(isBoardHotkey(press(undefined))).toBeTrue();
    expect(isBoardHotkey(press(document.body))).toBeTrue();
    expect(isBoardHotkey(press(el('button')))).toBeTrue();
    expect(isBoardHotkey(press(window))).toBeTrue();
  });

  it('schon verarbeitet (mat-select/mat-menu rufen preventDefault) -> nicht noch einmal', () => {
    const e = press(document.body);
    e.preventDefault();
    expect(e.defaultPrevented).toBeTrue();
    expect(isBoardHotkey(e)).toBeFalse();
  });

  it('Alt/Strg/Cmd gehoeren dem Browser, Shift nicht', () => {
    expect(isBoardHotkey(press(document.body, { altKey: true }))).toBeFalse();
    expect(isBoardHotkey(press(document.body, { ctrlKey: true }))).toBeFalse();
    expect(isBoardHotkey(press(document.body, { metaKey: true }))).toBeFalse();
    expect(isBoardHotkey(press(document.body, { shiftKey: true }))).toBeTrue();
  });

  it('in Eingabefeldern gehoeren die Tasten dem Cursor', () => {
    expect(isBoardHotkey(press(el('input')))).toBeFalse();
    expect(isBoardHotkey(press(el('textarea')))).toBeFalse();
    expect(isBoardHotkey(press(el('select')))).toBeFalse();
    const editable = el('div');
    editable.contentEditable = 'true';
    expect(isBoardHotkey(press(el('span', editable)))).toBeFalse();
    // Nachgebaute Ziele aus Specs (nur tagName), gross wie klein geschrieben.
    expect(isBoardHotkey(press({ tagName: 'INPUT' }))).toBeFalse();
    expect(isBoardHotkey(press({ tagName: 'textarea' }))).toBeFalse();
    expect(isBoardHotkey(press({ tagName: 'DIV' }))).toBeTrue();
  });

  it('im offenen Menue/Dialog bewegen die Tasten die Auswahl, nicht das Brett', () => {
    const item = el('button', pane());
    expect(isBoardHotkey(press(item))).toBeFalse();
    // auch ohne eigenes Fenster-Element (nur der Container)
    const bare = el('div');
    bare.className = 'cdk-overlay-container';
    expect(isBoardHotkey(press(el('button', bare)))).toBeFalse();
  });

  it('eine Komponente IM Dialog (host) behaelt ihre Tasten, ein Menue darueber nicht', () => {
    const dialogPane = pane();
    const host = el('div', dialogPane);
    const ownButton = el('button', host);
    expect(isBoardHotkey(press(ownButton), host)).withContext('Knopf im eigenen Fenster').toBeTrue();
    expect(isBoardHotkey(press(dialogPane), host)).withContext('Fokus auf dem Dialog-Rahmen').toBeTrue();
    const menuItem = el('button', pane());
    expect(isBoardHotkey(press(menuItem), host)).withContext('Menue in einem anderen Fenster').toBeFalse();
    expect(isBoardHotkey(press(el('input', host)), host)).withContext('Eingabe im eigenen Fenster').toBeFalse();
    // Eine Seite (host ausserhalb jedes Overlays) bekommt die Tasten des Dialogs nicht.
    const page = el('div');
    expect(isBoardHotkey(press(ownButton), page)).toBeFalse();
  });
});
