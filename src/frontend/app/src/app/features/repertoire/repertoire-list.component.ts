import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { CreateRepertoireDialogComponent } from './create-repertoire-dialog.component';
import { ShareRepertoireDialogComponent, ShareRepertoireDialogData } from './share-repertoire-dialog.component';
import { forkJoin, of, catchError } from 'rxjs';
import { Repertoire } from '../../core/models';
import { RepertoireService } from '../../core/repertoire.service';
import { RepertoireKind, REPERTOIRE_KIND_LABELS } from '../../core/repertoire.types';
import { ReprocessBannerComponent } from '../../shared/reprocess-banner/reprocess-banner.component';
import { RepertoireTrainingService } from './repertoire-training.service';
import { saveRepertoireOffline, hasRepertoireOffline, removeRepertoireOffline, cachedRepertoires } from './repertoire-offline.util';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-repertoire-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatDialogModule, MatChipsModule, MatTooltipModule, TranslatePipe, LoadingSpinnerComponent, ReprocessBannerComponent],
  template: `
    <div class="repertoire-container">
      <div class="header">
        <h1>{{ 'repertoire.list.title' | translate }}</h1>
        <button mat-raised-button color="primary" (click)="openCreateDialog()">
          <mat-icon>add</mat-icon> {{ 'repertoire.list.new' | translate }}
        </button>
      </div>

      <div class="ext-hint">
        <mat-icon>extension</mat-icon>
        <span>
          {{ 'repertoire.list.extHint' | translate }}
          <a routerLink="/help" fragment="extension">{{ 'repertoire.list.extHintLink' | translate }}</a>
        </span>
      </div>

      <app-reprocess-banner section="repertoires" (done)="loadRepertoires()" />

      @if (offlineList) {
        <div class="offline-banner">
          <mat-icon>cloud_off</mat-icon>
          <span>{{ 'repertoire.list.offlineListHint' | translate }}</span>
        </div>
      }

      @if (loading) {
        <app-loading-spinner />
      } @else {
        @if (repertoires.length > 0) {
          <mat-form-field appearance="outline" class="list-search" subscriptSizing="dynamic">
            <mat-icon matPrefix>search</mat-icon>
            <input matInput [(ngModel)]="search" [placeholder]="'repertoire.list.searchPlaceholder' | translate"
                   [attr.aria-label]="'common.search' | translate">
            @if (search) {
              <button matSuffix mat-icon-button (click)="search = ''" [attr.aria-label]="'common.clear' | translate">
                <mat-icon>close</mat-icon>
              </button>
            }
          </mat-form-field>
        }
        @if (extensionRepertoires.length > 0) {
          <section class="repertoire-section">
            <h2 class="section-title">
              <mat-icon class="section-icon">extension</mat-icon>
              {{ 'repertoire.list.sectionExtension' | translate }}
            </h2>
            <p class="section-hint">{{ 'repertoire.list.sectionExtensionHint' | translate }}</p>
            <div class="repertoire-grid">
              @for (rep of extensionRepertoires; track rep.id) {
                <ng-container *ngTemplateOutlet="repCard; context: { $implicit: rep }"></ng-container>
              }
            </div>
          </section>
        }

        @if (otherRepertoires.length > 0) {
          <section class="repertoire-section">
            @if (extensionRepertoires.length > 0) {
              <h2 class="section-title">{{ 'repertoire.list.sectionOther' | translate }}</h2>
              <p class="section-hint">{{ 'repertoire.list.sectionOtherHint' | translate }}</p>
            }
            <div class="repertoire-grid">
              @for (rep of otherRepertoires; track rep.id) {
                <ng-container *ngTemplateOutlet="repCard; context: { $implicit: rep }"></ng-container>
              }
            </div>
          </section>
        }

        @if (sharedRepertoires.length > 0) {
          <section class="repertoire-section">
            <h2 class="section-title">
              <mat-icon class="section-icon">group</mat-icon>
              {{ 'repertoire.list.sectionSharedWithMe' | translate }}
            </h2>
            <p class="section-hint">{{ 'repertoire.list.sectionSharedWithMeHint' | translate }}</p>
            <div class="repertoire-grid">
              @for (rep of sharedRepertoires; track rep.id) {
                <ng-container *ngTemplateOutlet="repCard; context: { $implicit: rep }"></ng-container>
              }
            </div>
          </section>
        }

        @if (filteredRepertoires.length === 0) {
          <p>{{ (search ? 'repertoire.list.noMatch' : 'repertoire.list.empty') | translate:{ query: search } }}</p>
        }
      }
    </div>

    <ng-template #repCard let-rep>
      <mat-card>
        <mat-card-header>
          <mat-card-title>
            {{ rep.name }}
            @if (rep.kind !== Kind.None) {
              <mat-chip-set class="kind-chip-set">
                <mat-chip class="kind-chip" [class.kind-opening]="rep.kind === Kind.Opening" [class.kind-middlegame]="rep.kind === Kind.Middlegame" [class.kind-endgame]="rep.kind === Kind.Endgame">{{ kindLabel(rep.kind) | translate }}</mat-chip>
              </mat-chip-set>
            }
            @if (rep.useForExtension && !rep.isShared) {
              <mat-icon class="ext-badge"
                        [matTooltip]="'repertoire.list.extensionBadge' | translate">extension</mat-icon>
            }
          </mat-card-title>
          <mat-card-subtitle>
            {{ 'repertoire.list.fileCount' | translate: { count: rep.fileCount } }} | {{ (rep.isPublic ? 'repertoire.list.public' : 'repertoire.list.private') | translate }}
            @if (rep.isShared && rep.sharedByUsername) {
              · <mat-icon class="shared-badge-icon">group</mat-icon>{{ 'repertoire.share.sharedBy' | translate:{ name: rep.sharedByUsername } }}
            }
          </mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <p>{{ rep.description || ('repertoire.list.noDescription' | translate) }}</p>
        </mat-card-content>
        <mat-card-actions>
          @if (offlineList) {
            <!-- Offline-Fallback: nur das Training funktioniert (aus dem Offline-Cache). -->
            <a mat-button color="primary" [routerLink]="['/repertoires', rep.id, 'train']">
              <mat-icon>fitness_center</mat-icon> {{ 'repertoireTrainer.train' | translate }}
            </a>
          } @else {
            <button mat-button [routerLink]="['/repertoires', rep.id]">{{ 'repertoire.list.open' | translate }}</button>
            <button mat-button (click)="downloadPgn(rep)">{{ 'common.downloadPgn' | translate }}</button>
            @if (!rep.isShared) {
              <button mat-button [disabled]="converting === rep.id" (click)="convertToCourse(rep)">{{ 'repertoire.list.convertToCourse' | translate }}</button>
              <button mat-button (click)="openShareDialog(rep)">{{ 'repertoire.share.action' | translate }}</button>
              <button mat-button (click)="openEditDialog(rep)">{{ 'common.edit' | translate }}</button>
              <button mat-button color="warn" (click)="deleteRepertoire(rep.id)">{{ 'common.delete' | translate }}</button>
            }
            <button mat-icon-button class="offline-toggle" [disabled]="savingOffline === rep.id"
                    (click)="toggleOffline(rep)"
                    [matTooltip]="(isOffline(rep) ? 'repertoire.list.offlineRemoveTooltip' : 'repertoire.list.offlineSaveTooltip') | translate"
                    [attr.aria-label]="(isOffline(rep) ? 'repertoire.list.offlineRemoveTooltip' : 'repertoire.list.offlineSaveTooltip') | translate">
              <mat-icon>{{ isOffline(rep) ? 'cloud_done' : 'cloud_download' }}</mat-icon>
            </button>
          }
        </mat-card-actions>
      </mat-card>
    </ng-template>
  `,
  styles: [`
    .repertoire-container { padding: 2rem; max-width: 1200px; margin: 0 auto; }
    .header { display: flex; justify-content: space-between; align-items: center; }
    .ext-hint { display: flex; align-items: center; gap: 8px; margin: 0.5rem 0 1.25rem;
                padding: 10px 12px; border-radius: 8px; font-size: 0.9rem;
                background: color-mix(in srgb, currentColor 7%, transparent);
                color: color-mix(in srgb, currentColor 80%, transparent); }
    .ext-hint mat-icon { flex: 0 0 auto; opacity: 0.7; }
    .ext-hint a { color: inherit; text-decoration: underline; cursor: pointer; }
    .offline-banner { display: flex; align-items: center; gap: 8px; margin: 0 0 1rem;
                      padding: 10px 12px; border-radius: 8px; font-size: 0.9rem;
                      background: color-mix(in srgb, currentColor 7%, transparent);
                      color: color-mix(in srgb, currentColor 80%, transparent); }
    .offline-banner mat-icon { flex: 0 0 auto; opacity: 0.7; }
    .offline-toggle { margin-left: auto; }
    .list-search { width: 100%; max-width: 360px; display: block; margin-bottom: 1rem; }
    .repertoire-section { margin-bottom: 1.75rem; }
    .repertoire-section .section-title { display: flex; align-items: center; gap: 6px; margin: 0.25rem 0 0.15rem; font-size: 1.05rem; font-weight: 600; }
    .repertoire-section .section-icon { color: var(--mat-sys-primary, #3f51b5); }
    .repertoire-section .section-hint { margin: 0 0 0.65rem; color: color-mix(in srgb, currentColor 60%, transparent); font-size: 0.88rem; }
    .repertoire-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 1rem; }
    .kind-chip-set { display: inline-flex; margin-left: 8px; vertical-align: middle; }
    .kind-chip { font-size: 0.72rem; min-height: 22px; }
    .kind-chip.kind-opening { background-color: #1976d2; color: #fff; }
    .kind-chip.kind-middlegame { background-color: #7b1fa2; color: #fff; }
    .kind-chip.kind-endgame { background-color: #c62828; color: #fff; }
    /* Kleiner „RepCheck ok"-Marker im Karten-Titel, in Primärfarbe. */
    .ext-badge { color: var(--mat-sys-primary, #3f51b5); font-size: 18px; width: 18px; height: 18px; margin-left: 8px; vertical-align: middle; opacity: 0.9; }
    .shared-badge-icon { font-size: 14px; width: 14px; height: 14px; vertical-align: middle; opacity: 0.7; }
  `]
})
export class RepertoireListComponent implements OnInit {
  repertoires: Repertoire[] = [];
  /** Freitext-Suche (filtert clientseitig nach Name + Beschreibung). */
  search = '';
  loading = true;

