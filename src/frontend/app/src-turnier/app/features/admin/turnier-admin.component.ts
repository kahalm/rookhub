import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AdminService, AdminUser } from '@rh/core/admin.service';
import { AuthService } from '@rh/core/auth.service';
import { MenuService } from '@rh/core/menu.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { RookHubLinkComponent } from '../../shell/rookhub-link.component';

/**
 * Die Admin-Seite der Turnierseite — und sie hat GENAU eine Aufgabe: als ein Nutzer einsteigen.
 *
 * <p><b>Warum nicht RookHubs Admin-Panel hierher haengen</b> (der naheliegende Weg, und der
 * ueberlegte Ausgangspunkt): das sind zehn Laschen — Buecher, Tagespuzzle, Puzzle-Themen,
 * Chessable-Downloads, Menue-Sichtbarkeit, CI. Jede davon fuehrt zu Bereichen, die es auf der
 * Turnierseite nicht gibt; die Laschen waeren also mehrheitlich Wege ins Leere. Was hier fehlte,
 * ist der Einstieg in ein fremdes Konto — „warum sieht der Nutzer den Kalender leer?" laesst sich
 * nur aus dessen Sicht beantworten. Der Rest bleibt in RookHub, einen Klick entfernt.</p>
 *
 * <p>Die Mechanik ist die BESTEHENDE, nicht eine zweite: `AdminService.impersonate` holt das
 * Token vom Server (es traegt die Rollen des ZIELS, nicht die des Admins),
 * `AuthService.impersonate` sichert die Admin-Anmeldung weg und uebernimmt es, und der rote
 * Streifen samt Rueckweg ist `ImpersonationBannerComponent` in der Huelle beider Oberflaechen.</p>
 *
 * <p>Der Zustand liegt in SIGNALEN: die Nutzerliste kommt aus einer HTTP-Antwort, und die trifft
 * ausserhalb der Angular-Zone ein (`provideHttpClient()` laeuft ueber `fetch`) — eine
 * Feldzuweisung loeste hier keine Aenderungserkennung aus, die Liste blieb leer.</p>
 */
@Component({
  selector: 'trn-admin',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [
    FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatTooltipModule, TranslatePipe, LoadingSpinnerComponent, RookHubLinkComponent,
  ],
  template: `
    <div class="ta-page">
      <h1>{{ 'turnierAdmin.title' | translate }}</h1>
      <!-- „Alles Uebrige bleibt in RookHub" — mit dem Weg dorthin (Codereview UX-079). -->
      <p class="muted">
        {{ 'turnierAdmin.lead' | translate }}
        <trn-rookhub-link path="admin">{{ 'turnierAdmin.rookhubAdmin' | translate }}</trn-rookhub-link>
      </p>

      <mat-card class="ta-card">
        <mat-form-field appearance="outline" class="ta-search">
          <!-- Eigener Schluessel: RookHubs Admin-Panel sagt durchgehend „User", diese Seite „Nutzer". -->
          <mat-label>{{ 'turnierAdmin.searchLabel' | translate }}</mat-label>
          <input matInput [(ngModel)]="search" (keyup.enter)="load()">
          <button matSuffix mat-icon-button (click)="load()"
                  [attr.aria-label]="'turnierAdmin.searchLabel' | translate">
            <mat-icon>search</mat-icon>
          </button>
        </mat-form-field>

        @if (loading()) {
          <app-loading-spinner />
        } @else if (!users().length) {
          <p class="muted">{{ 'turnierAdmin.none' | translate }}</p>
        } @else {
          <ul class="ta-users">
            @for (u of users(); track u.id) {
              <li>
                <span class="ta-name">
                  {{ u.username }}
                  @if (u.isAdmin) {
                    <mat-icon inline class="ta-admin"
                              [matTooltip]="'admin.users.columns.admin' | translate">shield</mat-icon>
                  }
                </span>
                <span class="muted ta-mail">{{ u.email }}</span>
                <!-- Vorgelesen mit dem Benutzernamen: in der Schaltflaechenliste eines Screenreaders
                     stuenden sonst bis zu 50-mal dieselben Worte ohne Bezug. Der sichtbare Text steht
                     vorne, damit Sprachsteuerung ihn weiter trifft (WCAG 2.5.3). -->
                <!-- Ins eigene Konto lehnt der Server ab (400) — ein Knopf dafuer fuehrte nur zur
                     Meldung „fehlgeschlagen" (Codereview UX-078). -->
                @if (u.id === selfId) {
                  <span class="muted ta-self">{{ 'turnierAdmin.self' | translate }}</span>
                } @else {
                  <button mat-stroked-button (click)="impersonate(u)" [disabled]="busyId() !== null"
                          [attr.aria-label]="'turnierAdmin.impersonateAria' | translate: { action: ('admin.users.impersonate' | translate), name: u.username }">
                    <mat-icon>login</mat-icon>
                    {{ 'admin.users.impersonate' | translate }}
                  </button>
                }
              </li>
            }
          </ul>
          <!-- Geholt wird eine Seite (50): wer weiter hinten steht, fehlte bisher ohne jeden Hinweis. -->
          @if (total() > users().length) {
            <p class="muted ta-more">
              {{ 'turnierAdmin.truncated' | translate: { shown: users().length, total: total() } }}
            </p>
          }
        }
      </mat-card>
    </div>
  `,
  styles: [`
    .ta-page { max-width: min(var(--page-max-width), 96vw); margin: 0 auto; padding: 16px; }
    .ta-card { margin-top: 12px; padding: 16px; }
    .ta-search { width: 100%; max-width: 420px; }
    .ta-users { list-style: none; margin: 0; padding: 0; }
    .ta-users li {
      display: flex; align-items: center; gap: 12px; flex-wrap: wrap;
      padding: 8px 0; border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .ta-users li:last-child { border-bottom: 0; }
    /* Ein Benutzername (bis 50 Zeichen, ohne Leerzeichen) konnte nicht umbrechen: auf 360 px ist die
       Zeile 296 px breit, ein 45-Zeichen-Name misst 386 px - Karte und Seite scrollten horizontal.
       Spiegel von .ta-mail, ohne max-width. */
    .ta-name { font-weight: 500; min-width: 0; overflow-wrap: anywhere; }
    .ta-admin { color: var(--mat-sys-primary); }
    /* Vom Template benutzt, war aber nirgends definiert (View-Encapsulation laesst die .muted-Regeln
       der anderen Turnier-Komponenten nicht hierher wirken): im umgebrochenen Textlauf auf dem Handy
       standen Name und E-Mail in derselben Farbe, die Adresse las sich wie ein zweiter Name. */
    .muted { color: var(--mat-sys-on-surface-variant); }
    /* Die Adresse schiebt den Knopf nach rechts und bricht selbst um, statt die Zeile zu dehnen. */
    .ta-mail { flex: 1 1 12rem; min-width: 0; overflow-wrap: anywhere; }
    .ta-more { margin: 12px 0 0; }
  `],
})
export class TurnierAdminComponent implements OnInit {
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly menu = inject(MenuService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);

