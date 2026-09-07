import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AppFooterComponent } from './app-footer.component';
import { DISCORD_INVITE_URL } from '../../core/community';
import { environment } from '../../../environments/environment';

/**
 * Die Fusszeile gehoert BEIDEN Oberflaechen (RookHub und Turnierseite). Geprueft wird vor allem
 * das Changelog-Overlay: seine ~0,9 MB Prosa duerfen nicht im Initial-Bundle liegen, kommen also
 * per dynamic import() erst beim Oeffnen.
 */
describe('AppFooterComponent', () => {
  function build(routes: { path: string }[] = []) {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes.map(r => ({ path: r.path, children: [] }))),
                  provideTranslateService({ fallbackLang: 'en' })],
    });
    return TestBed.createComponent(AppFooterComponent).componentInstance;
  }

  it('nennt den Discord-Einladungslink und die laufende Version', () => {
    const footer = build();
    expect(footer.discordUrl).toBe(DISCORD_INVITE_URL);
    expect(footer.version).toBe(environment.version);
  });

  it('haelt die Changelog-Eintraege aus dem eager geladenen Environment heraus', () => {
    expect((environment as Record<string, unknown>)['changelog']).toBeUndefined();
    expect(build().changelog.length).toBe(0);
  });

  it('laedt die Changelog-Eintraege beim Oeffnen des Overlays nach (dynamic import)', async () => {
    const footer = build();
    footer.openChangelog();
    expect(footer.showChangelog).toBeTrue();
    await footer.changelogLoad;
    expect(footer.changelog.length).toBeGreaterThan(0);
    expect(footer.changelog[0].version).toBeTruthy();
    expect(footer.changelog[0].changes[0].en).toBeTruthy();
    expect(footer.changelog[0].changes[0].de).toBeTruthy();
  });

  it('klappt das Overlay per Versionslink auf und zu (Laden nur beim Oeffnen)', async () => {
    const footer = build();
    footer.toggleChangelog();
    expect(footer.showChangelog).toBeTrue();
    await footer.changelogLoad;
    const loaded = footer.changelog;
    expect(loaded.length).toBeGreaterThan(0);

    footer.toggleChangelog();
    expect(footer.showChangelog).toBeFalse();

    footer.toggleChangelog();                 // erneutes Oeffnen laedt nicht neu
    await footer.changelogLoad;
    expect(footer.changelog).toBe(loaded);
  });

  it('schliesst das Overlay mit Escape', async () => {
    const footer = build();
    footer.openChangelog();
    await footer.changelogLoad;

    footer.onEscape();

    expect(footer.showChangelog).toBeFalse();
  });

  // ----- Der eine Teil, der sich zwischen den Oberflaechen unterscheidet ----

  it('verlinkt die eigene Hilfeseite, wenn es sie in dieser App gibt', () => {
    const footer = build([{ path: 'help' }]);
    expect(footer.helpRoute).toBeTrue();
    expect(footer.helpHref).toBeNull();
  });

  it('laesst den Hilfe-Link weg, wenn es weder eine eigene Seite noch eine Schwesterseite gibt', () => {
    // Karma laeuft auf localhost — dort gibt es keine Schwesterseite, ein Link waere ins Leere.
    const footer = build();
    expect(footer.helpRoute).toBeFalse();
    expect(footer.helpHref).toBeFalsy();
  });
});
