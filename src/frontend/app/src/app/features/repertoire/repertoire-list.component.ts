import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatChipsModule } from '@angular/material/chips';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { CreateRepertoireDialogComponent } from './create-repertoire-dialog.component';
import { Repertoire } from '../../core/models';
import { RepertoireKind, REPERTOIRE_KIND_LABELS } from '../../core/repertoire.types';

@Component({
  selector: 'app-repertoire-list',
  standalone: true,
  imports: [CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule, MatDialogModule, MatChipsModule, TranslateModule, LoadingSpinnerComponent],
  template: `
    <div class="repertoire-container">
      <div class="header">
        <h1>{{ 'repertoire.list.title' | translate }}</h1>
        <button mat-raised-button color="primary" (click)="openCreateDialog()">
          <mat-icon>add</mat-icon> {{ 'repertoire.list.new' | translate }}
        </button>
      </div>

      @if (loading) {
        <app-loading-spinner />
      } @else {
        <div class="repertoire-grid">
          @for (rep of repertoires; track rep.id) {
            <mat-card>
              <mat-card-header>
                <mat-card-title>
                  {{ rep.name }}
                  @if (rep.kind && rep.kind !== 0) {
                    <mat-chip-set class="kind-chip-set">
                      <mat-chip class="kind-chip" [class.kind-opening]="rep.kind === 1" [class.kind-middlegame]="rep.kind === 2" [class.kind-endgame]="rep.kind === 3">{{ kindLabel(rep.kind) | translate }}</mat-chip>
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
                <button mat-button (click)="openEditDialog(rep)">{{ 'common.edit' | translate }}</button>
                <button mat-button color="warn" (click)="deleteRepertoire(rep.id)">{{ 'common.delete' | translate }}</button>
              </mat-card-actions>
            </mat-card>
          } @empty {
            <p>{{ 'repertoire.list.empty' | translate }}</p>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .repertoire-container { padding: 2rem; max-width: 1200px; margin: 0 auto; }
    .header { display: flex; justify-content: space-between; align-items: center; }
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
  loading = true;

  constructor(private http: HttpClient, private dialog: MatDialog, private snackbar: SnackbarService, private translate: TranslateService) {}

  kindLabel(kind: number): string {
    return REPERTOIRE_KIND_LABELS[kind as RepertoireKind] ?? 'repertoire.kind.none';
  }

  ngOnInit(): void {
    this.loadRepertoires();
  }

  loadRepertoires(): void {
    this.loading = true;
    this.http.get<Repertoire[]>('/api/repertoires').subscribe({
      next: (r) => { this.repertoires = r; this.loading = false; },
      error: () => { this.loading = false; this.snackbar.info(this.translate.instant('repertoire.list.loadFailed')); }
    });
  }

  openCreateDialog(): void {
    const dialogRef = this.dialog.open(CreateRepertoireDialogComponent, { width: '400px' });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.http.post('/api/repertoires', result).subscribe({
          next: () => this.loadRepertoires(),
          error: () => this.snackbar.info(this.translate.instant('repertoire.list.createFailed'))
        });
      }
    });
  }

  openEditDialog(rep: Repertoire): void {
    const dialogRef = this.dialog.open(CreateRepertoireDialogComponent, { width: '400px', data: rep });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.http.put(`/api/repertoires/${rep.id}`, result).subscribe({
          next: () => this.loadRepertoires(),
          error: () => this.snackbar.info(this.translate.instant('repertoire.list.updateFailed'))
        });
      }
    });
  }

  deleteRepertoire(id: number): void {
    if (confirm(this.translate.instant('repertoire.list.deleteConfirm'))) {
      this.http.delete(`/api/repertoires/${id}`).subscribe({
        next: () => this.loadRepertoires(),
        error: () => this.snackbar.info(this.translate.instant('repertoire.list.deleteFailed'))
      });
    }
  }
}
