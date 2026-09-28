import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { GameReplayComponent } from './game-replay.component';

const PGN = '[Event "TMM Landesliga"]\n[Date "2024.05.12"]\n[White "Oberschmid, Patrik"]\n[Black "Hengl, Philip"]\n[Result "1-0"]\n\n'
  + '1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 1-0\n';

describe('GameReplayComponent', () => {
  let fixture: ComponentFixture<GameReplayComponent>;
  const el = () => fixture.nativeElement as HTMLElement;

  function create(pgn: string, flipped = false, evalsUrl: string | null = null): GameReplayComponent {
    TestBed.configureTestingModule({ imports: [GameReplayComponent],
      providers: [provideTranslateService({ fallbackLang: 'de' }), provideHttpClient(), provideHttpClientTesting()] });
    fixture = TestBed.createComponent(GameReplayComponent);
    fixture.componentRef.setInput('pgn', pgn);
    fixture.componentRef.setInput('flipped', flipped);
    fixture.componentRef.setInput('evalsUrl', evalsUrl);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('zeigt Kopf und Züge deutsch; Knöpfe, Pfeiltasten und Klick blättern', () => {
    const c = create(PGN);
    expect(el().querySelector('.replay-head')?.textContent).toContain('Oberschmid, Patrik – Hengl, Philip');
    expect(el().querySelector('.replay-head')?.textContent).toContain('TMM Landesliga · 12.05.2024');
    expect(Array.from(el().querySelectorAll('.mv')).map(b => b.textContent)).toEqual(['e4', 'c5', 'Sf3', 'd6', 'd4', 'cxd4', 'Sxd4', 'Sf6']);
    expect(c.index()).toBe(-1);
    const box = el().querySelector('.replay') as HTMLElement;
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(c.index()).toBe(1);
    expect(c.last()).toEqual(['c7', 'c5']);
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'End' }));
    expect(c.index()).toBe(7);
    fixture.detectChanges();
    expect(el().querySelector('.mv.on')?.textContent).toBe('Sf6');
    (el().querySelectorAll('.mv')[2] as HTMLButtonElement).click();
    expect(c.index()).toBe(2);
    expect(c.fen()).toContain('rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b');
    (el().querySelector('[aria-label="Zum Anfang"]') as HTMLButtonElement).click();
    expect(c.index()).toBe(-1);
    fixture.detectChanges();
    expect(el().textContent).toContain('1–0');
  });

  it('ohne Züge bzw. unlesbar: ein Satz statt eines leeren Bretts', () => {
    create('[White "A"]\n[Black "B"]\n[Result "*"]\n\n*\n');
    expect(el().textContent).toContain('keine Züge');
    expect(el().querySelector('app-chess-board')).toBeNull();
  });
  // Vereinspartien (0.593.0): mit der Adresse der Bewertungen steht RookHubs Rückblick darunter — ohne „Warum war das ein
  // Fehler?", den Endpunkt gibt es für Vereinspartien nicht (sonst ein 404 je Partie).
  it('mit evalsUrl: Rückblick darunter, lädt die Bewertungen, fragt keine Erklärungen nach', () => {
    create(PGN, false, '/api/league/club/games/7/evals');
    const http = TestBed.inject(HttpTestingController);
    expect(el().querySelector('app-game-review')).not.toBeNull();
    http.expectOne('/api/league/club/games/7/evals').flush({ status: 'done', analyzed: 8, total: 8, targetDepth: 20, analysisId: 3,
      plies: [], final: null, bookPlies: [], refining: false, refined: 0 });
    fixture.detectChanges();
    http.expectNone(r => r.url.includes('/explanations'));
    http.verify();
  });

  it('ohne evalsUrl (Spielerkarte): nur nachspielen, keine Anfrage', () => {
    create(PGN);
    expect(el().querySelector('app-game-review')).toBeNull();
    TestBed.inject(HttpTestingController).verify();
  });
});