  /** Repertoires nach Suchtext gefiltert (Name + Beschreibung, case-insensitive). */
  get filteredRepertoires(): Repertoire[] {
    const q = this.search.trim().toLowerCase();
    if (!q) return this.repertoires;
    return this.repertoires.filter(r =>
      (r.name || '').toLowerCase().includes(q) || (r.description || '').toLowerCase().includes(q));
  }

  /** Für die RepCheck-Extension aktive Repertoires — als eigener Block oben (nur eigene). */
  get extensionRepertoires(): Repertoire[] {
    return this.filteredRepertoires.filter(r => r.useForExtension && !r.isShared);
  }

  /** Alle übrigen eigenen Repertoires (Archiv / nicht für die Extension aktiv). */
  get otherRepertoires(): Repertoire[] {
    return this.filteredRepertoires.filter(r => !r.useForExtension && !r.isShared);
  }

  /** Von anderen Nutzern mit mir geteilte Repertoires (eigene Sektion „Mit mir geteilt"). */
  get sharedRepertoires(): Repertoire[] {
    return this.filteredRepertoires.filter(r => r.isShared);
  }
  /** Enum im Template referenzierbar (statt Magic-Numbers 1/2/3 für die Kind-Chip-Klassen). */
  readonly Kind = RepertoireKind;
  /** id des Repertoires, das gerade in einen Kurs umgewandelt wird (Button-Sperre). */
  converting: number | null = null;
  /** id des Repertoires, das gerade offline gespeichert wird (Button-Sperre). */
  savingOffline: number | null = null;
  /** true = Server nicht erreichbar; Anzeige aus dem Offline-Cache (nur heruntergeladene). */
  offlineList = false;

