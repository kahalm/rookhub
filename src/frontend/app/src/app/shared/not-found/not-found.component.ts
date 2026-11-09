import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { AppUpdateCheck, AppUpdateService } from '../../core/app-update.service';
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
 * Eine alte Fassung kennt neue Adressen nicht. Bevor der Text erscheint, deshalb zwei Stufen, beide hinter dem Ladekreis
 * (sonst blitzte der Text kurz auf):
 * 1. Automatisch, einmal je Adresse und Tab (`StaleRouteReloadService`): neuere Fassung im Service Worker aktivieren und
 *    dieselbe Adresse neu laden. Wird neu geladen, bleibt die Seite beim Ladekreis.
 * 2. Fallback (U1), wenn Stufe 1 nicht neu lädt — kein Service Worker (LeagueHub), Adresse schon einmal neu geladen,
 *    nichts zu aktivieren, Zeitgrenze, Fehler: `AppUpdateService.checkNow` vergleicht die Server-Version
 *    (`/assets/version.json`). Ist sie neuer, Hinweis mit „Aktualisieren“ (aktiviert, falls vorhanden, und lädt DIESELBE
 *    Adresse neu; ohne Service Worker ein einfacher Reload), sonst und bei jedem Fehler die normale Meldung.
 * `phase` ist ein Signal, weil die Ergebnisse asynchron eintreffen und die OnPush-View sonst nicht neu gezeichnet würde.
 */
@Component({
  selector: 'app-not-found',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    @if (phase() === 'checking') {
      <div class="nf-checking" role="status" [attr.aria-label]="'common.loading' | translate">
        <mat-spinner diameter="40"></mat-spinner>
      </div>
    } @else {
      <section class="not-found">
        <h1>{{ 'app.notFound.title' | translate }}</h1>
        @if (phase() === 'update') {
          <p class="nf-update" role="status">
            @if (update()?.version; as v) { {{ 'app.notFound.updateText' | translate: { version: v } }} }
            @else { {{ 'app.notFound.updateTextPlain' | translate }} }
          </p>
        } @else {
          <p>{{ 'app.notFound.text' | translate }}</p>
        }
        <div class="nf-actions">
          @if (phase() === 'update') {
            <button mat-flat-button type="button" class="nf-reload" [disabled]="reloading()" (click)="reload()">
              {{ 'app.notFound.update' | translate }}</button>
            <a mat-button routerLink="/">{{ 'app.notFound.home' | translate }}</a>
          } @else {
            <a mat-flat-button routerLink="/">{{ 'app.notFound.home' | translate }}</a>
          }
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
  private readonly appUpdate = inject(AppUpdateService);
  private destroyed = false;

  /** `checking`: Stufe 1 oder 2 läuft (bleibt so, wenn neu geladen wird); `update`: Server neuer; `none`: Text. */
  readonly phase = signal<'checking' | 'update' | 'none'>('checking');
  readonly update = signal<AppUpdateCheck | null>(null);
  readonly reloading = signal(false);

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));
    void this.run();
  }

  private async run(): Promise<void> {
    if (this.staleRoute.canTry() && await this.staleRoute.reloadIfNewer(() => this.destroyed)) return;
    if (this.destroyed) return;
    try {
      const r = await this.appUpdate.checkNow();
      this.update.set(r);
      this.phase.set(r.available ? 'update' : 'none');
    } catch {
      this.phase.set('none');
    }
  }

  reload(): void {
    this.reloading.set(true);
    void this.appUpdate.activateAndReload();
  }
}
