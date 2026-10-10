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

  function setup(loggedIn = true) {
    TestBed.configureTestingModule({
      imports: [MarkPositionButtonComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } }],
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

  it('ohne Anmeldung kein Knopf und keine Anfrage', () => {
    const { http, el } = setup(false);
    expect(el.querySelector('button')).toBeNull();
    http.verify();
  });
});
