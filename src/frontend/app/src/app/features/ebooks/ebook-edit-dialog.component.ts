import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpEventType } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TextFieldModule } from '@angular/cdk/text-field';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorText } from '../../core/api-error';
import { SnackbarService } from '../../core/snackbar.service';
import { EBOOK_ACCEPT, EBOOK_DUPLICATE_BOOK, Ebook, EbookGroupOption, EbookMeta, EbookService, duplicateBooksOf, formatBytes } from './ebook.service';
import { EbookDuplicateDialogComponent, EbookDuplicateDialogData, EbookDuplicateDialogResult } from './ebook-duplicate-dialog.component';

export interface EbookEditDialogData {
  /** Vorhandenes Buch (bearbeiten) oder `null` (hochladen). */
  ebook: Ebook | null;
  groups: EbookGroupOption[];
  maxFileBytes: number;
  /** Ohne Anmeldung (0.778.0): keine Freigabe, stattdessen der Hinweis, dass das Buch beim Anmelden ins Konto wandert. */
  anonymous?: boolean;
}

/**
 * Ein Dialog für beides: „Hochladen" (Datei + Metadaten + Sichtbarkeit) und „Bearbeiten" (ohne Datei). Schließt mit
 * dem gespeicherten Buch; beim Hochladen zeigt der Balken den Fortschritt, weil ein Buch Minuten brauchen kann.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-edit-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatSelectModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatProgressBarModule, TextFieldModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ (isNew ? 'ebooks.dialog.uploadTitle' : 'ebooks.dialog.editTitle') | translate }}</h2>
    <mat-dialog-content>
      @if (isNew) {
        <div class="file-row">
          <input #fileInput type="file" [accept]="accept" hidden (change)="onFile($event)" />
          <button mat-stroked-button type="button" (click)="fileInput.click()" [disabled]="busy()">
            <mat-icon>attach_file</mat-icon>{{ 'ebooks.dialog.chooseFile' | translate }}
          </button>
          <span class="file-name">
            @if (file) { {{ file.name }} · {{ size(file.size) }} } @else { {{ 'ebooks.dialog.noFile' | translate }} }
          </span>
        </div>
        <p class="hint">{{ 'ebooks.dialog.fileHint' | translate: { max: size(data.maxFileBytes) } }}</p>
      }

      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'ebooks.meta.title' | translate }}</mat-label>
        <input matInput [(ngModel)]="title" maxlength="300" required />
      </mat-form-field>
      <div class="two">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'ebooks.meta.author' | translate }}</mat-label>
          <input matInput [(ngModel)]="author" maxlength="300" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="year">
          <mat-label>{{ 'ebooks.meta.year' | translate }}</mat-label>
          <input matInput type="number" inputmode="numeric" [(ngModel)]="year" min="1400" max="2200" />
        </mat-form-field>
      </div>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'ebooks.meta.tags' | translate }}</mat-label>
        <input matInput [(ngModel)]="tags" maxlength="600" [placeholder]="'ebooks.meta.tagsHint' | translate" />
      </mat-form-field>
      <!-- d3-rk-ebook-upload-hint-ueberlappt-feld: zweizeiliger Hinweis (Handy) wächst mit, statt auf dem nächsten Feld zu liegen. -->
      <mat-form-field appearance="outline" class="full board-mode" subscriptSizing="dynamic">
        <mat-label>{{ 'ebooks.meta.boardMode' | translate }}</mat-label>
        <mat-select [(ngModel)]="boardMode">
          @for (m of boardModes; track m) { <mat-option [value]="m">{{ ('ebooks.board.mode.' + m) | translate }}</mat-option> }
        </mat-select>
        <mat-hint>{{ 'ebooks.meta.boardModeHint' | translate }}</mat-hint>
      </mat-form-field>
      <mat-form-field appearance="outline" class="full">
        <mat-label>{{ 'ebooks.meta.description' | translate }}</mat-label>
        <textarea matInput [(ngModel)]="description" maxlength="2000" cdkTextareaAutosize cdkAutosizeMinRows="1" cdkAutosizeMaxRows="6"></textarea>
      </mat-form-field>

      @if (data.anonymous) {
        <p class="hint anon-hint"><mat-icon>person_outline</mat-icon>{{ 'ebooks.anonymous.uploadHint' | translate: { max: size(data.maxFileBytes) } }}</p>
      } @else {
      <div class="groups">
        <div class="groups-title">{{ 'ebooks.visibilityLabel' | translate }}</div>
        @if (data.groups.length === 0) {
          <p class="hint">{{ 'ebooks.visibility.noGroups' | translate }}</p>
        } @else {
          @for (g of data.groups; track g.id) {
            <mat-checkbox [checked]="selected.has(g.id)" (change)="toggle(g.id, $event.checked)">
              {{ g.isEveryone ? ('ebooks.visibility.everyone' | translate) : g.name }}
            </mat-checkbox>
          }
        }
        <p class="hint">{{ (selected.size === 0 ? 'ebooks.visibility.onlyMeHint' : 'ebooks.visibility.groupsHint') | translate }}</p>
      </div>
      }

      @if (progress() !== null) {
        <mat-progress-bar [mode]="progress()! >= 100 ? 'indeterminate' : 'determinate'" [value]="progress()!"></mat-progress-bar>
        <p class="hint">{{ 'ebooks.dialog.uploading' | translate: { pct: progress() } }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <!-- c1-eb-upload-disabled-no-reason: gesperrter Hauptknopf sagt, was fehlt (DESIGN 5.4). -->
      @if (blockReason && !busy()) { <span class="why" role="status">{{ blockReason | translate }}</span> }
      <button mat-button mat-dialog-close [disabled]="busy()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="save()" [disabled]="busy() || !canSave">
        <mat-icon>{{ isNew ? 'upload' : 'save' }}</mat-icon>{{ (isNew ? 'ebooks.upload' : 'common.save') | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    /* Scrollkante sichtbar (c1-eb-upload-disabled-no-reason): was unter der Linie liegt, ist nur weggescrollt. */
    mat-dialog-content { display: flex; flex-direction: column; gap: 4px; max-height: 70vh; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    /* Knopfleiste mit Grund (.why): global in styles.scss (DESIGN 5.6, d8-rk-dialog-aktionen-umbruch). */
    .full { width: 100%; }
    /* Dynamischer Hinweisbereich reserviert unten nichts — 12 px Luft bis zur schwebenden Beschriftung des nächsten Felds. */
    .board-mode { margin-bottom: 12px; }
    .two { display: grid; grid-template-columns: 1fr minmax(110px, 140px); gap: 8px; }
    .file-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 4px; }
    .file-name { flex: 1 1 160px; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: .9rem; }
    .hint { margin: 0 0 8px; font-size: .85rem; opacity: .7; }
    .groups { display: flex; flex-direction: column; margin-top: 4px; }
    .anon-hint { display: flex; align-items: flex-start; gap: 6px; mat-icon { flex: 0 0 auto; font-size: 18px; width: 18px; height: 18px; } }
    .groups-title { font-weight: 500; margin-bottom: 2px; }
    /* c2-eb-upload-jahr-drehpfeile: Safari zeichnet im Jahr dauerhaft winzige Drehpfeile (auf Touch nicht treffbar,
       für eine Jahreszahl sinnlos) — weg wie im Seitenfeld des Lesers (c1-eb-ebook-leser-seitenfeld-spinner). */
    .year input { appearance: textfield; -moz-appearance: textfield; }
    .year input::-webkit-inner-spin-button, .year input::-webkit-outer-spin-button { -webkit-appearance: none; appearance: none; margin: 0; }
    /* Handy: Autor + Jahr bleiben nebeneinander, damit die Sichtbarkeit näher an die Falz rückt. */
    @media (max-width: 480px) { .two { grid-template-columns: 1fr minmax(88px, 110px); } }
  `],
})
export class EbookEditDialogComponent {
  readonly data = inject<EbookEditDialogData>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef<EbookEditDialogComponent, Ebook | null>);
  private ebooks = inject(EbookService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private dialog = inject(MatDialog);
  private router = inject(Router);

  readonly accept = EBOOK_ACCEPT;
  readonly isNew = this.data.ebook == null;
  file: File | null = null;
  title = this.data.ebook?.title ?? '';
  author = this.data.ebook?.author ?? '';
  year: number | null = this.data.ebook?.year ?? null;
  tags = (this.data.ebook?.tags ?? []).join(', ');
  description = this.data.ebook?.description ?? '';
  selected = new Set<number>(this.data.ebook?.groupIds ?? []);
  /** Startmodus des Bretts (0.781.0), Standard „Lösen“. */
  boardMode: string = this.data.ebook?.defaultBoardMode ?? 'solve';
  /** „Baum“ nur für Bücher mit Eröffnungsbaum (0.881.0) — oder wenn das Buch schon darauf steht. */
  readonly boardModes = ['solve', 'view', 'free', 'calc', 'maia', 'game',
    ...(this.data.ebook?.hasTree || this.data.ebook?.defaultBoardMode === 'tree' ? ['tree'] : [])];
  readonly busy = signal(false);
  readonly progress = signal<number | null>(null);

  get canSave(): boolean {
    return this.title.trim().length > 0 && (!this.isNew || this.file != null);
  }

  /** Was zum Speichern fehlt (i18n-Schlüssel) — steht neben dem gesperrten Knopf; `null`, wenn nichts fehlt. */
  get blockReason(): string | null {
    if (this.isNew && this.file == null) return 'ebooks.dialog.needFile';
    if (this.title.trim().length === 0) return 'ebooks.dialog.needTitle';
    return null;
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (!file) return;
    if (file.size > this.data.maxFileBytes) {
      this.snackbar.warn(this.translate.instant('ebooks.dialog.fileTooLarge', { max: this.size(this.data.maxFileBytes) }));
      return;
    }
    this.file = file;
    if (!this.title.trim()) this.title = file.name.replace(/\.[^.]+$/, '').replace(/_/g, ' ').trim();
  }

  toggle(groupId: number, checked: boolean): void {
    if (checked) this.selected.add(groupId); else this.selected.delete(groupId);
  }

  size(bytes: number): string { return formatBytes(bytes, this.translate.currentLang() ?? undefined); }

  private meta(): EbookMeta {
    return {
      title: this.title.trim(),
      author: this.author.trim(),
      year: this.year ?? null,
      tags: this.tags.split(/[,;]/).map(t => t.trim()).filter(t => t.length > 0),
      description: this.description.trim(),
      defaultBoardMode: this.boardMode,
      groupIds: [...this.selected],
    };
  }

  save(): void {
    if (!this.canSave) return;
    this.busy.set(true);
    if (this.isNew && this.file) {
      this.upload(false);
    } else if (this.data.ebook) {
      // PUT: `year: null` heißt „keine Angabe" — das Backend sieht null als „unverändert", deshalb 0 schicken (→ wird null).
      const m = this.meta();
      this.ebooks.update(this.data.ebook.id, { ...m, year: m.year ?? 0 }).subscribe({
        next: saved => this.ref.close(saved),
        error: err => this.fail(err),
      });
    }
  }

  private upload(forceNew: boolean): void {
    if (!this.file) return;
    this.busy.set(true);
    this.progress.set(0);
    this.ebooks.upload(this.file, this.meta(), forceNew).subscribe({
      next: event => {
        if (event.type === HttpEventType.UploadProgress && event.total) this.progress.set(Math.round(100 * event.loaded / event.total));
        else if (event.type === HttpEventType.Response && event.body) this.uploaded(event.body);
      },
      error: err => this.uploadFailed(err),
    });
  }

  /** Hochgeladen — wahrscheinliche Dubletten (0.863.0) erst nachfragen: „Ja, das vorhandene nehmen“ löscht das neue wieder. */
  private uploaded(book: Ebook): void {
    const books = book.possibleDuplicates ?? [];
    if (books.length === 0) { this.ref.close(book); return; }
    this.askDuplicate({ mode: 'probable', books, uploadedTitle: book.title }, result => {
      if (result?.action !== 'open') { this.ref.close(book); return; }
      this.ebooks.delete(book.id).subscribe({
        next: () => this.goTo(result.id),
        // Löschen gescheitert: das neue Buch bleibt — sagen und trotzdem zum gewählten.
        error: err => { this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.deleteFailed')); this.goTo(result.id); },
      });
    });
  }

  /** Abgelehnt: dasselbe Buch ist schon da (ISBN/Text, 409 mit den Büchern) → zeigen statt nur eine Meldung. */
  private uploadFailed(err: unknown): void {
    const dup = duplicateBooksOf(err);
    if (!dup || dup.books.length === 0) { this.fail(err); return; }
    this.progress.set(null);
    this.askDuplicate({ mode: 'certain', books: dup.books, uploadedTitle: this.title.trim(), allowForce: dup.code === EBOOK_DUPLICATE_BOOK }, result => {
      if (result?.action === 'open') this.goTo(result.id);
      else if (result?.action === 'force') this.upload(true);
      else this.busy.set(false);
    });
  }

  private askDuplicate(data: EbookDuplicateDialogData, then: (result: EbookDuplicateDialogResult | undefined) => void): void {
    this.dialog.open<EbookDuplicateDialogComponent, EbookDuplicateDialogData, EbookDuplicateDialogResult>(EbookDuplicateDialogComponent,
      { data, width: '560px', maxWidth: '94vw', autoFocus: 'dialog' })
      .afterClosed().subscribe(then);
  }

  private goTo(id: number): void {
    this.ref.close(null);
    void this.router.navigate(['/ebooks', id]);
  }

  private fail(err: unknown): void {
    this.busy.set(false);
    this.progress.set(null);
    this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.saveFailed'));
  }
}
