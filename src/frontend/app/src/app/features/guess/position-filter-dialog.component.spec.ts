import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { PositionFilterDialogComponent } from './position-filter-dialog.component';

/**
 * Der Stellungsfilter. Geprüft wird, was ihn von der Namenssuche unterscheidet: das Brett führt
 * der Dialog selbst mit, der Baum liefert nur Zugnamen — und ein Zug, der in der Stellung nicht
 * geht, darf die Linie nicht verschieben.
 */
describe('PositionFilterDialogComponent', () => {
  let http: HttpTestingController;
  let closed: number | undefined;

  beforeEach(() => {
    closed = undefined;
    TestBed.configureTestingModule({
      imports: [PositionFilterDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: (v?: number) => { closed = v; } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  function open() {
    const fixture = TestBed.createComponent(PositionFilterDialogComponent);
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/guess-tree')
      .flush({ line: '', onlyPlayable: true, total: 3, moves: [{ san: 'e4', games: 2 }, { san: 'd4', games: 1 }] });
    http.expectOne(r => r.url === '/api/game-analyses/public').flush([]);
    return fixture.componentInstance;
  }

  it('fragt den Baum und die spielbaren Partien zur Grundstellung', () => {
    const c = open();

    expect(c.total).toBe(3);
    expect(c.moves.length).toBe(2);
    expect(c.lineText).toBe('');
  });

  it('spielt einen Zug und fragt die neue Stellung ab', () => {
    const c = open();

    c.play({ san: 'e4', games: 2 });

    const baum = http.expectOne(r => r.url === '/api/guess-tree');
    expect(baum.request.params.get('line')).toBe('e4');
    baum.flush({ line: 'e4', onlyPlayable: true, total: 2, moves: [{ san: 'e5', games: 2 }] });
    http.expectOne(r => r.url === '/api/game-analyses/public').flush([]);

    expect(c.lineText).toBe('e4');
    expect(c.fen).toContain(' b ');   // Schwarz ist am Zug
  });

  /** Der Baum kennt nur Zeichenketten. Eine Partie mit abweichender Ausgangsstellung könnte eine
   *  Fortsetzung liefern, die auf diesem Brett nicht geht — dann darf sich nichts bewegen. */
  it('verschiebt die Linie nicht bei einem unmöglichen Zug', () => {
    const c = open();

    c.play({ san: 'Qxh8', games: 1 });

    expect(c.lineText).toBe('');
    http.expectNone(r => r.url === '/api/guess-tree');
  });

  it('geht zurück und fragt die vorige Stellung ab', () => {
    const c = open();
    c.play({ san: 'e4', games: 2 });
    http.expectOne(r => r.url === '/api/guess-tree').flush({ line: 'e4', onlyPlayable: true, total: 2, moves: [] });
    http.expectOne(r => r.url === '/api/game-analyses/public').flush([]);

    c.back();

    http.expectOne(r => r.url === '/api/guess-tree').flush({ line: '', onlyPlayable: true, total: 3, moves: [] });
    http.expectOne(r => r.url === '/api/game-analyses/public').flush([]);
    expect(c.lineText).toBe('');
  });

  /** „alle" zählt den Rohbestand mit — und fragt dann auch dessen Liste, nicht die spielbare. */
  it('holt bei alle den Rohbestand', () => {
    const c = open();

    c.onlyPlayable = false;
    c.load();

    const baum = http.expectOne(r => r.url === '/api/guess-tree');
    expect(baum.request.params.get('onlyPlayable')).toBe('false');
    baum.flush({ line: '', onlyPlayable: false, total: 130000, moves: [] });
    const liste = http.expectOne(r => r.url === '/api/library-games');
    expect(liste.request.params.get('line')).toBe('');
    liste.flush({ items: [], total: 0, page: 1, pageSize: 50 });
  });

  it('schliesst mit der Analyse-Id', () => {
    const c = open();

    c.choose(7);

    expect(closed).toBe(7);
  });
});