  constructor(private repertoireService: RepertoireService, private training: RepertoireTrainingService, private dialog: MatDialog, private snackbar: SnackbarService, private translate: TranslateService) {}

  kindLabel(kind: RepertoireKind): string {
    return REPERTOIRE_KIND_LABELS[kind] ?? 'repertoire.kind.none';
  }

  ngOnInit(): void {
    this.loadRepertoires();
  }

  loadRepertoires(): void {
    this.loading = true;
    this.repertoireService.list().subscribe({
      next: (r) => { this.repertoires = r; this.offlineList = false; this.loading = false; },
      error: () => {
        // Offline/Server weg → heruntergeladene Repertoires aus dem Offline-Cache zeigen
        // (nur Trainieren möglich). Ohne Cache bleibt es beim bisherigen Fehlerhinweis.
        const cached = cachedRepertoires();
        if (cached.length > 0) {
          this.repertoires = cached;
          this.offlineList = true;
        } else {
          this.snackbar.info(this.translate.instant('repertoire.list.loadFailed'));
        }
        this.loading = false;
      }
    });
  }

  isOffline(rep: Repertoire): boolean {
    return hasRepertoireOffline(rep.id);
  }

  /** Repertoire fürs Offline-Training herunterladen (PGN + SR-Zustände + Intervalle) bzw. die
   * Offline-Kopie wieder entfernen. */
  toggleOffline(rep: Repertoire): void {
    if (hasRepertoireOffline(rep.id)) {
      removeRepertoireOffline(rep.id);
      this.snackbar.info(this.translate.instant('repertoire.list.offlineRemoved', { name: rep.name }), { action: 'common.ok', duration: 2000 });
      return;
    }
    this.savingOffline = rep.id;
    forkJoin({
      pgn: this.repertoireService.getPgnText(rep.id),
      states: this.training.getLineStates(rep.id),
      // Config ist fürs Offline-Fälligkeits-Rechnen nice-to-have — ohne sie greifen die Defaults.
      config: this.training.getConfig(rep.id).pipe(catchError(() => of(null))),
    }).subscribe({
      next: ({ pgn, states, config }) => {
        this.savingOffline = null;
        const ok = saveRepertoireOffline({ meta: rep, pgn, states, config: config?.effective ?? null, savedAt: new Date().toISOString() });
        const key = ok ? 'repertoire.list.offlineSaved' : 'repertoire.list.offlineFailed';
        this.snackbar.info(this.translate.instant(key, { name: rep.name }), { action: 'common.ok', duration: 2500 });
      },
      error: () => {
        this.savingOffline = null;
        this.snackbar.info(this.translate.instant('repertoire.list.offlineFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }

  openCreateDialog(): void {
    const dialogRef = this.dialog.open(CreateRepertoireDialogComponent, { width: '400px', maxWidth: '95vw' });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.repertoireService.create(result).subscribe({
          next: () => this.loadRepertoires(),
          error: () => this.snackbar.info(this.translate.instant('repertoire.list.createFailed'))
        });
      }
    });
  }

  /** Öffnet den „Repertoire teilen"-Dialog (Freunde auswählen / Freigaben verwalten). */
  openShareDialog(rep: Repertoire): void {
    this.dialog.open<ShareRepertoireDialogComponent, ShareRepertoireDialogData>(
      ShareRepertoireDialogComponent, {
        width: '440px', maxWidth: '95vw',
        data: { repertoireId: rep.id, repertoireName: rep.name }
      });
  }

  openEditDialog(rep: Repertoire): void {
    const dialogRef = this.dialog.open(CreateRepertoireDialogComponent, { width: '400px', maxWidth: '95vw', data: rep });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.repertoireService.update(rep.id, result).subscribe({
          next: () => this.loadRepertoires(),
          error: () => this.snackbar.info(this.translate.instant('repertoire.list.updateFailed'))
        });
      }
    });
  }

