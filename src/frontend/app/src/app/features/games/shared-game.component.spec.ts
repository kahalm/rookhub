import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { SharedGameComponent } from './shared-game.component';

describe('SharedGameComponent', () => {
  async function setup() {
    await TestBed.configureTestingModule({
      imports: [SharedGameComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    return { fixture: TestBed.createComponent(SharedGameComponent), http: TestBed.inject(HttpTestingController) };
  }

  const sharedGame = (ownerSide: 'white' | 'black' | null) => ({
    source: 'lichess', white: 'a', black: 'b', result: '0-1',
    pgn: '[White "a"]\n[Black "b"]\n\n1. e4 c5 0-1', createdAt: '2026-07-16T00:00:00Z', ownerSide,
  });

  it('creates (template AOT-compiles + DI resolves)', async () => {
    const { fixture } = await setup();
    expect(fixture.componentInstance).toBeTruthy();
  });

  // Teilender spielte Schwarz → Brett startet aus seiner Sicht gedreht (Flip-Knopf bleibt nutzbar).
  it('starts flipped when the sharer played black (ownerSide=black)', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges(); // ngOnInit → GET /api/games/shared/…
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(sharedGame('black'));
    expect(fixture.componentInstance.flipped).toBeTrue();
  });

  it('starts unflipped for ownerSide=white or unknown', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(sharedGame(null));
    expect(fixture.componentInstance.flipped).toBeFalse();
  });
});