  search = '';
  readonly users = signal<AdminUser[]>([]);
  /** Wie viele Konten zur Suche passen — die Liste zeigt hoechstens die erste Seite davon. */
  readonly total = signal(0);
  readonly loading = signal(true);
  /** Wessen Einstieg gerade laeuft — sperrt alle Knoepfe, nicht nur den einen. */
  readonly busyId = signal<number | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.admin.getUsers(this.search, 1, 50).subscribe({
      next: res => {
        this.users.set(res.items ?? []);
        this.total.set(res.totalCount ?? res.items?.length ?? 0);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackbar.warn(this.translate.instant('turnierAdmin.loadFailed'));
      },
    });
  }

  /** Das angemeldete Konto — fuer das gibt es keinen Einstieg. */
  get selfId(): number | null {
    return this.auth.currentUser?.userId ?? null;
  }

  /**
   * Einsteigen und auf den Kalender wechseln — auf DIESER Seite ist das die Ansicht, um deren
   * Inhalt es geht. RookHub schickt an dieser Stelle ins Dashboard. In ein ADMIN-Konto erst nach
   * Rueckfrage: erlaubt ist es (Support-Fall), aber man handelt dann mit fremden Admin-Rechten.
   */
  impersonate(u: AdminUser): void {
    if (this.busyId() !== null || u.id === this.selfId) return;
    if (!u.isAdmin) {
      this.startImpersonation(u);
      return;
    }
    this.confirm.ask('turnierAdmin.confirmAdmin', { name: u.username })
      .subscribe(ok => { if (ok) this.startImpersonation(u); });
  }

  private startImpersonation(u: AdminUser): void {
    if (this.busyId() !== null) return;
    this.busyId.set(u.id);

    this.admin.impersonate(u.id).subscribe({
      next: res => {
        this.busyId.set(null);
        // Admin-Sicherung nicht schreibbar (Speicher voll/gesperrt): AuthService steigt dann NICHT ein —
        // ohne Sicherung gaebe es keinen Ruecksprung. Ohne diese Pruefung stuende hier „Eingestiegen als X"
        // samt Kalender, die Sitzung waere aber weiter die des Admins: Suchprofile und Favoriten, die er
        // „als Nutzer" anlegt, landeten in seinem eigenen Konto.
        if (!this.auth.impersonate(res)) {
          this.snackbar.warn(this.translate.instant('admin.users.impersonateFailed'));
          return;
        }
        this.menu.refresh();
        this.snackbar.info(this.translate.instant(
          'admin.users.impersonateStarted', { name: u.username }));
        void this.router.navigate(['/tournaments/calendar']);
      },
      error: () => {
        this.busyId.set(null);
        this.snackbar.warn(this.translate.instant('admin.users.impersonateFailed'));
      },
    });
  }
}
