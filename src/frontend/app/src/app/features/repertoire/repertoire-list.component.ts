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
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { CreateRepertoireDialogComponent } from './create-repertoire-dialog.component';
import { ShareRepertoireDialogComponent, ShareRepertoireDialogData } from './share-repertoire-dialog.component';
import { forkJoin, of, catchError } from 'rxjs';
import { Repertoire } from '../../core/models';
import { RepertoireService } from '../../core/repertoire.service';
import { RepertoireKind, REPERTOIRE_KIND_LABELS, toRepertoireKind } from '../../core/repertoire.types';
import { ReprocessBannerComponent } from '../../shared/reprocess-banner/reprocess-banner.component';
import { RepertoireTrainingService } from './repertoire-training.service';
import { saveRepertoireOffline, hasRepertoireOffline, removeRepertoireOffline, cachedRepertoires } from './repertoire-offline.util';
import { downloadBlob } from '../../shared/download.util';
import { repertoireDownloadPgn } from '../../shared/pgn-export.util';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-repertoire-list',
  standalone: true,
  imports: [IconLabelDirective, CommonModule, FormsModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatDialogModule, MatMenuModule, MatTooltipModule, TranslatePipe, LoadingSpinnerComponent, ReprocessBannerComponent],
  template: `
    <div class="repertoire-container">
      <!-- UI-Sweep 2026-10-11 (d2-rk-rep-list-lead): RepCheck-Tipp und Abschnittserklärung liegen hinter EINEM ⓘ
           (am Abschnitt „Für RepCheck und Vorbereitung", ohne diesen Abschnitt am Seitentitel) — über der Liste steht
           höchstens das Aufbereitungs-Banner. Am Handy kurze Knopfbeschriftung, damit die Titelzeile hält. -->
      <div class="header">
        <h1 class="title-with-info">{{ 'repertoire.list.title' | translate }}
          @if (!loading && extensionRepertoires.length === 0) { <ng-container [ngTemplateOutlet]="infoToggle" /> }
        </h1>
        <button mat-flat-button color="primary" (click)="openCreateDialog()" [attr.aria-label]="'repertoire.list.new' | translate">
          <mat-icon>add</mat-icon>
          <span class="new-long">{{ 'repertoire.list.new' | translate }}</span>
          <span class="new-short">{{ 'repertoire.list.newShort' | translate }}</span>
        </button>
      </div>
      @if (!loading && extensionRepertoires.length === 0 && infoOpen) { <ng-container [ngTemplateOutlet]="infoPanel" /> }

      <ng-template #infoToggle>
        <button type="button" class="info-toggle" (click)="infoOpen = !infoOpen" [attr.aria-expanded]="infoOpen"
                [attr.aria-label]="'repertoire.list.infoLabel' | translate">
          <mat-icon>info_outline</mat-icon>
        </button>
      </ng-template>
      <ng-template #infoPanel>
        <div class="info-panel">
          @if (extensionRepertoires.length > 0) { <p>{{ 'repertoire.list.sectionExtensionHint' | translate }}</p> }
          <p>{{ 'repertoire.list.extHint' | translate }}
            <a routerLink="/help" fragment="extension">{{ 'repertoire.list.extHintLink' | translate }}</a></p>
        </div>
      </ng-template>

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
              <ng-container [ngTemplateOutlet]="infoToggle" />
            </h2>
            @if (infoOpen) { <ng-container [ngTemplateOutlet]="infoPanel" /> }
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
          <p class="empty-state">{{ (search ? 'repertoire.list.noMatch' : 'repertoire.list.empty') | translate:{ query: search } }}</p>
        }
      }
    </div>

    <ng-template #repCard let-rep>
      <!-- UI-Sweep 2026-10-11 (r2-rg-rep-cards): Titel einzeilig mit Ellipse, die Art als kleine Marke in der
           Unterzeile (bei „keine" gar nicht) und die Aktionszeile unten bündig — sonst sprang „Öffnen" je Karte. -->
      <mat-card class="rep-card">
        <mat-card-header>
          <mat-card-title class="rep-title">
            <span class="rep-name" [title]="rep.name">{{ rep.name }}</span>
            @if (rep.useForExtension && !rep.isShared) {
              <mat-icon class="ext-badge"
                        [matTooltip]="'repertoire.list.extensionBadge' | translate">extension</mat-icon>
            }
            @if (rep.needsReimport) {
              <!-- Showstopper am REPERTOIRE statt als anonyme Zahl im Banner. -->
              <mat-icon class="stale-badge" [matTooltip]="'reprocess.itemNeedsReimport.repertoires' | translate"
                        [attr.aria-label]="'reprocess.itemNeedsReimport.repertoires' | translate">error_outline</mat-icon>
            }
          </mat-card-title>
          <mat-card-subtitle>
            @let kind = kindOf(rep);
            @if (kind !== Kind.None) {
              <span class="kind-chip" [class.kind-opening]="kind === Kind.Opening" [class.kind-middlegame]="kind === Kind.Middlegame" [class.kind-endgame]="kind === Kind.Endgame">{{ kindLabel(kind) | translate }}</span>
            }
            {{ (rep.fileCount === 1 ? 'repertoire.list.fileCountOne' : 'repertoire.list.fileCount') | translate: { count: rep.fileCount } }} · {{ (rep.isPublic ? 'repertoire.list.public' : 'repertoire.list.private') | translate }}
            @if (rep.isShared && rep.sharedByUsername) {
              · <mat-icon class="shared-badge-icon">group</mat-icon>{{ 'repertoire.share.sharedBy' | translate:{ name: rep.sharedByUsername } }}
            }
          </mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <p>{{ rep.description || ('repertoire.list.noDescription' | translate) }}</p>
        </mat-card-content>
        <mat-card-actions class="rep-actions">
          @if (offlineList) {
            <!-- Offline-Fallback: nur das Training funktioniert (aus dem Offline-Cache). -->
            <a mat-button color="primary" [routerLink]="['/repertoires', rep.id, 'train']">
              <mat-icon>fitness_center</mat-icon> {{ 'repertoireTrainer.train' | translate }}
            </a>
          } @else {
            <button mat-button [routerLink]="['/repertoires', rep.id]">{{ 'repertoire.list.open' | translate }}</button>
            <button mat-icon-button class="offline-toggle" [disabled]="savingOffline === rep.id"
                    (click)="toggleOffline(rep)"
                    [appIconLabel]="(isOffline(rep) ? 'repertoire.list.offlineRemoveTooltip' : 'repertoire.list.offlineSaveTooltip') | translate">
              <mat-icon>{{ isOffline(rep) ? 'cloud_done' : 'cloud_download' }}</mat-icon>
            </button>
            <button mat-icon-button [matMenuTriggerFor]="repMenu"
                    [appIconLabel]="'common.moreActions' | translate">
              <mat-icon>more_vert</mat-icon>
            </button>
            <mat-menu #repMenu="matMenu">
              <button mat-menu-item (click)="downloadPgn(rep)">
                <mat-icon>download</mat-icon>
                <span>{{ 'common.downloadPgn' | translate }}</span>
              </button>
              @if (!rep.isShared) {
                <button mat-menu-item [disabled]="converting === rep.id" (click)="convertToCourse(rep)">
                  <mat-icon>school</mat-icon>
                  <span>{{ 'repertoire.list.convertToCourse' | translate }}</span>
                </button>
                <button mat-menu-item (click)="openShareDialog(rep)">
                  <mat-icon>group_add</mat-icon>
                  <span>{{ 'repertoire.share.action' | translate }}</span>
                </button>
                <button mat-menu-item (click)="openEditDialog(rep)">
                  <mat-icon>edit</mat-icon>
                  <span>{{ 'common.edit' | translate }}</span>
                </button>
                <button mat-menu-item class="delete-item" (click)="deleteRepertoire(rep.id)">
                  <mat-icon>delete</mat-icon>
                  <span>{{ 'common.delete' | translate }}</span>
                </button>
              }
            </mat-menu>
          }
        </mat-card-actions>
      </mat-card>
    </ng-template>
  `,
  styles: [`
    .repertoire-container { padding: 16px; max-width: min(var(--page-max-width), 96vw); margin: 0 auto; }
    /* UI-Sweep u1 (u1-x-breite-stufe): Breitbild-Stufe ab 2400 px — Kartenraster breiter (DESIGN 4.1). */
    @media (min-width: 2400px) {
      .repertoire-container { max-width: min(var(--page-max-width-wide, 1880px), 96vw); }
    }
    .header { display: flex; justify-content: space-between; align-items: center; }
    .header { gap: 8px; margin-bottom: 0.75rem; }
    .header h1 { margin: 0; }
    .title-with-info { display: flex; align-items: center; gap: 4px; }
    .new-short { display: none; }
    @media (max-width: 599.98px) {
      .new-long { display: none; }
      .new-short { display: inline; }
      /* UI-Sweep z2 (z2-rk-rep-liste-vorlauf-admin): Titel einzeilig neben „Neu" (brach sonst zweizeilig um). */
      .header h1 { font-size: 1.6rem; line-height: 1.2; }
    }
    .info-toggle { display: inline-flex; align-items: center; justify-content: center; width: 28px; height: 28px;
      padding: 0; border: 0; border-radius: 50%; background: none; color: inherit; opacity: 0.6; cursor: pointer;
      font-size: 1rem; font-weight: 400; }
    .info-toggle:hover, .info-toggle:focus-visible, .info-toggle[aria-expanded="true"] { opacity: 1; }
    .info-toggle mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .info-panel { margin: 0 0 0.75rem; padding: 8px 12px; border-radius: 8px; font-size: 0.88rem; max-width: 75ch;
      background: color-mix(in srgb, currentColor 7%, transparent);
      color: color-mix(in srgb, currentColor 80%, transparent); }
    .info-panel p { margin: 0 0 6px; }
    .info-panel p:last-child { margin-bottom: 0; }
    .info-panel a { color: inherit; text-decoration: underline; cursor: pointer; }
    .offline-banner { display: flex; align-items: center; gap: 8px; margin: 0 0 1rem;
                      padding: 10px 12px; border-radius: 8px; font-size: 0.9rem;
                      background: color-mix(in srgb, currentColor 7%, transparent);
                      color: color-mix(in srgb, currentColor 80%, transparent); }
    .offline-banner mat-icon { flex: 0 0 auto; opacity: 0.7; }
    .offline-toggle { margin-left: auto; }
    /* Rotes Icon fuer den Loeschen-Eintrag im Aktionen-Menue (wie course-card). */
    .delete-item mat-icon { color: var(--rh-error); }
    .list-search { width: 100%; max-width: 360px; display: block; margin-bottom: 1rem; }
    .repertoire-section { margin-bottom: 1.75rem; }
    .repertoire-section .section-title { display: flex; align-items: center; gap: 6px; margin: 0.25rem 0 0.15rem; font-size: 1.05rem; font-weight: 600; }
    .repertoire-section .section-icon { color: var(--mat-sys-primary); }
    .repertoire-section .section-hint { margin: 0 0 0.65rem; color: color-mix(in srgb, currentColor 60%, transparent); font-size: 0.88rem; }
    .repertoire-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 1rem; }
    /* UI-Sweep 2026-10-11 (r2-rg-rep-cards): gleich hohe Karten, Aktionszeile unten bündig, Titel einzeilig. */
    .rep-card { height: 100%; }
    .rep-actions { margin-top: auto; }
    .rep-card mat-card-header { min-width: 0; }
    .rep-card ::ng-deep .mat-mdc-card-header-text { min-width: 0; flex: 1 1 auto; }
    .rep-title { display: flex; align-items: center; min-width: 0; }
    .rep-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; }
    .rep-title mat-icon { flex: 0 0 auto; }
    .kind-chip { display: inline-block; font-size: 0.72rem; line-height: 18px; padding: 0 8px; margin-right: 6px;
                 border-radius: 9px; vertical-align: middle; font-weight: 500;
                 background: color-mix(in srgb, currentColor 14%, transparent); }
    .kind-chip.kind-opening { background-color: #1976d2; color: #fff; }
    .kind-chip.kind-middlegame { background-color: #7b1fa2; color: #fff; }
    .kind-chip.kind-endgame { background-color: #c62828; color: #fff; }
    /* Kleiner „RepCheck ok"-Marker im Karten-Titel, in Primärfarbe. */
    .ext-badge { color: var(--mat-sys-primary); font-size: 18px; width: 18px; height: 18px; margin-left: 8px; vertical-align: middle; opacity: 0.9; }
    .shared-badge-icon { font-size: 14px; width: 14px; height: 14px; vertical-align: middle; opacity: 0.7; }
    /* (!) am Eintrag: klein und warnfarben, aber nicht schreiend — es ist ein Hinweis, kein Fehler. */
    .stale-badge { color: var(--rh-warn); font-size: 18px; width: 18px; height: 18px; margin-left: 8px; vertical-align: middle; cursor: help; }
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
  /** RepCheck-Tipp + Abschnittserklärung aufgeklappt (ⓘ)? */
  infoOpen = false;

  constructor(private repertoireService: RepertoireService, private training: RepertoireTrainingService, private dialog: MatDialog, private snackbar: SnackbarService, private translate: TranslateService, private confirm: ConfirmService) {}

  /** UI-Sweep 2026-10-11 (r2-rg-rep-cards): Art als Enum — die API schickt den NAMEN („None", „Opening", …). */
  kindOf(rep: Repertoire): RepertoireKind {
    return toRepertoireKind(rep.kind);
  }

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
    this.confirm.ask('repertoire.list.deleteConfirm').subscribe(ok => {
      if (!ok) return;
      this.repertoireService.remove(id).subscribe({
        next: () => this.loadRepertoires(),
        error: () => this.snackbar.info(this.translate.instant('repertoire.list.deleteFailed'))
      });
    });
  }

  /** Repertoire in einen persönlichen Kurs umwandeln (nur bei Puzzle-PGN im Chessable-Stil). Der Server
   *  verschiebt: das Repertoire wird danach samt Trainingsstand und Freigaben ALLER Nutzer gelöscht —
   *  deshalb vorher die Rückfrage wie beim Löschen. */
  convertToCourse(rep: Repertoire): void {
    this.confirm.ask('repertoire.list.convertMoveConfirm', { name: rep.name }).subscribe(ok => {
      if (!ok) return;
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
          // 400 hat zwei Ursachen, die der Nutzer klar unterscheiden können muss:
          // code 'repertoire_empty' = noch gar kein PGN drin (dann fehlt der Import, nicht der Puzzle-Marker),
          // sonst = PGN vorhanden, aber kein quiz-barer Inhalt (reines Eröffnungs-PGN).
          const key = e?.status === 400
            ? (e?.error?.code === 'repertoire_empty'
              ? 'repertoire.list.convertToCourseEmpty'
              : 'repertoire.list.convertToCourseNoPuzzles')
            : 'repertoire.list.convertToCourseFailed';
          this.snackbar.info(this.translate.instant(key), { action: 'common.ok', duration: 4000 });
        }
      });
    });
  }

  downloadPgn(rep: Repertoire): void {
    // Derselbe Endpunkt beliefert Viewer und Trainer (die brauchen [%alt]) — die internen Marker werden
    // deshalb erst hier, beim Speichern, entfernt.
    this.repertoireService.getPgnText(rep.id).subscribe({
      next: pgn => {
        const safe = (rep.name || 'repertoire').replace(/[^A-Za-z0-9]+/g, '_');
        downloadBlob(new Blob([repertoireDownloadPgn(pgn)], { type: 'application/x-chess-pgn' }), `${safe}.pgn`);
      },
      error: () => this.snackbar.info(this.translate.instant('common.downloadFailed'))
    });
  }
}
