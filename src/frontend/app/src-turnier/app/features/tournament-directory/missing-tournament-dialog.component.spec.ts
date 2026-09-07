import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MissingTournamentDialogComponent } from './missing-tournament-dialog.component';

/**
 * „Mein Turnier fehlt". Der LINK ist das Pflichtfeld und nicht der Turniername: eine
 * Verbands- oder Vereinsseite laesst sich zusaetzlich auswerten, eine Aufzaehlung einzelner
 * Termine im Freitext nicht.
 */
describe('MissingTournamentDialogComponent', () => {
  let http: HttpTestingController;
  let closed: jasmine.Spy;

  function setup() {
    closed = jasmine.createSpy('close');
    TestBed.configureTestingModule({
      imports: [MissingTournamentDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: closed } },
      ],
    });
    const fixture = TestBed.createComponent(MissingTournamentDialogComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('sendet Link und Nachricht', () => {
    const component = setup();
    component.link = ' https://www.tiroler-schachverband.at/termine ';
    component.message = 'Unsere Turniere stehen nicht auf chess-results.';

    component.send();

    const req = http.expectOne('/api/tournament-directory/suggest-source');
    expect(req.request.body).toEqual({
      link: 'https://www.tiroler-schachverband.at/termine',
      message: 'Unsere Turniere stehen nicht auf chess-results.',
    });
    req.flush(null);

    expect(closed).toHaveBeenCalledWith(true);
    http.verify();
  });

  /**
   * Vor dem Absenden pruefen, nicht nur serverseitig: ein 400 als Snackbar laesst den Nutzer
   * raten, WELCHES Feld gemeint war — und der Dialog waere dann schon zu.
   */
  it('weist einen unbrauchbaren Link ab, ohne zu senden', () => {
    for (const link of ['', 'tiroler-schachverband.at', 'javascript:alert(1)']) {
      const component = setup();
      component.link = link;

      component.send();

      expect(component.error()).toBe('tournamentDirectory.missing.linkRequired');
      expect(closed).not.toHaveBeenCalled();
      http.verify();
      TestBed.resetTestingModule();
    }
  });

  it('lässt den Dialog nach einem Fehlschlag offen', () => {
    const component = setup();
    component.link = 'https://example.org/termine';

    component.send();
    http.expectOne('/api/tournament-directory/suggest-source')
      .flush({ message: 'nope' }, { status: 500, statusText: 'Server Error' });

    expect(component.error()).toBe('tournamentDirectory.missing.sendError');
    expect(component.sending()).toBeFalse();
    expect(closed).not.toHaveBeenCalled();
    http.verify();
  });
});
