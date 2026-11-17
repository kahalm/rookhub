import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { TrainingDrillComponent } from './training-drill.component';
import { TrainingLessonComponent } from './training-lesson.component';
import { TrainingSharePanelComponent } from './training-share-panel.component';
import { TrainingLesson, TrainingShare, TrainingVideosService } from './training-videos.service';

const FEN = 'r1bqkbnr/pp3ppp/2n1p3/2pp4/3PP3/5N2/PPPN1PPP/R1BQKB1R w KQkq - 2 5';

function lesson(p: Partial<TrainingLesson> = {}): TrainingLesson {
  return {
    id: 7, date: '2026-10-27', summary: 'Französisch', durationSec: 3737, hasVideo: true, ownerName: null,
    games: [{ key: 'g1', kind: 'own', white: 'A', black: 'B', event: null, year: null, eco: null, result: null, fromSec: 10, toSec: null,
      rookhubGameId: 4711, matchConfidence: 'high', hasPgn: false }],
    segments: [{ id: 1, startSec: 272, endSec: 300, gameKey: 'g1', fen: FEN, move: 'Bb5', moveKind: 'played', comment: 'Läuferzug',
      quote: null, variations: null, tags: [], hasQuestion: false, quoteStartSec: null, quoteEndSec: null, question: null, progress: null,
      taskFen: FEN, taskMove: 'Bb5' }],
    pgns: {}, analysisPgns: {}, part: 'private', otherParts: [{ id: 8, part: 'general' }], ...p,
  };
}

const share = (id: number, token: string): TrainingShare => ({ id, token, createdAt: '2026-11-17T10:00:00Z', expiresAt: '2026-11-24T10:00:00Z' });

function routeFor(params: Record<string, string>, data: Record<string, unknown> = {}) {
  return { snapshot: { paramMap: convertToParamMap(params), queryParamMap: convertToParamMap({}), data }, paramMap: of(convertToParamMap(params)) };
}