  deleteRepertoire(id: number): void {
    if (confirm(this.translate.instant('repertoire.list.deleteConfirm'))) {
      this.repertoireService.remove(id).subscribe({
        next: () => this.loadRepertoires(),
        error: () => this.snackbar.info(this.translate.instant('repertoire.list.deleteFailed'))
      });
    }
  }

  /** Repertoire in einen persönlichen Kurs umwandeln (nur bei Puzzle-PGN im Chessable-Stil). */
  convertToCourse(rep: Repertoire): void {
    this.converting = rep.id;
    this.repertoireService.convertToCourse(rep.id).subscribe({
      next: course => {
        this.converting = null;
        // Verschieben: das Original-Repertoire wurde serverseitig entfernt → aus der Liste nehmen.
        this.repertoires = this.repertoires.filter(r => r.id !== rep.id);
        this.snackbar.info(this.translate.instant('repertoire.list.convertedToCourse', { name: course.displayName }), { action: 'common.ok', duration: 3000 });
      },
      error: (e) => {
        this.converting = null;
        // 400 = kein quiz-barer Inhalt (reines Eröffnungs-PGN) → klaren Hinweis zeigen.
        const key = e?.status === 400 ? 'repertoire.list.convertToCourseNoPuzzles' : 'repertoire.list.convertToCourseFailed';
        this.snackbar.info(this.translate.instant(key), { action: 'common.ok', duration: 4000 });
      }
    });
  }

  downloadPgn(rep: Repertoire): void {
    this.repertoireService.downloadPgn(rep.id).subscribe({
      next: blob => {
        const safe = (rep.name || 'repertoire').replace(/[^A-Za-z0-9]+/g, '_');
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = `${safe}.pgn`;
        a.click();
        URL.revokeObjectURL(a.href);
      },
      error: () => this.snackbar.info(this.translate.instant('common.downloadFailed'))
    });
  }
}
