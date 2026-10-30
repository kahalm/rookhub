import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { map, switchMap } from 'rxjs';
import { apiErrorText } from '../../core/api-error';
import { RepertoireService } from '../../core/repertoire.service';
import { SnackbarService } from '../../core/snackbar.service';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { SanPipe } from '../../shared/chess/san.pipe';
import { EbookOpeningTree, EbookTreeMove, TREE_EXPORT_MAX_NODES, treeToPgn } from './ebook-tree.util';
import {
  EbookTreeSaveDialogComponent, EbookTreeSaveDialogData, EbookTreeSaveDialogResult,
} from './ebook-tree-save-dialog.component';

/**
 * Brett-Modus „Baum“ unter dem Brett (0.881.0): Schrittleiste (Grundstellung, ◀, ▶ = Hauptfortsetzung) mit „Als
 * Repertoire speichern“, der Weg von der Grundstellung als klickbare Zugfolge und die Fortsetzungen im Buch — meistverzweigte
 * zuerst, je mit der Zahl der Buchlinien darunter. Der Zustand (Knoten) gehört dem Brett; hier wird nur angezeigt und
 * gemeldet, wohin gesprungen werden soll.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-ebook-tree-panel',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslatePipe, IconLabelDirective, SanPipe],
  template: `
    @if (notInTree()) { <p class="etp-note etp-not-in-tree">{{ 'ebooks.tree.notInTree' | translate }}</p> }
    <div class="etp-nav" role="group" [attr.aria-label]="'ebooks.board.mode.tree' | translate">
      <button mat-icon-button type="button" (click)="go.emit(0)" [disabled]="cursor() === 0" [appIconLabel]="'analysis.setup.startpos' | translate"><mat-icon>first_page</mat-icon></button>
      <button mat-icon-button type="button" class="etp-back" (click)="back()" [disabled]="cursor() === 0" [appIconLabel]="'common.previous' | translate"><mat-icon>chevron_left</mat-icon></button>
      <button mat-icon-button type="button" class="etp-forward" (click)="forward()" [disabled]="children.length === 0" [appIconLabel]="'common.next' | translate"><mat-icon>chevron_right</mat-icon></button>
      @if (canSave()) {
        <button mat-button type="button" class="etp-save" (click)="save()" [disabled]="saving()"><mat-icon>bookmark_add</mat-icon>{{ 'ebooks.tree.save' | translate }}</button>
      }
    </div>
    <div class="etp-path" role="list" [attr.aria-label]="'ebooks.tree.path' | translate">
      @if (path.length === 0) {
        <span class="etp-start">{{ 'analysis.setup.startpos' | translate }}</span>
      }
      @for (m of path; track m.node; let i = $index) {
        <button type="button" role="listitem" class="etp-step" [class.etp-step--on]="m.node === cursor()"
                [attr.aria-current]="m.node === cursor() ? 'step' : null" (click)="go.emit(m.node)">@if (i === 0 || m.label.endsWith('.')) {<span class="etp-no">{{ m.label }}</span>}{{ m.san | san }}</button>
      }
    </div>
    <div class="etp-next">
      <div class="etp-head">{{ 'ebooks.tree.next' | translate }}</div>
      @if (children.length === 0) {
        <p class="etp-note">{{ 'ebooks.tree.end' | translate }}</p>
      } @else {
        <div role="list">
          @for (m of children; track m.node) {
            <button type="button" role="listitem" class="etp-child" (click)="go.emit(m.node)">
              <span class="etp-child-move"><span class="etp-no">{{ m.label }}</span>{{ m.san | san }}</span>
              <span class="etp-lines">{{ (m.lines === 1 ? 'ebooks.tree.linesOne' : 'ebooks.tree.lines') | translate: { n: m.lines } }}</span>
            </button>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: flex; flex-direction: column; gap: 6px; }
    .etp-nav { display: flex; align-items: center; gap: 2px; flex-wrap: wrap; }
    .etp-save { margin-left: auto; }
    .etp-path { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0 2px; border-top: 1px solid var(--mat-sys-outline-variant); padding-top: 4px; }
    .etp-note { margin: 0; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .etp-step {
      font: inherit; font-size: .9rem; color: inherit; background: none; border: 0; border-radius: 4px; padding: 2px 4px; cursor: pointer;
      &:hover { background: color-mix(in srgb, var(--mat-sys-on-surface) 8%, transparent); }
      &:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 1px; }
    }
    .etp-step--on { color: var(--mat-sys-primary); background: color-mix(in srgb, var(--mat-sys-primary) 16%, transparent); font-weight: 600; }
    .etp-start { font-size: .9rem; color: var(--mat-sys-on-surface-variant); padding: 2px 4px; }
    .etp-no { color: var(--mat-sys-on-surface-variant); margin-right: 2px; font-weight: 400; }
    .etp-head { font-size: .85rem; color: var(--mat-sys-on-surface-variant); margin-bottom: 2px; }
    .etp-child {
      display: flex; align-items: baseline; gap: 8px; width: 100%; min-height: 36px; padding: 4px 8px; border: 0; border-radius: 4px;
      font: inherit; font-size: .9rem; color: inherit; background: none; cursor: pointer; text-align: left;
      &:hover { background: color-mix(in srgb, var(--mat-sys-on-surface) 8%, transparent); }
      &:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 1px; }
    }
    .etp-child-move { font-weight: 600; }
    .etp-lines { margin-left: auto; font-size: .8rem; color: var(--mat-sys-on-surface-variant); white-space: nowrap; }
    @media (pointer: coarse) { .etp-child { min-height: 44px; } }
  `],
})
export class EbookTreePanelComponent {
  readonly tree = input.required<EbookOpeningTree>();
  readonly cursor = input(0);
  readonly notInTree = input(false);
  readonly bookTitle = input('');
  /** „Als Repertoire speichern“ nur mit Konto. */
  readonly canSave = input(false);
  readonly go = output<number>();

  private readonly dialog = inject(MatDialog);
  private readonly repertoires = inject(RepertoireService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  readonly saving = signal(false);

  get path(): EbookTreeMove[] { return this.tree().path(this.cursor()).map(n => this.tree().move(n)); }
  get children(): EbookTreeMove[] { return this.tree().children[this.cursor()].map(n => this.tree().move(n)); }

  back(): void { if (this.cursor() > 0) this.go.emit(Math.max(0, this.tree().parent[this.cursor()])); }
  forward(): void { const c = this.tree().children[this.cursor()]; if (c.length) this.go.emit(c[0]); }

  /** Vorschlag „<Buch> – <letzte Züge>“ (höchstens drei Halbzüge, nummeriert). */
  defaultName(): string {
    const path = this.tree().path(this.cursor()).slice(-3);
    if (path.length === 0) return this.bookTitle().slice(0, 200);
    const moves = path.map((n, i) => {
      const label = this.tree().moveNumber(n);
      return i === 0 || label.endsWith('.') ? `${label.replace('…', '...')} ${this.tree().san[n]}` : this.tree().san[n];
    }).join(' ');
    return `${this.bookTitle()} – ${moves}`.slice(0, 200);
  }

  save(): void {
    if (this.saving()) return;
    const here = this.cursor();
    const data: EbookTreeSaveDialogData = {
      name: this.defaultName(),
      canHere: here > 0,
      colors: { all: this.tree().majorityLastMover(0), here: this.tree().majorityLastMover(here) },
      counts: { all: this.tree().size, here: this.tree().subtreeSize(here) + this.tree().depth[here] },
    };
    this.dialog.open<EbookTreeSaveDialogComponent, EbookTreeSaveDialogData, EbookTreeSaveDialogResult>(
      EbookTreeSaveDialogComponent, { data, width: '440px', maxWidth: '94vw', autoFocus: 'dialog' },
    ).afterClosed().subscribe(r => { if (r) this.create(r, r.scope === 'here' ? here : 0); });
  }

  /** Repertoire anlegen und das PGN des gewählten Teilbaums hochladen (derselbe Weg wie „Neues Repertoire aus Auswahl“). */
  private create(r: EbookTreeSaveDialogResult, from: number): void {
    const n = from === 0 ? this.tree().size : this.tree().subtreeSize(from) + this.tree().depth[from];
    if (n > TREE_EXPORT_MAX_NODES) {
      this.snackbar.warn(this.translate.instant('ebooks.tree.tooLarge', { n, max: TREE_EXPORT_MAX_NODES }));
      return;
    }
    const pgn = treeToPgn(this.tree(), from, { bookTitle: this.bookTitle(), name: r.name, color: r.color });
    const description = this.translate.instant('ebooks.tree.description', { book: this.bookTitle() });
    this.saving.set(true);
    this.repertoires.create({
      name: r.name, description: String(description).slice(0, 1000), isPublic: false, kind: 1, useForExtension: true,
    }).pipe(
      switchMap(created => {
        const id = (created as { id: number }).id;
        const form = new FormData();
        form.append('file', new File([pgn], 'buchbaum.pgn', { type: 'application/x-chess-pgn' }));
        return this.repertoires.uploadFile(id, form).pipe(map(() => id));
      }),
    ).subscribe({
      next: id => {
        this.saving.set(false);
        this.snackbar.show(this.translate.instant('ebooks.tree.saved', { name: r.name }), { action: 'repertoire.list.open', duration: 8000 })
          .onAction().subscribe(() => void this.router.navigate(['/repertoires', id]));
      },
      error: err => {
        this.saving.set(false);
        this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.tree.saveFailed'));
      },
    });
  }
}
