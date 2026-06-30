import { Component, OnInit } from '@angular/core';
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
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { CreateRepertoireDialogComponent } from './create-repertoire-dialog.component';
import { Repertoire } from '../../core/models';
import { RepertoireService } from '../../core/repertoire.service';
import { RepertoireKind, REPERTOIRE_KIND_LABELS } from '../../core/repertoire.types';
import { ReprocessBannerComponent } from '../../shared/reprocess-banner/reprocess-banner.component';

@Component({
  selector: 'app-repertoire-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatDialogModule, MatChipsModule, TranslateModule, LoadingSpinnerComponent, ReprocessBannerComponent],
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
        <div class="repertoire-grid">
          @for (rep of filteredRepertoires; track rep.id) {
            <mat-card>
              <mat-card-header>
                <mat-card-title>
                  {{ rep.name }}
                  @if (rep.kind !== Kind.None) {
                    <mat-chip-set class="kind-chip-set">
                      <mat-chip class="kind-chip" [class.kind-opening]="rep.kind === Kind.Opening" [class.kind-middlegame]="rep.kind === Kind.Middlegame" [class.kind-endgame]="rep.kind === Kind.Endgame">{{ kindLabel(rep.kind) | translate }}</mat-chip>
                    </mat-chip-set>
                  }
                </mat-card-title>
                <mat-card-subtitle>{{ 'repertoire.list.fileCount' | translate: { count: rep.fileCount } }} | {{ (rep.isPublic ? 'repertoire.list.public' : 'repertoire.list.private') | translate }}</mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <p>{{ rep.description || ('repertoire.list.noDescription' | translate) }}</p>
              </mat-card-content>
              <mat-card-actions>
                <button mat-button [routerLink]="['/repertoires', rep.id]">{{ 'repertoire.list.open' | translate }}</button>
                <button mat-button (click)="downloadPgn(rep)">{{ 'common.downloadPgn' | translate }}</button>
                <button mat-button (click)="openEditDialog(rep)">{{ 'common.edit' | translate }}</button>
                <button mat-button color="warn" (click)="deleteRepertoire(rep.id)">{{ 'common.delete' | translate }}</button>
              </mat-card-actions>
            </mat-card>
          } @empty {
            <p>{{ (search ? 'repertoire.list.noMatch' : 'repertoire.list.empty') | translate:{ query: search } }}</p>
          }
        </div>
      }
    </div>
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
    .list-search { width: 100%; max-width: 360px; display: block; margin-bottom: 1rem; }
    .repertoire-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 1rem; }
    .kind-chip-set { display: inline-flex; margin-left: 8px; vertical-align: middle; }
    .kind-chip { font-size: 0.72rem; min-height: 22px; }
    .kind-chip.kind-opening { background-color: #1976d2; color: #fff; }
    .kind-chip.kind-middlegame { background-color: #7b1fa2; color: #fff; }
    .kind-chip.kind-endgame { background-color: #c62828; color: #fff; }
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
  /** Enum im Template referenzierbar (statt Magic-Numbers 1/2/3 für die Kind-Chip-Klassen). */
  readonly Kind = RepertoireKind;

  constructor(private repertoireService: RepertoireService, private dialog: MatDialog, private snackbar: SnackbarService, private translate: TranslateService) {}

  kindLabel(kind: RepertoireKind): string {
    return REPERTOIRE_KIND_LABELS[kind] ?? 'repertoire.kind.none';
  }

  ngOnInit(): void {
    this.loadRepertoires();
  }

  loadRepertoires(): void {
    this.loading = true;
    this.repertoireService.list().subscribe({
      next: (r) => { this.repertoires = r; this.loading = false; },
      error: () => { this.loading = false; this.snackbar.info(this.translate.instant('repertoire.list.loadFailed')); }
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
