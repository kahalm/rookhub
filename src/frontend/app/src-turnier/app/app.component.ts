import { Component, ChangeDetectionStrategy, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TurnierNavbarComponent } from './shell/turnier-navbar.component';
import { AppFooterComponent } from '@rh/shared/app-footer/app-footer.component';
import { ImpersonationBannerComponent } from '@rh/shared/impersonation-banner/impersonation-banner.component';
import { LocaleService } from '@rh/core/locale.service';
import { HandoffService } from '@rh/core/handoff.service';
import { ThemeService } from '@rh/core/theme.service';

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
    <app-footer />
  `,
  styles: [`
    :host { display: flex; flex-direction: column; min-height: 100vh; }
    main { display: block; flex: 1; }
  `],
})
export class TurnierAppComponent implements OnInit {
  private locale = inject(LocaleService);
  private handoff = inject(HandoffService);
  // Nur injizieren genuegt: der Dienst liest den geteilten Modus und setzt die Klasse am
  // <html>-Element selbst. Ohne ihn stand die Turnierseite immer im hellen Grundzustand.
  private theme = inject(ThemeService);

  ngOnInit(): void {
    this.locale.init();
    // Kommt der Aufrufer per Sprung von RookHub, bringt er einen Einmal-Code mit — den gegen eine
    // eigene Anmeldung tauschen, BEVOR die erste Seite ihre Daten holt.
    void this.handoff.consumeIncoming();
  }
}
