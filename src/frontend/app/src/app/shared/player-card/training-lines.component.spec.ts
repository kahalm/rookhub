import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { HandoffService } from '@rh/core/handoff.service';
import { PLAYER_CARD_API } from './player-card-api';
import { TRAINING_LINES_KEY, TrainingLines } from './training-lines';
import { TrainingLinesComponent } from './training-lines.component';

const DATA: TrainingLines = {
  repertoires: [{ id: 7, name: 'Sizilianisch (Weiß)' }, { id: 9, name: 'Französisch' }],
  repertoire: 7, color: 'w', colors: ['w', 'b'], games: 12, total: 3, more: 1,
  lines: [
    { key: 'lA', end: 'x', start: null, chapter: 'Najdorf', moves: ['e4', 'c5', 'Nf3', 'd6'], probability: 0.5, reached: 6, lastYear: 2025, neverReached: false },
    { key: 'lB', end: 'y', start: null, chapter: '', moves: ['e4', 'e5', 'Nf3', 'Nc6', 'Bb5'], probability: 0.0004, reached: 1, lastYear: 2019, neverReached: false },
    { key: 'lC', end: 'z', start: null, chapter: '', moves: ['e4', 'c6', 'd4', 'd5'], probability: 0, reached: 0, lastYear: null, neverReached: true },
  ],
};

