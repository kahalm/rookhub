import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AddBoardDialogData, LiveAddBoardDialogComponent } from './live-add-board-dialog.component';
import { ExternalRoundView } from './live-boards.models';

const WM: ExternalRoundView = {
  roundId: 'fnmEB1kH', roundName: 'Game 3', tourName: 'FIDE World Championship 2026', url: 'https://lichess.org/broadcast/-/-/fnmEB1kH',
  ongoing: true, finished: false, gameId: 'RD8sbYw1',
  games: [{ id: 'RD8sbYw1', white: 'Gukesh D', black: 'Carlsen, Magnus', whiteTitle: 'GM', blackTitle: 'GM', whiteElo: 2780, blackElo: 2830, result: '*' }],
};

/** „Brett hinzufügen“ (1.025.0): Suche in der Runde, Lichess-Link prüfen und laden, Partie-Link wählt seine Partie gleich aus. */
describe('LiveAddBoardDialogComponent', () => {
  let fixture: ComponentFixture<LiveAddBoardDialogComponent>;
  let http: HttpTestingController;
  let close: jasmine.Spy;

  async function setup(data: AddBoardDialogData): Promise<void> {
    close = jasmine.createSpy('close');
    await TestBed.configureTestingModule({
      imports: [LiveAddBoardDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LiveAddBoardDialogComponent);
    fixture.autoDetectChanges();
    await fixture.whenStable();
  }

  const rows = [
    { key: '2.1', board: 1, white: 'Muster, Max', black: 'Gast, Erster', match: 'SK Absam – SK Wörgl' },
    { key: '2.2', board: 2, white: 'Zweiter, Gast', black: 'Muster, Moritz', match: 'SK Absam – SK Wörgl' },
    { key: '3.1', board: 1, white: 'Huber, Anna', black: 'Berger, Eva', match: 'Innsbruck – Kufstein' },
  ];

  it('Suche nach Spieler (Reihenfolge egal) oder Begegnung, Antippen schaltet um, Fertig gibt die Auswahl zurück', async () => {
    await setup({ rows, added: ['2.2'], ext: [] });
    const c = fixture.componentInstance;
    c.query.set('anna huber');
    expect(c.shownRows().map(r => r.key)).toEqual(['3.1']);
    c.query.set('kufstein');
    expect(c.shownRows().map(r => r.key)).toEqual(['3.1']);
    c.toggleAdd('3.1');
    c.toggleAdd('2.2');
    c.done();
    expect(close).toHaveBeenCalledWith({ added: ['3.1'], ext: [] });
  });

  it('ohne Bretter der Runde (kein Filter) nur das Link-Feld', async () => {
    await setup({ rows: [], added: [], ext: [] });
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.ab-head').length).toBe(1);
  });

  it('ungültiger Link: Fehler im Browser, keine Anfrage', async () => {
    await setup({ rows: [], added: [], ext: [] });
    const c = fixture.componentInstance;
    c.link.set('https://example.com/broadcast/x/game-3/fnmEB1kH');
    c.lookup();
    expect(c.linkError()).toBe('apiErrors.live_invalid_url');
    http.expectNone(() => true);
  });

  it('Partie-Link: lädt die Runde und wählt genau diese Partie aus', async () => {
    await setup({ rows: [], added: [], ext: [] });
    const c = fixture.componentInstance;
    c.link.set('https://lichess.org/broadcast/fide-world-chess-championship-2026/game-3/fnmEB1kH/RD8sbYw1');
    c.lookup();
    http.expectOne(r => r.url === '/api/live-boards/external').flush(WM);
    await fixture.whenStable();
    expect(c.ext()).toEqual(['fnmEB1kH.RD8sbYw1']);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('FIDE World Championship 2026 · Game 3');
    c.done();
    expect(close).toHaveBeenCalledWith({ added: [], ext: ['fnmEB1kH.RD8sbYw1'] });
  });

  it('Serverfehler mit Code: übersetzter Text', async () => {
    await setup({ rows: [], added: [], ext: [] });
    const c = fixture.componentInstance;
    c.link.set('https://lichess.org/broadcast/x/game-3/fnmEB1kH/Missing1');
    c.lookup();
    http.expectOne(r => r.url === '/api/live-boards/external')
      .flush({ message: 'x', code: 'live_ext_game_not_found' }, { status: 404, statusText: 'Not Found' });
    expect(c.linkError()).toBe('x');                       // ohne geladene Sprachdatei: message (sonst apiErrors.…)
    expect(c.round()).toBeNull();
  });

  it('Grenze: höchstens 4 fremde Runden', async () => {
    await setup({ rows: [], added: [], ext: ['Round001.Game0001', 'Round002.Game0001', 'Round003.Game0001', 'Round004.Game0001'] });
    const c = fixture.componentInstance;
    expect(c.canAddExt('Round005')).toBeFalse();
    expect(c.canAddExt('Round001')).toBeTrue();
  });
});
