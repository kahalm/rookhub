import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Subject } from 'rxjs';
import { SnackbarService } from '@rh/core/snackbar.service';
import { Subscription } from '@rh/core/models';
import { TournamentListComponent } from './tournament-list.component';

function sub(over: Partial<Subscription> = {}): Subscription {
  return {
    id: 1, crawlerTournamentId: '1107064', tournamentName: 'Schach Tirol Open',
    subscribedAt: '2026-09-01T10:00:00Z', tournamentDbId: null, eventDate: '2026-12-01', ...over,
  };
}

/**
 * „Meine Turniere" zeigt die GEMERKTEN Turniere — nicht mehr alles, was jemals jemand geholt hat,
 * und ohne das Import-Formular. Die Liste kommt aus den Abos, ein Abruf.
 */
describe('TournamentListComponent', () => {
  let fixture: ComponentFixture<TournamentListComponent>;
  let component: TournamentListComponent;
  let http: HttpTestingController;

  async function setup(subs: Subscription[]) {
    await TestBed.configureTestingModule({
      imports: [TournamentListComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TournamentListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    http.expectOne('/api/subscriptions').flush(subs);
  }

  afterEach(() => TestBed.resetTestingModule());

  it('holt nur die Abos — nicht mehr die Liste aller geholten Turniere', async () => {
    await setup([sub()]);

    expect(component.upcoming().length).toBe(1);
    http.verify();
  });

  /**
   * Die Frage an eine Merkliste ist „was steht als Naechstes an" — nicht „was habe ich zuletzt
   * gemerkt". Kommende nach Termin aufsteigend, vergangene getrennt und neueste zuerst.
   */
  it('sortiert kommende nach Termin und trennt die vergangenen ab', async () => {
    await setup([
      sub({ id: 1, eventDate: '2026-12-01', tournamentName: 'Spaeter' }),
      sub({ id: 2, eventDate: '2026-10-01', tournamentName: 'Frueher' }),
      sub({ id: 3, eventDate: '2020-05-01', tournamentName: 'Vorbei' }),
    ]);

    expect(component.upcoming().map(s => s.tournamentName)).toEqual(['Frueher', 'Spaeter']);
    expect(component.past().map(s => s.tournamentName)).toEqual(['Vorbei']);
  });

  /** Ohne Termin (Altbestand) gilt als kommend — wir wissen es nicht besser und verstecken nichts. */
  it('behandelt ein Abo ohne Termin als kommend', async () => {
    await setup([sub({ eventDate: null })]);

    expect(component.upcoming().length).toBe(1);
    expect(component.past().length).toBe(0);
  });

  /**
   * Die Zeile verschwindet SOFORT. Auf die Antwort zu warten, bevor sich etwas ruehrt, laesst den
   * Klick verloren wirken.
   */
  it('nimmt das Merken sofort zurück', async () => {
    await setup([sub({ id: 7 })]);

    component.unbookmark(sub({ id: 7 }));
    expect(component.upcoming().length).toBe(0);

    http.expectOne(r => r.url === '/api/subscriptions/7' && r.method === 'DELETE').flush(null);
    expect(component.removing()).toBeNull();
  });

  /** Scheitert der Aufruf, kommt die Zeile zurueck — sonst ist sie weg und das Abo besteht weiter. */
  it('stellt die Zeile wieder her, wenn das Entfernen scheitert', async () => {
    await setup([sub({ id: 7 })]);

    component.unbookmark(sub({ id: 7 }));
    http.expectOne(r => r.url === '/api/subscriptions/7')
      .flush('kaputt', { status: 500, statusText: 'Server Error' });

    expect(component.upcoming().length).toBe(1);
  });

  /**
   * Der Loesen-Knopf ist ein reines Icon am rechten Rand — ein Fehltipp auf dem Handy darf nicht
   * endgueltig sein. „Rueckgaengig" im Snackbar merkt neu und legt das ALTE Objekt mit der NEUEN
   * Id zurueck (Termin bleibt, Zeile bleibt in ihrem Abschnitt) — nicht die Server-Antwort.
   */
  it('legt die Zeile per Rückgängig mit neuer Id wieder an', async () => {
    await setup([sub({ id: 7 })]);
    const action = new Subject<void>();
    const snackbar = TestBed.inject(SnackbarService);
    const show = spyOn(snackbar, 'show').and.returnValue({ onAction: () => action.asObservable() } as never);

    component.unbookmark(sub({ id: 7 }));
    http.expectOne(r => r.url === '/api/subscriptions/7' && r.method === 'DELETE').flush(null);
    expect(component.upcoming().length).toBe(0);
    expect(show).toHaveBeenCalledWith('tournaments.list.unsubscribed', { action: 'common.undo', duration: 6000 });

    action.next();
    const post = http.expectOne(r => r.url === '/api/subscriptions' && r.method === 'POST');
    expect(post.request.body).toEqual({ crawlerTournamentId: '1107064', tournamentName: 'Schach Tirol Open' });
    // Server-Antwort absichtlich ohne Termin: die Zeile behaelt trotzdem ihren.
    post.flush(sub({ id: 8, eventDate: null }));

    expect(component.upcoming().map(s => s.id)).toEqual([8]);
    expect(component.upcoming()[0].eventDate).toBe('2026-12-01');
    http.verify();
  });

  /** Scheitert das Neu-Merken, bleibt die Zeile weg und die Meldung sagt es — kein stilles Nichts. */
  it('meldet, wenn Rückgängig scheitert', async () => {
    await setup([sub({ id: 7 })]);
    const action = new Subject<void>();
    const snackbar = TestBed.inject(SnackbarService);
    spyOn(snackbar, 'show').and.returnValue({ onAction: () => action.asObservable() } as never);
    const warn = spyOn(snackbar, 'warn').and.returnValue(undefined as never);

    component.unbookmark(sub({ id: 7 }));
    http.expectOne(r => r.url === '/api/subscriptions/7').flush(null);

    action.next();
    http.expectOne(r => r.url === '/api/subscriptions' && r.method === 'POST')
      .flush('kaputt', { status: 500, statusText: 'Server Error' });

    expect(component.upcoming().length).toBe(0);
    expect(warn).toHaveBeenCalledWith('tournaments.list.undoFailed');
  });
});