describe('Trainerstunden teilen', () => {
  let snack: jasmine.SpyObj<SnackbarService>;

  function setup(svc: Partial<TrainingVideosService>, route: unknown, confirm = true) {
    snack = jasmine.createSpyObj<SnackbarService>('SnackbarService', ['warn', 'copy', 'info']);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: TrainingVideosService, useValue: { shareUrl: (t: string) => `https://x/tl/${t}`, sharedVideoUrl: (t: string) => `/api/training-videos/shared/${t}/video`, ...svc } },
        { provide: ActivatedRoute, useValue: route },
        { provide: SnackbarService, useValue: snack },
        { provide: ConfirmService, useValue: { ask: () => of(confirm) } }],
    });
  }

  describe('TrainingSharePanelComponent', () => {
    async function render(svc: Partial<TrainingVideosService>, confirm = true) {
      setup(svc, routeFor({}), confirm);
      const fixture = TestBed.createComponent(TrainingSharePanelComponent);
      fixture.componentRef.setInput('lessonId', 7);
      fixture.autoDetectChanges();
      await fixture.whenStable();
      return fixture;
    }

    it('zeigt aktive Links mit Ablauf; „Link erstellen“ nimmt die gewählte Frist (Vorgabe 7) und stellt den neuen nach oben', async () => {
      const createShare = jasmine.createSpy('createShare').and.returnValue(of(share(2, 'neu')));
      const fixture = await render({ shares: () => of([share(1, 'alt')]), createShare });
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelectorAll('.link').length).toBe(1);
      expect(el.querySelector('.url')!.textContent).toContain('https://x/tl/alt');
      expect(fixture.componentInstance.days()).toBe(7);
      fixture.componentInstance.days.set(30);
      (el.querySelector('.tv-share-create') as HTMLButtonElement).click();
      await fixture.whenStable();
      expect(createShare).toHaveBeenCalledWith(7, 30);
      expect(Array.from(el.querySelectorAll('.url')).map(x => x.textContent)).toEqual(['https://x/tl/neu', 'https://x/tl/alt']);
    });

    it('Widerrufen fragt nach und nimmt den Link aus der Liste; abgelehnt bleibt er', async () => {
      const revokeShare = jasmine.createSpy('revokeShare').and.returnValue(of(undefined));
      let fixture = await render({ shares: () => of([share(1, 'a'), share(2, 'b')]), revokeShare }, false);
      (fixture.nativeElement.querySelector('.tv-share-revoke') as HTMLButtonElement).click();
      expect(revokeShare).not.toHaveBeenCalled();
      TestBed.resetTestingModule();
      fixture = await render({ shares: () => of([share(1, 'a'), share(2, 'b')]), revokeShare });
      (fixture.nativeElement.querySelector('.tv-share-revoke') as HTMLButtonElement).click();
      await fixture.whenStable();
      expect(revokeShare).toHaveBeenCalledWith(7, 1);
      expect(fixture.componentInstance.shares().map(s => s.id)).toEqual([2]);
    });

    it('Ladefehler ist kein Leerzustand; Fehler beim Anlegen wird gemeldet', async () => {
      const fixture = await render({ shares: () => throwError(() => new Error('x')), createShare: () => throwError(() => new HttpErrorResponse({ status: 500 })) });
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('.error')).not.toBeNull();
      expect(el.querySelector('.none')).toBeNull();
      fixture.componentInstance.create();
      expect(snack.warn).toHaveBeenCalled();
    });
  });

  describe('Stundenseite über /tl/:token', () => {
    async function render(svc: Partial<TrainingVideosService>, params: Record<string, string>, data: Record<string, unknown>) {
      setup(svc, routeFor(params, data));
      const fixture = TestBed.createComponent(TrainingLessonComponent);
      fixture.autoDetectChanges();
      await fixture.whenStable();
      return fixture;
    }

    it('Gast: Stunde über das Token, Video über den Teilen-Weg, kein Zurück/anderer Teil/eigene Partie/Teilen; Nachspielen bleibt am Link', async () => {
      const getShared = jasmine.createSpy('getShared').and.returnValue(of(lesson({ otherParts: [], games: [{ ...lesson().games[0], rookhubGameId: null }] })));
      const accessToken = jasmine.createSpy('accessToken');
      const get = jasmine.createSpy('get');
      const fixture = await render({ getShared, accessToken, get }, { token: 'tok' }, { mode: 'share' });
      const el: HTMLElement = fixture.nativeElement;
      expect(getShared).toHaveBeenCalledWith('tok');
      expect(get).not.toHaveBeenCalled();
      expect(accessToken).not.toHaveBeenCalled();
      expect(el.querySelector('video')!.getAttribute('src')).toBe('/api/training-videos/shared/tok/video');
      expect(el.querySelector('a[href="/training-videos"]')).toBeNull();
      expect(el.querySelector('.tv-other-part')).toBeNull();
      expect(el.querySelector('.tv-game-open')).toBeNull();
      expect(el.querySelector('app-training-share-panel')).toBeNull();
      expect(el.querySelector('.guest-note')).not.toBeNull();
      expect(el.querySelector('.tv-drill')!.getAttribute('href')).toBe('/tl/tok/drill');
      expect(el.textContent).toContain('Läuferzug');
    });

    it('abgelaufener oder widerrufener Link (410) und unbekannter (404) → „Link abgelaufen“, kein Weg zur Liste', async () => {
      for (const status of [410, 404]) {
        TestBed.resetTestingModule();
        const fixture = await render({ getShared: () => throwError(() => new HttpErrorResponse({ status })) }, { token: 'weg' }, { mode: 'share' });
        const el: HTMLElement = fixture.nativeElement;
        expect(el.querySelector('.tv-share-expired')).not.toBeNull();
        expect(el.textContent).toContain('trainingVideos.share.expiredTitle');
        expect(el.querySelector('a[href="/training-videos"]')).toBeNull();
      }
    });

    it('Besitzer: Teilen-Feld da; Admin bei fremder Stunde (ownerName) nicht', async () => {
      const svc = { get: () => of(lesson()), accessToken: () => of('t'), videoUrl: () => '/v', shares: () => of([]) };
      let fixture = await render(svc, { id: '7' }, {});
      expect(fixture.nativeElement.querySelector('app-training-share-panel')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('.tv-game-open')).not.toBeNull();
      TestBed.resetTestingModule();
      fixture = await render({ ...svc, get: () => of(lesson({ ownerName: 'patrik' })) }, { id: '7' }, {});
      expect(fixture.nativeElement.querySelector('app-training-share-panel')).toBeNull();
    });
  });

  describe('Nachspielen über /tl/:token/drill', () => {
    it('lädt über das Token und speichert KEINEN Stand', async () => {
      const saveProgress = jasmine.createSpy('saveProgress');
      setup({ getShared: () => of(lesson()), saveProgress }, routeFor({ token: 'tok' }, { mode: 'share' }));
      const fixture = TestBed.createComponent(TrainingDrillComponent);
      fixture.autoDetectChanges();
      await fixture.whenStable();
      const c = fixture.componentInstance;
      expect(c.items().length).toBe(1);
      c.skip();
      await fixture.whenStable();
      expect(saveProgress).not.toHaveBeenCalled();
      expect(c.status()).toBe('skipped');
      expect(c.backLink()).toEqual(['/tl', 'tok']);
    });
  });
});
