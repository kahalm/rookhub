import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { MenuService } from '@rh/core/menu.service';

/**
 * Der rote Streifen „du bist als X unterwegs" samt Ausgang.
 *
 * <p>Er steht in BEIDEN Oberflaechen (RookHub und Turnierseite) und ist deshalb eine eigene
 * Komponente statt zweimal derselbe Block: ein Einstieg ohne sichtbaren Hinweis ist die
 * gefaehrliche Variante — der Admin haelt die fremde Sicht fuer seine eigene und aendert Daten,
 * die ihm nicht gehoeren. Genau das darf auf der Turnierseite nicht anders sein als hier.</p>
 *
 * <p>Der Ausgang fuehrt auf `/admin`: dort ist der Einstieg passiert, und beide Seiten haben
 * inzwischen eine solche Seite. `MenuService.refresh()` muss dabei sein — die Sichtbarkeit von
 * Menuepunkten haengt an Rolle und Gruppen des ANGEMELDETEN Kontos, und die wechselt hier
 * zurueck.</p>
 */
@Component({
  selector: 'app-impersonation-banner',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [TranslatePipe],
  template: `
    @if (auth.isImpersonating) {
      <div class="imp-banner">
        <span class="imp-text">
          <span class="imp-icon">&#x1F464;</span>
          {{ 'app.impersonation.banner' | translate: { user: auth.currentUser?.username, admin: auth.impersonatorUsername } }}
        </span>
        <button class="imp-exit" (click)="exit()">{{ 'app.impersonation.exit' | translate }}</button>
      </div>
    }
  `,
  styles: [`
    .imp-banner {
      display: flex; align-items: center; justify-content: center; gap: 12px; flex-wrap: wrap;
      background: #b71c1c; color: #fff; padding: 4px 12px; font-size: 0.85rem; font-weight: 500;
      position: sticky; top: 0; z-index: 1100;
    }
    .imp-icon { margin-right: 4px; }
    /* min-height 40px: der Knopf war ~24px hoch und am Handy schwer zu treffen — die einzige
       Aktion im Streifen. Ein <button> zentriert seinen Inhalt vertikal selbst; das Streifen-
       Padding oben ist dafuer von 6 auf 4px runter, damit er nur um ~12px waechst. */
    .imp-exit {
      background: rgba(255,255,255,0.18); color: #fff; border: 1px solid rgba(255,255,255,0.5);
      border-radius: 4px; min-height: 40px; padding: 0 12px; cursor: pointer; font: inherit; font-weight: 600;
    }
    .imp-exit:hover { background: rgba(255,255,255,0.3); }
  `],
})
export class ImpersonationBannerComponent {
  readonly auth = inject(AuthService);
  private readonly menu = inject(MenuService);
  private readonly router = inject(Router);

  exit(): void {
    this.auth.stopImpersonation();
    this.menu.refresh();
    void this.router.navigate(['/admin']);
  }
}
