import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { HandoffService } from '@rh/core/handoff.service';
import { PLAYER_CARD_API } from './player-card-api';
import { TRAINING_LINES_KEY, TrainingLines } from './training-lines';
import { TrainingLinesComponent } from './training-lines.component';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

const DATA: TrainingLines = {
  repertoires: [{ id: 7, name: 'Sizilianisch (Weiß)', colors: ['w'] }, { id: 9, name: 'Französisch', colors: ['w', 'b'] }],
  repertoire: 7, color: 'w', colors: ['w', 'b'], games: 12, total: 4, more: 1,
  lines: [
    { key: 'lA', end: 'x', start: null, chapter: 'Najdorf', repertoireId: 7, repertoireName: 'Sizilianisch (Weiß)', moves: ['e4', 'c5', 'Nf3', 'd6'], probability: 0.5, reached: 6, lastYear: 2025,
      neverReached: false, matched: 2, missing: 0, prefixProbability: 0.5, prefixReached: 6, source: 'own', ownMoves: 2, lichessMoves: 0, lichessFrom: null, pending: false },
    { key: 'lB', end: 'y', start: null, chapter: '', repertoireId: 7, repertoireName: 'Sizilianisch (Weiß)', moves: ['e4', 'e5', 'Nf3', 'Nc6', 'Bb5'], probability: 0.0004, reached: 1, lastYear: 2019,
      neverReached: false, matched: 2, missing: 0, prefixProbability: 0.0004, prefixReached: 1, source: 'own', ownMoves: 2, lichessMoves: 0, lichessFrom: null, pending: false },
    { key: 'lD', end: 'w', start: null, chapter: 'Drache', repertoireId: 7, repertoireName: 'Sizilianisch (Weiß)', moves: ['e4', 'c5', 'Nf3', 'd6', 'd4', 'cxd4', 'Nxd4', 'g6'], probability: 0, reached: 0,
      lastYear: null, neverReached: false, matched: 3, missing: 1, prefixProbability: 0.12, prefixReached: 4, source: 'none', ownMoves: 2, lichessMoves: 0, lichessFrom: null, pending: false },
    { key: 'lC', end: 'z', start: null, chapter: '', repertoireId: 9, repertoireName: 'Französisch', moves: ['e4', 'c6', 'd4', 'd5'], probability: 0, reached: 0, lastYear: null,
      neverReached: true, matched: 0, missing: 2, prefixProbability: 0, prefixReached: 12, source: 'none', ownMoves: 2, lichessMoves: 0, lichessFrom: null, pending: false },
  ],
};

