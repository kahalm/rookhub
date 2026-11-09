import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { StaleRouteReloadService } from '../../core/stale-route-reload.service';

/**
 * „Seite nicht gefunden“ (Codereview UX-026) — statt einer stummen Umleitung aufs Dashboard (RookHub) bzw. die
 * Prognosen (LeagueHub). OHNE Guard: ein Gast mit falschem oder veraltetem Link sah vorher die Anmeldemaske
 * „… um fortzufahren“, glaubte, dahinter warte der Inhalt, und stand nach der Anmeldung doch nur auf der Startseite.
 * Die Adresse bleibt in der Leiste stehen (keine Umleitung) — man sieht, welcher Link kaputt ist.
 *
 * Benutzt von RookHubs '**', vom Kurz-URL-Auflöser (`PublicSlugComponent`, unbekannter Alias oder Kapitel) und von
 * LeagueHubs '**'. „Zur Startseite“ ist „/“ — jede App weiß selbst, wohin das führt; die Hilfe nur, wo die App eine
 * eigene Hilfeseite hat (dieselbe Frage an die Routentabelle wie in der Fußzeile).
 *
 * Eine alte Fassung kennt neue Adressen nicht: Vor dem Text wird deshalb nach einer neueren Fassung gesucht und einmal
 * neu geladen (`StaleRouteReloadService`). Der Ladekreis verhindert, dass der Text dabei kurz aufblitzt; `checking` ist
 * ein Signal, weil das Ergebnis asynchron eintrifft und die OnPush-View sonst nicht neu gezeichnet würde.
 */
@Component({
  selector: 'app-not-found',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    @if (checking()) {
      <div class="nf-checking" role="status" [attr.aria-label]="'common.loading' | translate">
        <mat-spinner diameter="40"></mat-spinner>
      </div>
    } @else {
      <section class="not-found">
        <h1>{{ 'app.notFound.title' | translate }}</h1>
        <p>{{ 'app.notFound.text' | translate }}</p>
        <div class="nf-actions">
          <a mat-flat-button routerLink="/">{{ 'app.notFound.home' | translate }}</a>
          @if (helpRoute) {
            <a mat-button routerLink="/help">{{ 'nav.help' | translate }}</a>
          }
        </div>
      </section>
    }
  `,
  styles: [`
    .not-found { max-width: 560px; margin: 48px auto; padding: 0 16px; text-align: center; }
    .not-found h1 { font-size: 1.5rem; margin: 0 0 12px; }
    .not-found p { margin: 0 0 24px; line-height: 1.5; }
    .nf-checking { display: flex; justify-content: center; margin: 48px auto; }
    .nf-actions { display: flex; flex-wrap: wrap; gap: 8px; justify-content: center; }
  `],
})
export class NotFoundComponent {
  /** Hat DIESE App eine eigene Hilfeseite? (RookHub ja, LeagueHub nein.) */
  readonly helpRoute = inject(Router).config.some(r => r.path === 'help');

  private readonly staleRoute = inject(StaleRouteReloadService);
  private destroyed = false;

  /** Sucht gerade nach einer neueren Fassung; bleibt `true`, wenn neu geladen wird. */
  readonly checking = signal(this.staleRoute.canTry());

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));
    if (!this.checking()) return;
    void this.staleRoute.reloadIfNewer(() => this.destroyed).then(reloading => {
      if (!reloading) this.checking.set(false);
    });
  }
}
