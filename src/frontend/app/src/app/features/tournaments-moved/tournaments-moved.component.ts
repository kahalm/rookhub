import { Component, OnInit, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { HandoffService } from '../../core/handoff.service';

/**
 * Auffangstelle fuer die Turnier-Adressen, die es in RookHub nicht mehr gibt.
 *
 * <p>Mit v0.409.0 sind `/tournaments`, `/tournaments/:id` und `/t/:id` auf die eigene Turnierseite
 * gezogen. Die Routen hier ERSATZLOS zu streichen war ein Fehler: alte Lesezeichen, geteilte
 * `/t/{id}`-Links und die Liste „Abonnierte Turniere" auf dem Dashboard zeigen weiter dorthin.
 * Ohne diese Auffangstelle fallen sie nicht ins Catch-all, sondern in die Kurz-URL-Route
 * `:slug/:chapter` — die fragt nach einem Kurs mit diesem Namen, bekommt 404 und schickt still
 * aufs Dashboard. Der Nutzer klickt, und es passiert scheinbar nichts.</p>
 *
 * <p>Weitergereicht wird MIT der Anmeldung (Einmal-Code), damit man drueben nicht vor der
 * Anmeldemaske landet. Gibt es keine Schwesterseite (localhost, IP), bleibt der Hinweis stehen —
 * dann gibt es schlicht kein Ziel.</p>
 */
@Component({
  selector: 'app-tournaments-moved',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <div class="moved">
      <p>{{ (partner ? 'tournamentsMoved.leaving' : 'tournamentsMoved.noPartner') | translate }}</p>
      @if (partner) {
        <a [href]="target">{{ 'tournamentsMoved.link' | translate }}</a>
      }
    </div>
  `,
  styles: [`
    .moved { max-width: 40rem; margin: 3rem auto; padding: 0 1rem; text-align: center; }
    a { color: var(--mat-sys-primary); }
  `],
})
export class TournamentsMovedComponent implements OnInit {
  private readonly handoff = inject(HandoffService);
  private readonly router = inject(Router);

  readonly partner = this.handoff.partnerUrl;
  /** Fuer den Fall, dass der Sprung nicht ankommt: derselbe Weg zum Klicken, ohne Einmal-Code. */
  readonly target = `${this.partner ?? ''}${this.router.url}`;

  ngOnInit(): void {
    if (!this.partner) return;
    void this.handoff.jump(this.router.url.replace(/^\//, ''));
  }
}
