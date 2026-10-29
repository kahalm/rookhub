import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
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

  // d8-t-dialog-scroll-hint-fehlt: scrollt am Handy bzw. Boox 200 %
  it('Inhalt mit Weiterscroll-Schatten', () => {
    setup();
    const fixture = TestBed.createComponent(MissingTournamentDialogComponent);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-dialog-content')?.classList).toContain('rh-scroll-hint');
  });

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

  // d7-t-melden-hint-ueberlappt-textarea + d7-t-melden-notizfeld-abgeschnitten-handy: am Handy bricht der Linkhinweis
  // in zwei Zeilen um und lag auf dem Notizfeld; das lange Label wurde mit „…“ gekürzt.
  it('Handybreite: Linkhinweis endet über dem Notizfeld, das Notizfeld hat ein kurzes Label und die Frage als Platzhalter', async () => {
    TestBed.configureTestingModule({
      imports: [MissingTournamentDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'de' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
      ],
    });
    TestBed.inject(TranslateService).setTranslation('de', { tournamentDirectory: { missing: {
      link: 'Link auf die offizielle Seite',
      linkHint: 'Verbandskalender, Vereinsseite, Seite der Turnierreihe — je offizieller, desto besser',
      message: 'Anmerkung (freiwillig)',
      messagePlaceholder: 'Sonst noch etwas, das wir wissen sollten?',
    } } });
    TestBed.inject(TranslateService).use('de');
    const fixture = TestBed.createComponent(MissingTournamentDialogComponent);
    const el = fixture.nativeElement as HTMLElement;
    el.style.display = 'block';
    el.style.width = '300px';
    document.body.appendChild(el);
    try {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      const hint = el.querySelector('.link-field mat-hint')!;
      const message = el.querySelector('.message-field')!;
      expect(hint.getBoundingClientRect().height).toBeGreaterThan(20);   // wirklich zweizeilig
      expect(hint.getBoundingClientRect().bottom).toBeLessThanOrEqual(message.getBoundingClientRect().top - 8);
      expect(message.querySelector('mat-label')!.textContent!.trim()).toBe('Anmerkung (freiwillig)');
      expect(message.querySelector('textarea')!.getAttribute('placeholder')).toBe('Sonst noch etwas, das wir wissen sollten?');
    } finally {
      el.remove();
    }
  });

  // d7-t-hier-eintragen-beschriftung: der Knopf trägt kein Turnier ein, er schlägt eine Quelle vor (suggest-source).
  // d7-t-melden-notizfeld-abgeschnitten-handy: kurzes Label, die Frage als Platzhalter.
  it('Texte: „Quelle vorschlagen“ statt „Hier eintragen“, kurzes Notiz-Label', async () => {
    const load = async (lang: string) =>
      (await (await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`))).json())
        .tournamentDirectory.missing;
    const de = await load('de');
    const en = await load('en');
    expect(de.cta).toBe('Dein Turnier fehlt? Quelle vorschlagen');
    expect(en.cta).toBe('Your event missing? Suggest a source');
    expect(en.title).toBe('Suggest a missing tournament source');
    for (const m of [de, en]) {
      expect(m.cta).not.toMatch(/eintragen|add it/i);
      expect(m.message.length).toBeLessThanOrEqual(25);   // passt am Handy ungekürzt ins Label
      expect(m.messagePlaceholder).toContain('?');
    }
  });
});
