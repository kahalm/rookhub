import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PartEditorComponent, PlyStep } from './part-editor.component';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('PartEditorComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PartEditorComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
  });

  function create(inputs: Record<string, unknown> = {}) {
    const fixture = TestBed.createComponent(PartEditorComponent);
    fixture.componentRef.setInput('fen', START);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return fixture;
  }

  const buttons = (el: HTMLElement) => Array.from(el.querySelectorAll<HTMLButtonElement>('button'));
  const byText = (el: HTMLElement, key: string) => buttons(el).find(b => b.textContent!.includes(key));

  it('blaettert per Leiste und meldet die Richtung', () => {
    const fixture = create({ plyShown: 1, plyTotal: 2, atEnd: false });
    const steps: PlyStep[] = [];
    fixture.componentInstance.goPly.subscribe(s => steps.push(s));
    const nav = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.ply-nav button'));
    nav.slice(0, 4).forEach(b => b.click());
    expect(steps).toEqual(['start', -1, 1, 'end']);
  });

  it('sperrt die Leiste an Anfang und Ende; „Ab hier" nur mitten in der Folge', () => {
    let el: HTMLElement = create({ plyShown: 0, plyTotal: 0, atEnd: true }).nativeElement;
    const nav = Array.from(el.querySelectorAll<HTMLButtonElement>('.ply-nav button'));
    expect(nav.every(b => b.disabled)).toBeTrue();
    expect(byText(el, 'reconstruct.continueHere')).toBeUndefined();

    const fixture = create({ plyShown: 1, plyTotal: 3, atEnd: false });
    el = fixture.nativeElement;
    let cut = 0;
    fixture.componentInstance.truncate.subscribe(() => cut++);
    byText(el, 'reconstruct.continueHere')!.click();
    expect(cut).toBe(1);
  });

  it('meldet getippte Zuege und zeigt den unmoeglichen Zug', async () => {
    const fixture = create({ moves: 'e4', badMove: 'Qh9' });
    const typed: string[] = [];
    fixture.componentInstance.movesInput.subscribe(t => typed.push(t));
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.bad-hint')?.textContent).toContain('reconstruct.badMove');
    const area = el.querySelector('textarea')!;
    area.value = 'e4 e5';
    area.dispatchEvent(new Event('input'));
    expect(typed).toEqual(['e4 e5']);
  });

  it('Notiz zweiseitig', async () => {
    const fixture = create({ note: 'alt' });
    await fixture.whenStable();
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[maxlength="500"]')!;
    input.value = 'neu';
    input.dispatchEvent(new Event('input'));
    expect(fixture.componentInstance.note()).toBe('neu');
  });

  it('Speichern gesperrt bei einem Vorschlag, „Stellung eingeben" nur fuer ein neues Teil', () => {
    let el: HTMLElement = create({ generated: true }).nativeElement;
    expect(byText(el, 'common.save')!.disabled).toBeTrue();
    expect(byText(el, 'reconstruct.enterPosition')).toBeUndefined();
    expect(el.querySelector<HTMLElement>('.side-row')!.hidden).toBeTrue();

    const fixture = create({ isNew: true, moves: 'e4' });
    el = fixture.nativeElement;
    const events: string[] = [];
    const c = fixture.componentInstance;
    c.save.subscribe(() => events.push('save'));
    c.toPosition.subscribe(() => events.push('toPosition'));
    c.cancel.subscribe(() => events.push('cancel'));
    c.undo.subscribe(() => events.push('undo'));
    byText(el, 'common.save')!.click();
    byText(el, 'reconstruct.enterPosition')!.click();
    byText(el, 'common.cancel')!.click();
    byText(el, 'reconstruct.undoMove')!.click();
    expect(events).toEqual(['save', 'toPosition', 'cancel', 'undo']);
  });

  it('Zuruecknehmen nur mit Zuegen', () => {
    expect(byText(create({ moves: '  ' }).nativeElement, 'reconstruct.undoMove')!.disabled).toBeTrue();
  });

  it('meldet die Seite am Zug', () => {
    const fixture = create({ sideBlack: false });
    const sides: boolean[] = [];
    fixture.componentInstance.sideChange.subscribe(b => sides.push(b));
    const toggles = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.side-row button');
    toggles[1].click();
    expect(sides).toEqual([true]);
  });

  it('fokussiert den Brett-Kasten', () => {
    const fixture = create();
    document.body.appendChild(fixture.nativeElement);
    try {
      fixture.componentInstance.focusBoard();
      expect(document.activeElement?.classList).toContain('board-wrap');
    } finally {
      (fixture.nativeElement as HTMLElement).remove();
    }
  });
});