describe('TrainingLinesComponent', () => {
  let fixture: ComponentFixture<TrainingLinesComponent>;
  let trainingLines: jasmine.Spy;
  let trainerParams: jasmine.Spy | undefined;
  let router: jasmine.SpyObj<Router>;
  let handoff: { rookHubUrl: string | null; jumpToRookHub: jasmine.Spy };
  let trainingRepertoire: jasmine.Spy;
  let confirmAsk: jasmine.Spy;
  let lang: string | undefined;
  const el = () => fixture.nativeElement as HTMLElement;
  const buttons = (text: string) => Array.from(el().querySelectorAll<HTMLButtonElement>('button')).filter(b => b.textContent?.includes(text));

  function build(withPrepParams = false): void {
    trainingLines = jasmine.createSpy('trainingLines').and.resolveTo(DATA);
    trainingRepertoire = jasmine.createSpy('trainingRepertoire').and.resolveTo({ id: 501, name: 'Prep: Huber, Franz 2026', lines: 4, replaced: false });
    confirmAsk = jasmine.createSpy('ask').and.returnValue(of(true));
    trainerParams = withPrepParams ? jasmine.createSpy('trainerParams').and.returnValue({ opponent: 'prep:42', all: 'true' }) : undefined;
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
    handoff = { rookHubUrl: null, jumpToRookHub: jasmine.createSpy('jumpToRookHub').and.resolveTo() };
    const api = { card: jasmine.createSpy(), profile: jasmine.createSpy(), recent: jasmine.createSpy(), tree: jasmine.createSpy(),
      pgn: jasmine.createSpy(), trainingLines, trainingRepertoire, ...(trainerParams ? { trainerParams } : {}) };
    const translate = { getCurrentLang: () => lang,
      instant: (k: string, p: Record<string, unknown>) => k === 'prep.trainingRepertoire.button' ? 'Show me lines to train'
        : k === 'prep.trainingRepertoire.confirm' ? `Create ${p['name']}?` : k };
    TestBed.configureTestingModule({ imports: [TrainingLinesComponent], providers: [
      { provide: PLAYER_CARD_API, useValue: api }, { provide: Router, useValue: router }, { provide: HandoffService, useValue: handoff },
      { provide: ConfirmService, useValue: { ask: confirmAsk } }, { provide: TranslateService, useValue: translate },
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
    lang = undefined;
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
    expect(items.length).toBe(4);
    expect(items[0].textContent).toContain('1.e4 c5 2.Sf3 d6');
    expect(items[0].textContent).toContain('Najdorf');
    expect(items[0].textContent).toContain('50 %');
    expect(items[0].textContent).toContain('6 Partien, zuletzt 2025');
    expect(items[1].textContent).toContain('3.Lb5');
    expect(items[1].textContent).toContain('<0,1 %');
    expect(items[1].textContent).toContain('1 Partie, zuletzt 2019');
    // aufgefüllt: nur der Anfang getroffen — „bis Zug … dabei" mit der Wahrscheinlichkeit des Anfangs
    expect(items[2].textContent).toContain('bis 3…cxd4 dabei');
    expect(items[2].textContent).toContain('Anfang 12 %, 4 Partien');
    expect(items[3].classList).toContain('never');
    expect(items[3].textContent).toContain('nie erreicht');
    expect(el().textContent).toContain('gezählt: 12 Partien von Huber mit Schwarz');
    expect(el().textContent).toContain('1 weitere Linie');
    // beide Farben im Repertoire: Umschalter
    expect(buttons('Ich habe Schwarz').length).toBe(1);
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
      repertoire: 9, color: 'w', chapterColors: { Hauptlinie: 'b' } }));
    expect(JSON.parse(localStorage.getItem(TRAINING_LINES_KEY)!)).toEqual({ repertoire: 9, color: 'w' });
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
    expect(el().querySelectorAll('.tl-list li').length).toBe(4);
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

  describe('„Show me lines to train" (Trainings-Repertoire)', () => {
    const createBtn = () => el().querySelector<HTMLButtonElement>('button.tl-create')!;

    const exists = () => new HttpErrorResponse({ status: 409, error: { reason: 'exists', id: 77, name: 'Prep: Huber, Franz 2026' } });

    it('noch kein „Prep: …": KEINE Rückfrage, legt an (replace false) und springt per Absprung hin (LeagueHub, deutsch)', async () => {
      build();
      handoff.rookHubUrl = 'https://rookhub.example';
      await openSection();
      expect(createBtn().textContent).toContain('Trainings-Repertoire anlegen');
      createBtn().click();
      await fixture.whenStable();
      expect(confirmAsk).not.toHaveBeenCalled();
      expect(trainingRepertoire).toHaveBeenCalledOnceWith('1606921', jasmine.objectContaining({
        repertoire: 7, color: 'w', filter: { source: 'both', speeds: ['blitz'], years: 3, withUnsure: false } }), false);
      expect(handoff.jumpToRookHub).toHaveBeenCalledWith('repertoires/501?trainColor=w');
    });

    it('es gibt schon eins (409 exists): Rückfrage mit Namen und „Ersetzen"/„Abbrechen"; Ersetzen schickt replace', async () => {
      build();
      trainingRepertoire.and.callFake((_k: string, _q: unknown, replace: boolean) =>
        replace ? Promise.resolve({ id: 77, name: 'Prep: Huber, Franz 2026', lines: 4, replaced: true }) : Promise.reject(exists()));
      await openSection();
      createBtn().click();
      await fixture.whenStable();
      const [message, , labels] = confirmAsk.calls.mostRecent().args;
      expect(message).toContain('Es gibt schon ein Repertoire „Prep: Huber, Franz 2026“');
      expect(message).toContain('Trainingsstand je Linie bleibt erhalten');
      expect(labels).toEqual({ confirm: 'Ersetzen', cancel: 'Abbrechen' });
      expect(trainingRepertoire.calls.allArgs().map(a => a[2])).toEqual([false, true]);
      expect(router.navigate).toHaveBeenCalledWith(['/repertoires', 77], { queryParams: { trainColor: 'w' } });
    });

    it('Abbrechen schreibt nichts (kein zweiter Aufruf), keine Fehlermeldung', async () => {
      build();
      trainingRepertoire.and.rejectWith(exists());
      confirmAsk.and.returnValue(of(false));
      await openSection();
      createBtn().click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(trainingRepertoire).toHaveBeenCalledTimes(1);
      expect(router.navigate).not.toHaveBeenCalled();
      expect(el().querySelector('.err')).toBeNull();
    });

    it('in RookHub in der Sprache der Oberfläche; anderer Fehler: sagt es', async () => {
      lang = 'en';
      build(true);
      await openSection();
      expect(createBtn().textContent).toContain('Show me lines to train');
      trainingRepertoire.and.rejectWith(new HttpErrorResponse({ status: 400 }));
      createBtn().click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el().querySelector('.err')).not.toBeNull();
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('Quelle heißt wie das Ziel (400 sameRepertoire): sagt genau das', async () => {
      build();
      trainingRepertoire.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'sameRepertoire' } }));
      await openSection();
      createBtn().click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el().querySelector('.err')?.textContent).toContain('würde sich selbst überschreiben');
      expect(el().querySelector('.err')?.textContent).toContain('Prep: Huber, Franz');
      expect(router.navigate).not.toHaveBeenCalled();
    });
  });

  describe('Vorgabe: alle markierten Repertoires (2026-10-07)', () => {
    const ALL: TrainingLines = { ...DATA, repertoire: null };

    it('„Alle markierten" ist vorgewählt, Linien tragen ihr Repertoire, „Alle trainieren" nur bei Einzelwahl (sonst Hinweis)', async () => {
      build();
      trainingLines.and.resolveTo(ALL);
      await openSection();
      const select = el().querySelector<HTMLSelectElement>('select.tl-rep')!;
      expect(select.options[0].textContent).toContain('Alle markierten');
      expect(select.options[0].selected).toBeTrue();
      expect(select.options.length).toBe(3);
      const items = Array.from(el().querySelectorAll('.tl-list li'));
      expect(items[0].querySelector('.tl-chapter')?.textContent).toContain('Sizilianisch (Weiß) · Najdorf');
      expect(items[3].querySelector('.tl-chapter')?.textContent).toContain('Französisch');
      expect(el().querySelector('button.tl-all')).toBeNull();
      expect(el().querySelector('.tl-all-hint')?.textContent).toContain('Trainings-Repertoire anlegen');
      expect(el().querySelector('button.tl-create')).not.toBeNull();

      // Einzelwahl: der Knopf ist da, der Hinweis weg
      trainingLines.and.resolveTo(DATA);
      select.value = '7';
      select.dispatchEvent(new Event('change'));
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el().querySelector('button.tl-all')).not.toBeNull();
      expect(el().querySelector('.tl-all-hint')).toBeNull();
    });

    it('Farbumschalter „Ich habe Weiß / Schwarz" fragt alle markierten mit der anderen Farbe', async () => {
      build();
      trainingLines.and.resolveTo(ALL);
      await openSection();
      expect(buttons('Ich habe Weiß').length).toBe(1);
      buttons('Ich habe Schwarz')[0].click();
      await fixture.whenStable();
      expect(trainingLines.calls.mostRecent().args[1]).toEqual(jasmine.objectContaining({ repertoire: null, color: 'b' }));
    });

    it('„Trainieren" öffnet den Trainer des Quell-Repertoires der Linie', async () => {
      build();
      trainingLines.and.resolveTo(ALL);
      await openSection();
      buttons('Trainieren').filter(b => b.classList.contains('tl-train'))[3].click();
      await fixture.whenStable();
      expect(router.navigate.calls.mostRecent().args[0]).toEqual(['/repertoires/9/train']);
    });

    it('eigene Kapitelfarben je Repertoire gehen beim ersten Laden nachträglich mit (einmal)', async () => {
      localStorage.setItem('rookhub_rep_train_chaptercolor_9', JSON.stringify({ Hauptlinie: 'b' }));
      build();
      trainingLines.and.resolveTo(ALL);
      await openSection();
      await fixture.whenStable();
      expect(trainingLines.calls.count()).toBe(2);
      expect(trainingLines.calls.mostRecent().args[1].chapterColors).toEqual({ '9': { Hauptlinie: 'b' } });
    });

    it('Anlegen aus allen markierten: repertoire null, Kapitelfarben je Repertoire', async () => {
      localStorage.setItem('rookhub_rep_train_chaptercolor_9', JSON.stringify({ Hauptlinie: 'b' }));
      build();
      trainingLines.and.resolveTo(ALL);
      await openSection();
      await fixture.whenStable();
      fixture.detectChanges();
      el().querySelector<HTMLButtonElement>('button.tl-create')!.click();
      await fixture.whenStable();
      expect(trainingRepertoire).toHaveBeenCalledOnceWith('1606921', jasmine.objectContaining({
        repertoire: null, color: 'w', chapterColors: { '9': { Hauptlinie: 'b' } } }), false);
    });
  });

  describe('Schätzung mit Lichess-Partien (2026-10-07)', () => {
    const EST: TrainingLines = {
      ...DATA, repertoire: null, games: 3, ownGames: 3, lichessBand: '2000–2300', explorerIncomplete: true,
      lines: [
        { ...DATA.lines[0], key: 'm1', moves: ['e4', 'c6', 'd4', 'd5', 'e5'], source: 'mixed', ownMoves: 1, lichessMoves: 1, lichessFrom: 3,
          probability: 0.62, reached: 0 },
        { ...DATA.lines[1], key: 'm2', source: 'lichess', ownMoves: 0, lichessMoves: 2, lichessFrom: 1, probability: 0.2 },
        { ...DATA.lines[2], key: 'm3', source: 'none', pending: true },
        { ...DATA.lines[1], key: 'm4', moves: ['e4', 'e5', 'Nf3', 'Nc6'], source: 'deviates', ownMoves: 0, lichessMoves: 2, lichessFrom: 1,
          probability: 0.36, deviationPly: 1, deviationSan: 'c5', deviationGames: 1 },
      ],
    };

    it('Kopfzeile sagt, dass Lücken geschätzt sind; je Linie „geschätzt" bzw. „ab 2…d5 geschätzt"; unvollständig angezeigt', async () => {
      build();
      trainingLines.and.resolveTo(EST);
      await openSection();
      expect(el().querySelector('.tl-estimate')?.textContent).toContain('Von Huber nur 3 passende Partien — Lücken mit Lichess-Partien der Stufe 2000–2300 geschätzt.');
      expect(el().querySelector('.tl-incomplete')?.textContent).toContain('unvollständig');
      const items = Array.from(el().querySelectorAll('.tl-list li'));
      expect(items[0].querySelector('.tl-tag')?.textContent).toContain('ab 2…d5 geschätzt (Lichess 2000–2300)');
      expect(items[0].textContent).toContain('62 %');
      expect(items[1].querySelector('.tl-tag')?.textContent?.trim()).toBe('geschätzt');
      expect(items[2].querySelector('.tl-tag-warn')?.textContent).toContain('Schätzung unvollständig');
      expect(items[2].textContent).toContain('bis 3…cxd4 dabei');            // ohne Quelle: Auffüllregel wie bisher
      // widerspricht ihm: „weicht ab: er spielt hier 1…c5 (1 Partie)" mit der Schätzung ab dort
      expect(items[3].querySelector('.tl-deviates')?.textContent).toContain('weicht ab: er spielt hier 1…c5 (1 Partie)');
      expect(items[3].textContent).toContain('≈ 36 %');
    });

    it('unvollständig: „… Stellungen offen", von selbst nach 3 s weiter (höchstens 3 Runden), „Weiter rechnen" fragt erneut', fakeAsync(() => {
      build();
      trainingLines.and.resolveTo({ ...EST, explorerPending: 120 });
      el().querySelector<HTMLButtonElement>('button.tl-toggle')!.click();
      flushMicrotasks();
      fixture.detectChanges();
      expect(trainingLines).toHaveBeenCalledTimes(1);
      expect(el().querySelector('.tl-incomplete')?.textContent).toContain('Schätzung unvollständig — 120 Stellungen offen.');
      for (const n of [2, 3, 4]) { tick(3000); flushMicrotasks(); expect(trainingLines).toHaveBeenCalledTimes(n); }
      tick(3000); flushMicrotasks();
      expect(trainingLines).toHaveBeenCalledTimes(4);                // nach 3 automatischen Runden Schluss
      fixture.detectChanges();
      el().querySelector<HTMLButtonElement>('button.tl-continue')!.click();
      flushMicrotasks();
      expect(trainingLines).toHaveBeenCalledTimes(5);
      expect(trainingLines.calls.mostRecent().args[1]).toEqual(jasmine.objectContaining({ repertoire: null, color: 'w' }));
      discardPeriodicTasks();
    }));

    it('vollständig: kein automatisches Nachladen', fakeAsync(() => {
      build();
      trainingLines.and.resolveTo({ ...EST, explorerIncomplete: false, explorerPending: 0 });
      el().querySelector<HTMLButtonElement>('button.tl-toggle')!.click();
      flushMicrotasks();
      tick(10_000); flushMicrotasks();
      expect(trainingLines).toHaveBeenCalledTimes(1);
    }));

    it('ganz ohne passende Partien: „ein typischer Spieler seiner Stärke"', async () => {
      build();
      trainingLines.and.resolveTo({ ...EST, games: 0, ownGames: 0, explorerIncomplete: false });
      await openSection();
      expect(el().querySelector('.tl-estimate')?.textContent).toContain('keine passenden Partien');
      expect(el().querySelector('.tl-incomplete')).toBeNull();
    });

    it('ohne Schätzung keine Kopfzeile', async () => {
      build();
      await openSection();
      expect(el().querySelector('.tl-estimate')).toBeNull();
    });
  });
});
