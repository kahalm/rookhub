import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { OwnSharedLine, RepertoireService } from '../../core/repertoire.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { SnackbarService } from '../../core/snackbar.service';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';

/**
 * Karte „Meine Teilen-Links" im Profil (Codereview A2-006, entschieden 25.10.2026): alle eigenen öffentlichen
 * Linien-Links (`/l/…`) mit „Widerrufen". Vorher gab es weder Liste noch Widerruf — ein Link lebte bis zur
 * Kontolöschung, sogar über das Löschen des Repertoires hinaus. Partie-Links (`/g/…`) widerruft man an der Partie
 * (Partieliste ⋮), weil jede gespeicherte Partie einen trägt; die Karte weist nur darauf hin.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-shared-links-card',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatIconModule, TranslatePipe, HelpHintComponent],
  template: `
    <div class="shared-links">
      <!-- d6-rk-profil-teilen-links-textstapel: Erklärung (Linien- und Partie-Links) hinter dem ?, sichtbar bleibt nur
           die Liste bzw. der Leersatz (DESIGN 2 Nr. 2), ohne Pfadkürzel (Nr. 7). -->
      <h4>{{ 'profile.sharedLinks.title' | translate }}&nbsp;<app-help-hint [text]="'profile.sharedLinks.help' | translate" /></h4>
      @if (error()) {
        <p class="hint error">{{ 'profile.sharedLinks.error' | translate }}</p>
      } @else if (lines(); as list) {
        @if (list.length === 0) {
          <p class="hint empty">{{ 'profile.sharedLinks.empty' | translate }}</p>
        } @else {
          <ul class="list">
            @for (l of list; track l.id) {
              <li class="row">
                <div class="what">
                  <span class="title">{{ label(l) }}</span>
                  <span class="meta">
                    {{ l.repertoireId === null ? ('profile.sharedLinks.fromExtension' | translate) : (l.repertoireName || '') }}
                    · {{ l.createdAt | date:'mediumDate' }}
                  </span>
                </div>
                <a mat-button class="open" [href]="'/l/' + l.shareToken" target="_blank" rel="noopener">
                  {{ 'profile.sharedLinks.open' | translate }}
                </a>
                <button mat-stroked-button color="warn" type="button" class="revoke" [disabled]="busy() === l.id" (click)="revoke(l)">
                  <mat-icon>link_off</mat-icon> {{ 'profile.sharedLinks.revoke' | translate }}
                </button>
              </li>
            }
          </ul>
        }
      }
    </div>
  `,
  styles: [`
    .shared-links h4 { margin: 0 0 0.5rem; color: var(--rh-accent); }
    .hint { margin: 0.25rem 0; color: var(--rh-text-secondary); font-size: 0.9em; }
    .list { list-style: none; margin: 0.5rem 0; padding: 0; display: flex; flex-direction: column; gap: 0.5rem; }
    .row { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; }
    .what { flex: 1 1 12rem; min-width: 0; display: flex; flex-direction: column; }
    .title { overflow-wrap: anywhere; }
    .meta { font-size: 0.85em; color: var(--rh-text-secondary); }
  `],
})
export class SharedLinksCardComponent implements OnInit {
  private readonly repertoires = inject(RepertoireService);
  private readonly confirm = inject(ConfirmService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  /** `null` = lädt noch. */
  readonly lines = signal<OwnSharedLine[] | null>(null);
  readonly error = signal(false);
  /** Id der Zeile, deren Widerruf gerade läuft. */
  readonly busy = signal<number | null>(null);

  ngOnInit(): void {
    this.repertoires.listSharedLines().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => this.lines.set(list),
      error: () => this.error.set(true),
    });
  }

  label(l: OwnSharedLine): string {
    return l.title || l.repertoireName || '—';
  }

  revoke(l: OwnSharedLine): void {
    this.confirm.ask('profile.sharedLinks.revokeConfirm', { title: this.label(l) })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(ok => {
        if (!ok) return;
        this.busy.set(l.id);
        this.repertoires.deleteSharedLine(l.id).subscribe({
          next: () => {
            this.busy.set(null);
            this.lines.update(list => (list ?? []).filter(x => x.id !== l.id));
            this.snackbar.success(this.translate.instant('profile.sharedLinks.revoked'));
          },
          error: () => {
            this.busy.set(null);
            this.snackbar.warn(this.translate.instant('profile.sharedLinks.revokeError'));
          },
        });
      });
  }
}
