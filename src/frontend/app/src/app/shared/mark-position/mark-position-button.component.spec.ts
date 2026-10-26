import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { MarkPositionButtonComponent } from './mark-position-button.component';
import { MarkedPositionsService, positionKey } from './marked-positions.service';

describe('MarkPositionButtonComponent', () => {
  const FEN = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1';
  const KEY = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3';

  function setup(loggedIn = true, auth: { currentUser: { userId: number } | null } = { currentUser: loggedIn ? { userId: 7 } : null }) {
    TestBed.configureTestingModule({
      imports: [MarkPositionButtonComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: auth }],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(MarkPositionButtonComponent);
    fixture.componentRef.setInput('fen', FEN);
    fixture.componentRef.setInput('origin', { context: 'mistake', savedGameId: 5, ply: 1, bestUci: 'e7e5' });
    fixture.detectChanges();
    return { fixture, http, el: fixture.nativeElement as HTMLElement };
  }

  it('Schlüssel ohne Zugzähler', () => {
    expect(positionKey(FEN)).toBe(KEY);
  });

  it('lädt die Markierungen einmal und zeigt eine markierte Stellung grün', () => {
    const { fixture, http, el } = setup();
    http.expectOne('/api/positions/marked').flush([{ id: 1, fen: FEN, positionKey: KEY, context: 'analysis', createdAt: '' }]);
    fixture.detectChanges();
    expect(el.querySelector('button.mark.on')).not.toBeNull();
    http.verify();
  });

  it('Klick markiert mit Herkunft; nochmal Klick nimmt zurück; Fehler dreht zurück', () => {
    const { fixture, http, el } = setup();
    http.expectOne('/api/positions/marked').flush([]);
    fixture.detectChanges();
    const btn = () => el.querySelector('button.mark') as HTMLButtonElement;

    btn().click(); fixture.detectChanges();
    const post = http.expectOne(r => r.method === 'POST' && r.url === '/api/positions/marked');
    expect(post.request.body).toEqual({ fen: FEN, context: 'mistake', savedGameId: 5, ply: 1, bestUci: 'e7e5' });
    expect(btn().classList).toContain('on');
    post.flush({});

    btn().click(); fixture.detectChanges();
    const del = http.expectOne(r => r.method === 'DELETE');
    expect(del.request.params.get('fen')).toBe(FEN);
    del.flush('fail', { status: 500, statusText: 'x' });
    fixture.detectChanges();
    expect(btn().classList).toContain('on');
    expect(TestBed.inject(MarkedPositionsService).isMarked(FEN)).toBeTrue();
  });

  // 0.847.0 (Wunsch 2026-10-26): auch ohne Anmeldung — der Server ordnet die Markierung der Besucher-Kennung zu.
  it('ohne Anmeldung: Knopf da, lädt und markiert ebenso', () => {
    const { fixture, http, el } = setup(false);
    http.expectOne('/api/positions/marked').flush([]);
    fixture.detectChanges();
    (el.querySelector('button.mark') as HTMLButtonElement).click();
    http.expectOne(r => r.method === 'POST' && r.url === '/api/positions/marked').flush({});
    http.verify();
  });

  it('nach dem Anmelden: holt die anonymen Markierungen ins Konto und lädt für das Konto neu', () => {
    const auth = { currentUser: null as { userId: number } | null };
    const { http } = setup(false, auth);
    http.expectOne('/api/positions/marked').flush([]);
    auth.currentUser = { userId: 7 };
    const service = TestBed.inject(MarkedPositionsService);
    let claimed = -1;
    service.claimSession('0123456789abcdef0123456789abcdef').subscribe(r => claimed = r.claimed);
    const claim = http.expectOne('/api/positions/marked/claim-session');
    expect(claim.request.body).toEqual({ sessionId: '0123456789abcdef0123456789abcdef' });
    claim.flush({ claimed: 1 });
    expect(claimed).toBe(1);
    http.expectOne('/api/positions/marked').flush([{ id: 1, fen: FEN, positionKey: KEY, context: 'analysis', createdAt: '' }]);
    expect(service.isMarked(FEN)).toBeTrue();
    service.ensureLoaded();                               // gleicher Aufrufer: keine weitere Anfrage
    http.verify();
  });

  it('die Beschriftung nennt die Stellung „besonders interessant"', () => {
    const { http, el } = setup();
    http.expectOne('/api/positions/marked').flush([]);
    expect(el.querySelector('button.mark')!.getAttribute('aria-label') ?? '').toContain('markPosition.mark');
  });
});
