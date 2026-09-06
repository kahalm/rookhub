import { Component, ChangeDetectionStrategy, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TurnierNavbarComponent } from './shell/turnier-navbar.component';
import { LocaleService } from '@rh/core/locale.service';
import { HandoffService } from '@rh/core/handoff.service';
import { ThemeService } from '@rh/core/theme.service';

@Component({
  selector: 'trn-root',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [RouterOutlet, TurnierNavbarComponent],
  template: `
    <trn-navbar />
    <main><router-outlet /></main>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; }
    main { display: block; }
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