describe('TrainingLinesComponent', () => {
  let fixture: ComponentFixture<TrainingLinesComponent>;
  let trainingLines: jasmine.Spy;
  let trainerParams: jasmine.Spy | undefined;
  let router: jasmine.SpyObj<Router>;
  let handoff: { rookHubUrl: string | null; jumpToRookHub: jasmine.Spy };
  const el = () => fixture.nativeElement as HTMLElement;
  const buttons = (text: string) => Array.from(el().querySelectorAll<HTMLButtonElement>('button')).filter(b => b.textContent?.includes(text));

  function build(withPrepParams = false): void {
    trainingLines = jasmine.createSpy('trainingLines').and.resolveTo(DATA);
    trainerParams = withPrepParams ? jasmine.createSpy('trainerParams').and.returnValue({ opponent: 'prep:42', all: 'true' }) : undefined;
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
    handoff = { rookHubUrl: null, jumpToRookHub: jasmine.createSpy('jumpToRookHub').and.resolveTo() };
    const api = { card: jasmine.createSpy(), profile: jasmine.createSpy(), recent: jasmine.createSpy(), tree: jasmine.createSpy(),
      pgn: jasmine.createSpy(), trainingLines, ...(trainerParams ? { trainerParams } : {}) };
    TestBed.configureTestingModule({ imports: [TrainingLinesComponent], providers: [
      { provide: PLAYER_CARD_API, useValue: api }, { provide: Router, useValue: router }, { provide: HandoffService, useValue: handoff },
    ] });
    fixture = TestBed.createComponent(TrainingLinesComponent);
    fixture.componentRef.setInput('key', withPrepParams ? '42' : '1606921');
    fixture.componentRef.setInput('name', 'Huber, Franz');
    fixture.componentRef.setInput('filter', { source: 'both', speeds: ['blitz'], years: 3, withUnsure: false });
    fixture.detectChanges();
  }

  async function openSection(): Promise<void> {
    el().querySelector<HTMLButtonElement>('button.tl-toggle')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    localStorage.removeItem(TRAINING_LINES_KEY);
    localStorage.removeItem('rookhub_rep_train_chaptercolor_9');
  });
  afterEach(() => {
    localStorage.removeItem(TRAINING_LINES_KEY);
    localStorage.removeItem('rookhub_rep_train_chaptercolor_9');
  });

  it('lädt erst beim Aufklappen; Linien wahrscheinlichste zuerst, deutsch notiert, „nie erreicht" hinten, „weitere"', async () => {
    build();
    expect(trainingLines).not.toHaveBeenCalled();
    expect(el().querySelector('button.tl-toggle')!.textContent).toContain('Trainingslinien gegen Huber');
    await openSection();
    expect(trainingLines).toHaveBeenCalledOnceWith('1606921', jasmine.objectContaining({
      repertoire: null, color: null, filter: { source: 'both', speeds: ['blitz'], years: 3, withUnsure: false } }));
    const items = Array.from(el().querySelectorAll('.tl-list li'));
    expect(items.length).toBe(3);
    expect(items[0].textContent).toContain('1.e4 c5 2.Sf3 d6');
    expect(items[0].textContent).toContain('Najdorf');
    expect(items[0].textContent).toContain('50 %');
    expect(items[0].textContent).toContain('6 Partien, zuletzt 2025');
    expect(items[1].textContent).toContain('3.Lb5');
    expect(items[1].textContent).toContain('<0,1 %');
    expect(items[1].textContent).toContain('1 Partie, zuletzt 2019');
    expect(items[2].classList).toContain('never');
    expect(items[2].textContent).toContain('nie erreicht');
    expect(el().textContent).toContain('gezählt: 12 Partien von Huber mit Schwarz');
    expect(el().textContent).toContain('1 weitere Linie');
    // beide Farben im Repertoire: Umschalter
    expect(buttons('Ich mit Schwarz').length).toBe(1);
  });

  it('ohne freigegebenes Repertoire: sagt, wo man es einschaltet', async () => {
    build();
    trainingLines.and.resolveTo({ ...DATA, repertoires: [], repertoire: null, color: null, colors: [], lines: [], total: 0, more: 0, games: 0 });
    await openSection();
    expect(el().textContent).toContain('Für Extension und Vorbereitung verwenden');
    expect(el().querySelector('.tl-list')).toBeNull();
  });

  it('Repertoire wechseln: schickt dessen eigene Kapitelfarben mit und merkt sich die Wahl', async () => {
    build();
    await openSection();
    localStorage.setItem('rookhub_rep_train_chaptercolor_9', JSON.stringify({ Hauptlinie: 'b' }));
    trainingLines.and.resolveTo({ ...DATA, repertoire: 9 });
    const select = el().querySelector('select')!;
    select.value = '9';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(trainingLines.calls.mostRecent().args[1]).toEqual(jasmine.objectContaining({
      repertoire: 9, color: null, chapterColors: { Hauptlinie: 'b' } }));
    expect(JSON.parse(localStorage.getItem(TRAINING_LINES_KEY)!)).toEqual({ repertoire: 9, color: null });
  });

  it('das gemerkte Repertoire gibt es nicht mehr (404): einmal ohne Vorgabe', async () => {
    localStorage.setItem(TRAINING_LINES_KEY, JSON.stringify({ repertoire: 99, color: 'b' }));
    build();
    trainingLines.and.callFake((_k: string, q: { repertoire: number | null }) =>
      q.repertoire === 99 ? Promise.reject(new HttpErrorResponse({ status: 404 })) : Promise.resolve(DATA));
    await openSection();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(trainingLines.calls.count()).toBe(2);
    expect(trainingLines.calls.mostRecent().args[1].repertoire).toBeNull();
    expect(el().querySelectorAll('.tl-list li').length).toBe(3);
    expect(el().querySelector('.err')).toBeNull();
  });

  it('Fehler: sagt es', async () => {
    build();
    trainingLines.and.rejectWith(new HttpErrorResponse({ status: 500 }));
    await openSection();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el().querySelector('.err')?.textContent).toContain('konnten nicht geladen werden');
  });

  it('in RookHub: „Trainieren" öffnet den Trainer mit ?line=, „Alle" mit der Reihenfolge des Gegners', async () => {
    build(true);
    await openSection();
    buttons('Trainieren').find(b => b.classList.contains('tl-train'))!.click();
    await fixture.whenStable();
    expect(router.navigate).toHaveBeenCalledWith(['/repertoires/7/train'], { queryParams: {
      opponent: 'prep:42', all: 'true', color: 'w', source: 'both', speeds: 'blitz', years: '3', line: 'lA', chapter: 'Najdorf' } });
    buttons('Alle in dieser Reihenfolge')[0].click();
    await fixture.whenStable();
    expect(router.navigate.calls.mostRecent().args[1]).toEqual({ queryParams: {
      opponent: 'prep:42', all: 'true', color: 'w', source: 'both', speeds: 'blitz', years: '3' } });
    expect(handoff.jumpToRookHub).not.toHaveBeenCalled();
  });

  it('in LeagueHub: per Einmal-Code hinüber nach RookHub, Gegner = league:<FIDE>', async () => {
    build();
    handoff.rookHubUrl = 'https://rookhub.example';
    await openSection();
    buttons('Trainieren').find(b => b.classList.contains('tl-train'))!.click();
    await fixture.whenStable();
    const path = handoff.jumpToRookHub.calls.mostRecent().args[0] as string;
    expect(path.startsWith('repertoires/7/train?')).toBeTrue();
    const q = new URLSearchParams(path.split('?')[1]);
    expect(q.get('opponent')).toBe('league:1606921');
    expect(q.get('line')).toBe('lA');
    expect(q.get('color')).toBe('w');
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('am Handy (390 px) läuft nichts quer über', async () => {
    build();
    (fixture.nativeElement as HTMLElement).style.display = 'block';
    (fixture.nativeElement as HTMLElement).style.width = '358px';
    await openSection();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.scrollWidth).toBeLessThanOrEqual(host.clientWidth);
  });
});
