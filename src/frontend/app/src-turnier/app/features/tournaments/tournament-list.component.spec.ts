import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
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
});
