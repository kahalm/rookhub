import { ApplicationRef } from '@angular/core';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { renderAfterHttpInterceptor } from './render-after-http.interceptor';

/**
 * Die Fehlerklasse, die dieser Interceptor beendet: eine Antwort trifft ausserhalb der Zone ein,
 * die Feldzuweisung im Abonnenten loest keinen Aenderungslauf aus, und das Ergebnis erscheint erst
 * beim naechsten Klick. Gemeldet an der Spielersuche im Profil und am Turnierkalender.
 */
describe('renderAfterHttpInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let tick: jasmine.Spy;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([renderAfterHttpInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    tick = spyOn(TestBed.inject(ApplicationRef), 'tick');
  });

  afterEach(() => controller.verify());

  it('zeichnet nach einer Antwort neu', async () => {
    http.get('/api/x').subscribe();
    controller.expectOne('/api/x').flush({ ok: true });

    await Promise.resolve();
    expect(tick).toHaveBeenCalled();
  });

  /** Auch eine Fehlerbehandlung setzt Felder — der Lauf gehoert also ebenso dazu. */
  it('zeichnet auch nach einem Fehlschlag neu', async () => {
    http.get('/api/x').subscribe({ error: () => {} });
    controller.expectOne('/api/x').flush('kaputt', { status: 500, statusText: 'Server Error' });

    await Promise.resolve();
    expect(tick).toHaveBeenCalled();
  });

  /** Der Lauf kommt NACH dem Abonnenten — sonst saehe er dessen Zuweisung nicht. */
  it('läuft erst, nachdem der Abonnent seine Felder gesetzt hat', async () => {
    let valueWhenTicked: unknown = 'noch nichts';
    let received: unknown = null;
    tick.and.callFake(() => (valueWhenTicked = received));

    http.get('/api/x').subscribe(v => (received = v));
    controller.expectOne('/api/x').flush({ ok: true });

    await Promise.resolve();
    expect(valueWhenTicked).toEqual({ ok: true });
  });

  /** Mehrere Antworten im selben Tick brauchen nur EINEN Lauf. */
  it('bündelt mehrere Antworten zu einem Lauf', async () => {
    http.get('/api/a').subscribe();
    http.get('/api/b').subscribe();
    controller.expectOne('/api/a').flush({});
    controller.expectOne('/api/b').flush({});

    await Promise.resolve();
    expect(tick).toHaveBeenCalledTimes(1);
  });

  /** Ein Wurf aus dem Lauf darf den Aufrufer nicht treffen — der hat seine Antwort laengst. */
  it('schluckt einen Fehler des Änderungslaufs', async () => {
    tick.and.throwError('läuft schon');

    let value: unknown = null;
    http.get('/api/x').subscribe(v => (value = v));
    controller.expectOne('/api/x').flush({ ok: true });

    await Promise.resolve();
    expect(value).toEqual({ ok: true });
  });
});
