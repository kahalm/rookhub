import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { HandoffService } from '@rh/core/handoff.service';
import { LineupGame, LineupsApiService } from '../core/lineups';
import { BoardGameComponent } from './board-game.component';

// Erfundene Spieler.
const GAME: LineupGame = { source: 'club', clubGameId: 168, plies: 61, result: '1/2-1/2', white: 'Ackermann, Anna', black: 'Brunner, Bert',
  firstMoves: ['d4', 'Nf6', 'c4', 'e6'], canEdit: true };

describe('BoardGameComponent', () => {
  let fixture: ComponentFixture<BoardGameComponent>;

  function render(game: LineupGame, rookHub: string | null): HTMLElement {
    fixture = TestBed.createComponent(BoardGameComponent);
    (fixture.componentInstance as { rookHub: string | null }).rookHub = rookHub;
    fixture.componentRef.setInput('game', game);
    fixture.componentRef.setInput('tnr', 4711);
    fixture.componentRef.setInput('round', 2);
    fixture.componentRef.setInput('team', 'Bergheim');
    fixture.componentRef.setInput('board', 1);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [BoardGameComponent], providers: [
      { provide: LineupsApiService, useValue: jasmine.createSpyObj('LineupsApiService', ['clubGame', 'fixturePgn', 'sharedPgn']) },
      { provide: HandoffService, useValue: jasmine.createSpyObj('HandoffService', ['jumpToRookHub']) },
      provideRouter([]), provideTranslateService({ fallbackLang: 'de' })] });
  });

  // 0.727.2, Nutzer 2026-10-09: „die 4 links sind verschoben" — Nachspielen/Analyse waren .btn-link-Knöpfe,
  // Bearbeiten/Korrigieren nackte <a> in Browser-Linkfarbe und ohne Polster.
  it('alle vier Aktionen sind dieselbe Art (.btn-link) auf einer Grundlinie', () => {
    const el = render(GAME, 'https://rookhub.example');
    const acts = Array.from(el.querySelectorAll('.bg-actions > *')) as HTMLElement[];
    expect(acts.map(a => a.textContent?.trim())).toEqual(['Nachspielen', 'Analyse', 'Bearbeiten', 'Korrigieren']);
    expect(acts.every(a => a.classList.contains('btn-link'))).toBeTrue();
    expect(getComputedStyle(el.querySelector('.bg-actions')!).alignItems).toBe('baseline');
    expect(el.querySelector('.bg-edit')?.getAttribute('href')).toBe('/verein?bearbeiten=168');
    expect(el.querySelector('.bg-fix')?.getAttribute('href')).toBe('/verein/partie/168/korrigieren');
  });

  it('ohne Bearbeitungsrecht und ohne RookHub nur „Nachspielen"', () => {
    const el = render({ ...GAME, canEdit: false }, null);
    const acts = Array.from(el.querySelectorAll('.bg-actions > *'));
    expect(acts.map(a => a.textContent?.trim())).toEqual(['Nachspielen']);
    expect(el.querySelector('.bg-what')?.textContent).toContain('Partie vorhanden · 61 Halbzüge · 1.d4 Sf6 2.c4 e6 …');
  });

  // 0.838.0: über einen Teilen-Link gibt es ohne Anmeldung weder Analyse noch Bearbeiten; das PGN kennt der Link nur für
  // die geteilte Begegnung (`/s/{token}/games`), an anderen Brettern der Runde bleibt es bei „Partie vorhanden".
  it('über einen Teilen-Link: Nachspielen nur an der geteilten Begegnung, PGN über den Link, sonst keine Aktion', async () => {
    const api = TestBed.inject(LineupsApiService) as jasmine.SpyObj<LineupsApiService>;
    api.sharedPgn.and.resolveTo('1. d4 Nf6 2. c4 e6 1/2-1/2');
    const el = render(GAME, 'https://rookhub.example');
    fixture.componentRef.setInput('shareToken', 'TOK');
    fixture.componentRef.setInput('shared', true);
    fixture.detectChanges();
    // 0.838.3: „Analyse" auch über den Link (RookHubs Partieseite über den Link), Bearbeiten/Korrigieren nicht
    expect(Array.from(el.querySelectorAll('.bg-actions > *')).map(a => a.textContent?.trim())).toEqual(['Nachspielen', 'Analyse']);
    (el.querySelector('.bg-analysis') as HTMLButtonElement).click();
    expect((TestBed.inject(HandoffService) as jasmine.SpyObj<HandoffService>).jumpToRookHub).toHaveBeenCalledWith('s/TOK/club-games/168');
    (el.querySelector('.bg-replay') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.sharedPgn).toHaveBeenCalledWith('TOK', 1);
    expect(api.clubGame).not.toHaveBeenCalled();
    expect(el.querySelector('lh-game-replay')).not.toBeNull();
    expect(fixture.componentInstance.evalsUrl()).toBeNull();

    fixture.componentRef.setInput('shared', false);
    fixture.detectChanges();
    expect(el.querySelectorAll('.bg-actions > *').length).toBe(0);
    expect(el.querySelector('.bg-what')?.textContent).toContain('Partie vorhanden');
  });
});
