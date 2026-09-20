import { Component, OnInit, inject, ChangeDetectionStrategy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { ReconstructService, ReconstructionListItem } from './reconstruct.service';

/**
 * Übersicht der Partien, die gerade rekonstruiert werden — anlegen, öffnen, löschen.
 *
 * <p>Der Fortschritt steht als Zahl auf der Karte: wie viele Halbzüge ab der Grundstellung schon
 * lückenlos stehen und wie viele Lücken noch offen sind. Das ist die Frage, die man an eine halb
 * zusammengesetzte Partie hat; die Zahl der Teile allein beantwortet sie nicht.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-reconstruct-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule,
    MatFormFieldModule, MatIconModule, MatInputModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="page">
      <div class="head">
        <div>
          <h1>{{ 'reconstruct.title' | translate }}</h1>
          <p class="muted">{{ 'reconstruct.intro' | translate }}</p>
        </div>
      </div>

      <mat-card class="new-card">
        <mat-form-field appearance="outline" class="grow">
          <mat-label>{{ 'reconstruct.newTitle' | translate }}</mat-label>
          <input matInput [(ngModel)]="newTitle" (keyup.enter)="create()"
                 [placeholder]="'reconstruct.newTitlePlaceholder' | translate" maxlength="200">
        </mat-form-field>
        <button mat-flat-button color="primary" [disabled]="busy() || !newTitle.trim()" (click)="create()">
          <mat-icon>add</mat-icon> {{ 'reconstruct.create' | translate }}
        </button>
      </mat-card>

      @if (loading()) {
        <p class="muted">{{ 'common.loading' | translate }}</p>
      } @else if (items().length === 0) {
        <p class="muted empty">{{ 'reconstruct.empty' | translate }}</p>
      } @else {
        <div class="grid">
          @for (item of items(); track item.id) {
            <mat-card class="item">
              <a class="item-title" [routerLink]="['/reconstruct', item.id]">{{ item.title }}</a>
              @if (item.white || item.black) {
                <div class="muted">{{ item.white || '?' }} – {{ item.black || '?' }}</div>
              }
              <div class="chips">
                <span class="chip">{{ 'reconstruct.partsCount' | translate:{ count: item.partCount } }}</span>
                <span class="chip ok">{{ 'reconstruct.knownPlies' | translate:{ count: item.knownPlies } }}</span>
                @if (item.gaps > 0) {
                  <span class="chip warn">{{ 'reconstruct.gaps' | translate:{ count: item.gaps } }}</span>
                }
              </div>
              <div class="actions">
                <a mat-button [routerLink]="['/reconstruct', item.id]">
                  <mat-icon>edit</mat-icon> {{ 'reconstruct.open' | translate }}
                </a>
                <button mat-icon-button [disabled]="busy()" (click)="remove(item)"
                        [matTooltip]="'common.delete' | translate"
                        [attr.aria-label]="'common.delete' | translate">
                  <mat-icon>delete</mat-icon>
                </button>
              </div>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .page { max-width: min(var(--page-max-width), 96vw); margin: 0 auto; padding: 1rem; }
    h1 { margin: 0 0 0.25rem; }
    .muted { opacity: 0.75; }
    .empty { margin-top: 1rem; }
    /* flex-direction MUSS hier stehen: mat-card ist selbst ein Flexbox in SPALTEN-Richtung, und
       ohne diese Zeile wirkte \`flex: 1 1 260px\` unten auf die HOEHE — das Titelfeld stand als
       212 x 260 px grosser Kasten da statt als Textzeile. */
    .new-card { display: flex; flex-direction: row; gap: 0.75rem; align-items: center;
                padding: 0.75rem 1rem; margin: 1rem 0; flex-wrap: wrap; }
    .grow { flex: 1 1 260px; margin-bottom: -1.25em; }
    .grid { display: grid; gap: 0.75rem; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); }
    .item { padding: 0.75rem 1rem; }
    .item-title { font-weight: 600; font-size: 1.05rem; text-decoration: none; }
    .chips { display: flex; gap: 0.4rem; flex-wrap: wrap; margin: 0.5rem 0; }
    .chip { font-size: 0.8rem; padding: 2px 8px; border-radius: 10px;
            background: color-mix(in srgb, currentColor 12%, transparent); }
    .chip.ok { background: color-mix(in srgb, #2e7d32 28%, transparent); }
    .chip.warn { background: color-mix(in srgb, #e65100 30%, transparent); }
    .actions { display: flex; align-items: center; justify-content: space-between; }
  `],
})
export class ReconstructListComponent implements OnInit {
  private service = inject(ReconstructService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private router = inject(Router);

  // Signale, weil die Antworten ausserhalb der Angular-Zone eintreffen (fetch-HttpClient).
  readonly items = signal<ReconstructionListItem[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  newTitle = '';

  ngOnInit(): void { this.load(); }

  private load(): void {
    this.loading.set(true);
    this.service.list().subscribe({
      next: rows => { this.items.set(rows); this.loading.set(false); },
      error: () => {
        this.loading.set(false);
        this.snackbar.warn(this.translate.instant('reconstruct.loadFailed'));
      },
    });
  }

  create(): void {
    const title = this.newTitle.trim();
    if (!title || this.busy()) return;
    this.busy.set(true);
    this.service.create({ title }).subscribe({
      next: created => {
        this.busy.set(false);
        this.newTitle = '';
        this.router.navigate(['/reconstruct', created.id]);
      },
      error: err => {
        this.busy.set(false);
        const reason = err?.error?.reason === 'too-many' ? 'reconstruct.tooMany' : 'reconstruct.saveFailed';
        this.snackbar.warn(this.translate.instant(reason));
      },
    });
  }

  remove(item: ReconstructionListItem): void {
    if (!confirm(this.translate.instant('reconstruct.deleteConfirm', { title: item.title }))) return;
    this.busy.set(true);
    this.service.remove(item.id).subscribe({
      next: () => { this.busy.set(false); this.items.set(this.items().filter(i => i.id !== item.id)); },
      error: () => { this.busy.set(false); this.snackbar.warn(this.translate.instant('reconstruct.saveFailed')); },
    });
  }
}
