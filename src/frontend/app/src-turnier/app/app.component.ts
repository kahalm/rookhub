import { Component, ChangeDetectionStrategy, DestroyRef, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TurnierNavbarComponent } from './shell/turnier-navbar.component';
import { AppFooterComponent } from '@rh/shared/app-footer/app-footer.component';
import { ImpersonationBannerComponent } from '@rh/shared/impersonation-banner/impersonation-banner.component';
import { LocaleService } from '@rh/core/locale.service';
import { HandoffService } from '@rh/core/handoff.service';
import { ThemeService } from '@rh/core/theme.service';
import { AppUpdateService } from '@rh/core/app-update.service';

@Component({
  selector: 'trn-root',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [RouterOutlet, TurnierNavbarComponent, AppFooterComponent, ImpersonationBannerComponent],
  template: `
    <!-- Derselbe rote Streifen wie in RookHub: ein Einstieg in ein fremdes Konto ohne sichtbaren
         Hinweis ist die gefaehrliche Variante. -->
    <app-impersonation-banner />
    <trn-navbar />
    <main><router-outlet /></main>
    <!-- Anders als in RookHub bleibt die Fusszeile auch am Handy stehen: hier ist sie der
         einzige Weg zu Version, Hilfe und Rueckmeldung — RookHubs Menue traegt diese Wege selbst. -->
    <app-footer [hideOnMobile]="false" />
  `,
  styles: [`
    /* 100vh ist am Handy HOEHER als der sichtbare Bereich (die Adressleiste zaehlt mit) —
       selbst die kurze Anmeldeseite liess sich um deren Hoehe scrollen und „wackelte" beim
       Antippen. svh ist die kleine, stabile Hoehe (dvh aenderte sie beim Ein-/Ausklappen der
       Leiste waehrend des Scrollens und loeste Relayouts aus); am Schreibtisch gleich 100vh.
       Die vh-Zeile davor ist der Rueckfall fuer Browser ohne svh. */
    :host { display: flex; flex-direction: column; min-height: 100vh; min-height: 100svh; }
    main { display: block; flex: 1; }
  `],
})
export class TurnierAppComponent implements OnInit {
  private locale = inject(LocaleService);
  private handoff = inject(HandoffService);
  // Nur injizieren genuegt: der Dienst liest den geteilten Modus und setzt die Klasse am
  // <html>-Element selbst. Ohne ihn stand die Turnierseite immer im hellen Grundzustand.
  private theme = inject(ThemeService);
  /**
   * Der Hinweis auf eine neue Fassung. Die Turnierseite registriert seit ihrem ersten Tag einen
   * Service Worker, hatte aber nie einen Hinweis darauf, dass eine neue Fassung bereitliegt —
   * ein offener Tab lief nach einem Deploy also unbegrenzt auf der ALTEN weiter, ohne jedes
   * Anzeichen. Derselbe Dienst wie in RookHub, keine zweite Fassung.
   */
  private appUpdate = inject(AppUpdateService);
  private destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    this.locale.init();
    this.appUpdate.start(this.destroyRef);
    // Kommt der Aufrufer per Sprung von RookHub, bringt er einen Einmal-Code mit — den gegen eine
    // eigene Anmeldung tauschen, BEVOR die erste Seite ihre Daten holt.
    void this.handoff.consumeIncoming();
  }
}
