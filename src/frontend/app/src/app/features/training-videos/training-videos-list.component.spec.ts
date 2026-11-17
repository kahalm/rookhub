import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { TrainingVideosListComponent, displayFen, looksLikeFen, pickPart, sortPositionHits } from './training-videos-list.component';
import { TrainingLessonListItem, TrainingSearchHit, TrainingVideosService } from './training-videos.service';

function hit(p: Partial<TrainingSearchHit>): TrainingSearchHit {
  return { kind: 'segment', lessonId: 1, date: '2026-10-27', segmentId: 5, startSec: 730, fen: null, move: null, text: 'x',
    gameLabel: 'A – B', category: null, sameSide: null, ...p };
}

describe('TrainingVideosListComponent', () => {
  it('erkennt FEN (auch nur das Figurenfeld) und ergänzt fürs Brett Weiß am Zug', () => {
    expect(looksLikeFen('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1')).toBeTrue();
    expect(looksLikeFen('8/8/8/8/8/8/8/K6k')).toBeTrue();
    expect(looksLikeFen('Läuferopfer auf h7')).toBeFalse();
    expect(displayFen('8/8/8/8/8/8/8/K6k')).toBe('8/8/8/8/8/8/8/K6k w - - 0 1');
  });

  it('Stellungstreffer mit gleichem Zugrecht zuerst, sonst Reihenfolge des Servers', () => {
    const sorted = sortPositionHits([hit({ segmentId: 1, sameSide: false }), hit({ segmentId: 2, sameSide: true }), hit({ segmentId: 3, sameSide: null }), hit({ segmentId: 4, sameSide: true })]);
    expect(sorted.map(h => h.segmentId)).toEqual([2, 4, 1, 3]);
  });

  async function render(svc: Partial<TrainingVideosService>) {
    TestBed.configureTestingModule({
      imports: [TrainingVideosListComponent],
      providers: [provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: TrainingVideosService, useValue: svc }],
    });
    const fixture = TestBed.createComponent(TrainingVideosListComponent);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('zeigt die Stunden als Karten mit „Ansehen“ und „Nachspielen“; Ladefehler ist kein Leerzustand', async () => {
    const fixture = await render({
      list: () => of([{ id: 4, date: '2026-10-27', summary: 'Kurz', durationSec: 3725, hasVideo: true, segmentCount: 18,
        games: [{ key: 'g', kind: 'own', white: 'Anna', black: '?', event: null, year: null, eco: null, result: null, fromSec: null, toSec: null, rookhubGameId: null, matchConfidence: null, hasPgn: false }],
        ownerName: null }]),
    });
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('.lesson').length).toBe(1);
    expect(el.querySelector('.duration')!.textContent).toContain('1:02:05');
    expect(el.querySelector('.tv-watch')!.getAttribute('href')).toBe('/training-videos/4');
    expect(el.querySelector('.tv-drill')!.getAttribute('href')).toBe('/training-videos/4/drill');
    TestBed.resetTestingModule();
    const failed = await render({ list: () => throwError(() => new Error('x')) });
    expect((failed.nativeElement as HTMLElement).querySelector('app-load-error')).not.toBeNull();
    expect((failed.nativeElement as HTMLElement).querySelector('.empty-state')).toBeNull();
  });

  it('Textsuche erst ab drei Buchstaben; Treffer verlinken auf Zeit und Segment', async () => {
    const searchText = jasmine.createSpy('searchText').and.returnValue(of({ hits: [hit({ move: 'Nf3' })], truncated: false }));
    const fixture = await render({ list: () => of([]), searchText });
    const c = fixture.componentInstance;
    c.query.set('ab');
    c.submit();
    expect(searchText).not.toHaveBeenCalled();
    expect(c.hint()).toBe('trainingVideos.search.tooShort');
    c.query.set('Läufer');
    c.submit();
    await fixture.whenStable();
    expect(searchText).toHaveBeenCalledWith('Läufer');
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.tv-hit-watch')!.getAttribute('href')).toBe('/training-videos/1?t=730&seg=5');
    expect(el.querySelector('.tv-hit-drill')!.getAttribute('href')).toBe('/training-videos/1/drill?seg=5');
  });

  function item(id: number, part: 'private' | 'general'): TrainingLessonListItem {
    return { id, date: '2026-10-27', summary: null, durationSec: null, hasVideo: true, segmentCount: 1, games: [], ownerName: null, part };
  }

  it('Vorgabe-Teil: aus der Adresse, sonst privat — nur allgemeine Einträge → allgemein', () => {
    expect(pickPart('general', [item(1, 'private')])).toBe('general');
    expect(pickPart(null, [item(1, 'private'), item(2, 'general')])).toBe('private');
    expect(pickPart(null, [item(2, 'general')])).toBe('general');
    expect(pickPart('quatsch', [])).toBe('private');
    expect(pickPart(null, [{ ...item(3, 'private'), part: undefined }])).toBe('private');   // ältere Antwort ohne Feld
  });

  it('trennt private und allgemeine Partien per Umschalter mit Anzahl; jede Karte nennt den Teil', async () => {
    const fixture = await render({ list: () => of([item(1, 'private'), item(2, 'general'), item(3, 'general')]) });
    const el: HTMLElement = fixture.nativeElement;
    const toggles = el.querySelectorAll('mat-button-toggle');
    expect(toggles.length).toBe(2);
    expect(toggles[0].textContent).toContain('(1)');
    expect(toggles[1].textContent).toContain('(2)');
    expect(el.querySelectorAll('.lesson').length).toBe(1);
    expect(el.querySelector('.lesson .part-label')!.textContent).toContain('trainingVideos.part.private');
    fixture.componentInstance.selectPart('general');
    await fixture.whenStable();
    expect(el.querySelectorAll('.lesson').length).toBe(2);
    expect(el.querySelector('.lesson .part-label')!.textContent).toContain('trainingVideos.part.general');
  });
});
