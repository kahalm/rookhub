import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { SnackbarService } from '../../core/snackbar.service';
import { apiErrorText } from '../../core/api-error';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { of } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CourseService, CourseListItem, CourseChapter } from './course.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { ReprocessBannerComponent } from '../../shared/reprocess-banner/reprocess-banner.component';
import { CourseLanguageService } from './course-language.service';
import { saveBookOffline, removeBookOffline, cachedBookFileNames, saveCourseListCache, loadCourseListCache } from '../puzzles/book-offline.util';
import { downloadBlob } from '../../shared/download.util';
import { CreateCourseDialogComponent, CreateCourseDialogData, CreateCourseDialogResult } from './create-course-dialog.component';
import { ShareCourseDialogComponent, ShareCourseDialogData } from './share-course-dialog.component';
import { LinkCourseDialogComponent, LinkCourseDialogData } from './link-course-dialog.component';
import { CourseThemesDialogComponent, CourseThemesDialogData } from './course-themes-dialog.component';
import { AuthService } from '../../core/auth.service';
import { CourseCardComponent } from './course-card.component';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';

/** Ein Abschnitt der Kursliste („In Arbeit", „Mit mir geteilt", „Öffentlich", „Eigene"). */
export interface CourseSection {
  titleKey: string;
  hintKey: string;
  items: CourseListItem[];
}

@Component({
  selector: 'app-course-list',
  changeDetection: ChangeDetectionStrategy.Default,
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatDialogModule,
    LoadingSpinnerComponent, TranslatePipe, ReprocessBannerComponent, CourseCardComponent, HelpHintComponent,
    IconLabelDirective, LoadErrorComponent
  ],
  template: `
    <div class="courses-container">
      <div class="header">
        <!-- UI-Sweep z1 (z1-rk-courses-banner-vorlauf): am Handy steht der Erklärsatz hinter dem ⓘ am Titel und die
             zwei Kopfknöpfe als Symbolknöpfe in der Titelzeile (Beschriftung als aria-label) — weniger Vorlauf. -->
        <div class="title-row">
          <h1>{{ 'courses.title' | translate }}</h1>
          <app-help-hint class="intro-hint" icon="info_outline" [text]="'courses.intro' | translate" />
        </div>
        <!-- UI-Sweep z2 (z2-rk-courses-vorlauf): ab 600 px steht das Suchfeld in der Kopfzeile links neben den Knöpfen
             (am Handy eigene Zeile darunter); der Erklärsatz liegt in jeder Breite hinter dem ⓘ am Titel. -->
        @if (!loading && courses.length > 0) {
          <mat-form-field appearance="outline" class="list-search" subscriptSizing="dynamic">
            <mat-icon matPrefix>search</mat-icon>
            <input matInput [(ngModel)]="search" [placeholder]="'courses.searchPlaceholder' | translate"
                   [attr.aria-label]="'common.search' | translate">
            @if (search) {
              <button matSuffix mat-icon-button (click)="search = ''" [attr.aria-label]="'common.clear' | translate">
                <mat-icon>close</mat-icon>
              </button>
            }
          </mat-form-field>
        }
        <div class="header-actions">
          <!-- UI-Sweep z2 (z2-rk-courses-kopf-symbole): Import am Handy als Symbolknopf mit Tooltip (appIconLabel);
               der Hauptknopf behält zwischen 381 und 599 px den Kurztext „Neu", erst ≤ 380 px nur „+" (DESIGN 5.4). -->
          <button mat-stroked-button [disabled]="creating" (click)="openImportDialog()"
                  [appIconLabel]="'courses.import.button' | translate">
            <mat-icon>upload_file</mat-icon>
            <span class="btn-label">{{ 'courses.import.button' | translate }}</span>
          </button>
          <button mat-flat-button color="primary" [disabled]="creating" (click)="openCreateDialog()"
                  [attr.aria-label]="(creating ? 'courses.create.creating' : 'courses.create.button') | translate">
            <mat-icon>{{ creating ? 'hourglass_empty' : 'add' }}</mat-icon>
            <span class="btn-label">{{ (creating ? 'courses.create.creating' : 'courses.create.button') | translate }}</span>
            @if (!creating) { <span class="btn-label-short">{{ 'courses.create.short' | translate }}</span> }
          </button>
        </div>
      </div>

      <!-- UI-Sweep 2026-10-11 (d2-rk-courses-zwei-banner): EIN Hinweis über der Liste — die „Neu holen"-Zahl ist der
           zweite Satz im Aufbereitungs-Banner; fehlt das Banner, steht sie allein als Legende. -->
      <app-reprocess-banner #rb section="courses" (done)="loadCourses()">
        @if (reimportCount > 0) { <ng-container [ngTemplateOutlet]="reimportTpl" /> }
      </app-reprocess-banner>
      @if (reimportCount > 0 && !rb.visible) {
        <div class="reimport-legend"><ng-container [ngTemplateOutlet]="reimportTpl" /></div>
      }
      <ng-template #reimportTpl>
        <!-- UI-Sweep z1 (z1-rk-courses-banner-vorlauf): im Banner eine eigene Zeile, sonst klebte der zweite Hinweis am ersten Satz. -->
        <span class="reimport-part">
        <span class="reimport-chip">{{ 'courses.reimport.chip' | translate }}</span>
        <!-- UI-Sweep z3 (z3-rk-courses-vorlauf): am Handy im Banner nur „Neu holen 13 ⓘ" — der Satz samt Hinweis liegt im ⓘ. -->
        <span class="reimport-count">{{ reimportCount }}</span>
        <app-help-hint class="reimport-info" icon="info_outline"
          [text]="((reimportCount === 1 ? 'courses.reimport.legendOne' : 'courses.reimport.legend') | translate:{ count: reimportCount }) + '\\n\\n' + ('reprocess.itemNeedsReimport.courses' | translate)" />
        <!-- UI-Sweep 2026-10-11 (Nachprüfung Runde 3): „Mehr" als Textknopf am Satzende statt als eigener Flex-Eintrag — am Handy brach er sonst
             allein in eine Zeile. -->
        <span class="reimport-text">{{ (reimportCount === 1 ? 'courses.reimport.legendOne' : 'courses.reimport.legend') | translate:{ count: reimportCount } }}
          <button type="button" class="reimport-toggle" (click)="reimportOpen = !reimportOpen" [attr.aria-expanded]="reimportOpen">{{ (reimportOpen ? 'common.showLess' : 'common.showMore') | translate }}</button></span>
        @if (reimportOpen) {
          <span class="reimport-hint">{{ 'reprocess.itemNeedsReimport.courses' | translate }}</span>
        }
        </span>
      </ng-template>

      @if (offlineList) {
        <div class="offline-banner">
          <mat-icon>cloud_off</mat-icon>
          <span>{{ 'courses.offlineListHint' | translate }}</span>
        </div>
      }

      @if (loading) {
        <app-loading-spinner />
      } @else if (loadError) {
        <!-- d3-rk-ladefehler-endlos-spinner: gescheitertes Laden ohne Offline-Vorrat ist nicht „keine Kurse". -->
        <app-load-error messageKey="courses.loadFailed" (retry)="loadCourses()" />
      } @else if (courses.length === 0) {
        <p class="empty-state">{{ 'courses.emptyHint' | translate }}</p>
      } @else {
        @if (filtered.length === 0) {
          <p class="empty-state">{{ 'courses.noMatch' | translate:{ query: search } }}</p>
        }
        @for (s of sections; track s.titleKey) {
          @if (s.items.length > 0) {
            <section class="course-section">
              <div class="section-head">
                <h2>{{ s.titleKey | translate }}</h2>
                <app-help-hint icon="info_outline" [text]="s.hintKey | translate" />
              </div>
              <div class="course-grid">
                @for (c of s.items; track c.bookId) {
                  <app-course-card [course]="c" [origin]="originOf(c)"
                    [pinning]="pinning === c.bookId"
                    [savingOffline]="savingOffline === c.bookId"
                    [downloadingPgn]="downloadingPgn === c.bookId"
                    [converting]="converting === c.bookId"
                    [deleting]="deleting === c.bookId"
                    [offline]="isOffline(c)"
                    [canManageThemes]="canManageThemes(c)"
                    [expanded]="expandedBook === c.bookId"
                    [loadingChapters]="loadingChapters === c.bookId"
                    [chapters]="chaptersByBook[c.bookId]"
                    (pinToggle)="togglePin(c)"
                    (offlineToggle)="toggleOffline(c)"
                    (pgnDownload)="downloadPgn(c)"
                    (progressReset)="reset(c)"
                    (convertRepertoire)="convertToRepertoire(c)"
                    (linkEdit)="openLinkDialog(c)"
                    (themesEdit)="openThemesDialog(c)"
                    (shareCourse)="openShareDialog(c)"
                    (deleteCourse)="deleteCourse(c)"
                    (chaptersToggle)="toggleChapters(c)"></app-course-card>
                }
              </div>
            </section>
          }
        }
      }
    </div>

  `,
  styles: [`
    .header-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .courses-container { max-width: min(var(--page-max-width), 96vw); margin: 24px auto; padding: 0 16px; }
    .header { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
    .header h1 { margin: 0; }
    /* UI-Sweep 2026-10-11 (Nachprüfung Runde 3): am Handy standen Titel und zwei Knöpfe gedrängt nebeneinander (Knöpfe übereinander) — die Knöpfe
       rücken in eine eigene Zeile unter den Titel. */
    /* UI-Sweep z1 (z1-rk-courses-banner-vorlauf): am Handy bleiben die Knöpfe als Symbolknöpfe in der Titelzeile,
       der Erklärsatz steht hinter dem ⓘ am Titel (Vorlauf ≤ 40 %, DESIGN 4.6). */
    .title-row { display: flex; align-items: center; gap: 4px; min-width: 0; }
    /* UI-Sweep z2 (z2-rk-courses-vorlauf): Kopfzeile = Titel ⓘ | Suche | Knöpfe; darunter direkt Banner bzw. Liste. */
    .header { flex-wrap: wrap; margin-bottom: 16px; }
    .list-search { flex: 0 1 320px; margin: 0 0 0 auto; min-width: 200px; }
    .btn-label-short { display: none; }
    @media (max-width: 599.98px) {
      .header-actions { flex-wrap: nowrap; }
      .header-actions .btn-label { display: none; }
      .header-actions .btn-label-short { display: inline; }
      .header-actions button { min-width: 0; padding: 0 12px; }
      .header-actions button mat-icon { margin: 0; }
      .header-actions .btn-label-short { margin-left: 4px; }
      .list-search { order: 3; flex: 1 1 100%; margin: 0; }
    }
    @media (max-width: 380px) {
      .header-actions .btn-label-short { display: none; }
    }
    /* a1-rk-kurse-tablet-kopf (DESIGN 5.1): zwischen 600 und 1023 px passten Titel, Suche und beide Knöpfe nicht in
       eine Zeile — die Knöpfe rutschten linksbündig in eine zweite. Jetzt Titel links, Knöpfe rechts in Zeile 1,
       die Suche in voller Breite darunter (wie am Handy). */
    @media (min-width: 600px) and (max-width: 1023.98px) {
      .list-search { order: 3; flex: 1 1 100%; margin: 0; }
      .header-actions { margin-left: auto; }
    }
    /* Leerzustand nach docs/DESIGN.md 5.10: globale .empty-state (zentriert, Polster) + leise Schrift. */
    .empty-state { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; }

    .course-section { margin-bottom: 28px; }
    .reimport-legend { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 10px; margin: 0 0 1rem;
      padding: 6px 12px; border-radius: 8px; font-size: 0.88rem;
      border: 1px solid color-mix(in srgb, var(--rh-warn) 45%, transparent); }
    /* Im Aufbereitungs-Banner steht die Legende als zweiter Satz im Fließtext. */
    app-reprocess-banner .reimport-part { display: block; margin-top: 4px; }
    .reimport-part { display: contents; }
    .reimport-count, .reimport-info { display: none; }
    /* UI-Sweep z3 (z3-rk-courses-vorlauf): am Handy im Banner nur Chip + Zahl + ⓘ, in einer Zeile mit dem Knopf. */
    @media (max-width: 599.98px) {
      app-reprocess-banner .reimport-part { display: flex; align-items: center; gap: 4px; flex: 1 1 auto;
        min-width: 0; margin: 0 0 0 32px; }
      app-reprocess-banner .reimport-chip { margin-right: 2px; }
      app-reprocess-banner .reimport-count { display: inline; font-weight: 500; }
      app-reprocess-banner .reimport-info { display: inline-flex; }
      app-reprocess-banner .reimport-text, app-reprocess-banner .reimport-hint { display: none; }
    }
    .reimport-chip { margin-right: 6px; }
    .reimport-chip { border: 1px solid var(--rh-warn); color: var(--rh-warn); border-radius: 10px; padding: 0 7px; font-size: 0.72rem; white-space: nowrap; }
    .reimport-text { flex: 1 1 220px; }
    .reimport-toggle { font: inherit; font-weight: 500; color: var(--mat-sys-primary); background: none; border: 0;
      padding: 6px 4px; margin: -6px 0; cursor: pointer; white-space: nowrap; text-decoration: underline;
      text-underline-offset: 2px; }
    .reimport-toggle:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 1px; border-radius: 4px; }
    .reimport-hint { display: block; flex-basis: 100%; margin: 4px 0; color: color-mix(in srgb, currentColor 70%, transparent); }
    .offline-banner { display: flex; align-items: center; gap: 8px; margin: 0 0 1rem;
                      padding: 10px 12px; border-radius: 8px; font-size: 0.9rem;
                      background: color-mix(in srgb, currentColor 7%, transparent);
                      color: color-mix(in srgb, currentColor 80%, transparent); }
    .offline-banner mat-icon { flex: 0 0 auto; opacity: 0.7; }
    /* UI-Sweep z1: die Unterzeile je Abschnitt steht im ⓘ des Abschnittskopfs. */
    .section-head { display: flex; align-items: center; gap: 4px; margin: 0 0 10px; }
    .course-section h2 { font-size: 1.05rem; font-weight: 600; margin: 0; letter-spacing: .01em; }
    .course-grid {
      display: grid; gap: 12px;
      grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
    }
    /* UI-Sweep u1 (u1-x-breite-stufe): Breitbild-Stufe ab 2400 px — Kartenraster mit 6 Spalten (DESIGN 4.1). */
    /* Niedrige Fenster (Boox 200 % = 480 × 640, DESIGN 4.6 „Niedrige Fenster“): der erste Kurs begann bei 47 % der Höhe —
       oben 8 statt 24 px, Kopf, Import-Hinweis und Abschnittskopf mit 4–8 px statt 10–16 px Abstand. */
    @media (max-width: 768px) and (max-height: 720px) {
      .courses-container { margin-top: 8px; }
      .header { margin-bottom: 8px; }
      .reimport-legend { margin-bottom: 0.5rem; }
      .section-head { margin-bottom: 6px; }
    }
    @media (min-width: 2400px) {
      .courses-container { max-width: min(var(--page-max-width-wide, 1880px), 96vw); }
      .course-grid { grid-template-columns: repeat(auto-fill, minmax(290px, 1fr)); }
    }
  `]
})
export class CourseListComponent implements OnInit {
  courses: CourseListItem[] = [];

  /** Freitext-Suche (filtert clientseitig nach Kurstitel). Setter statt Feld, damit die
   *  vorberechneten Sektionen (siehe {@link rebuildSections}) beim Tippen mitkommen. */
  get search(): string { return this._search; }
  set search(value: string) {
    this._search = value;
    this.rebuildSections();
  }
  private _search = '';
  loading = false;
  /** Laden gescheitert und kein Offline-Vorrat — statt des Leerhinweises steht dann app-load-error. */
  loadError = false;
  savingOffline: number | null = null;
  downloadingPgn: number | null = null;
  /** Läuft gerade ein Upload eines eigenen Kurs-PGN? */
  creating = false;
  /** bookId, der gerade gelöscht wird (Button-Sperre). */
  deleting: number | null = null;
  /** bookId, dessen Pin gerade umgeschaltet wird (Button-Sperre). */
  pinning: number | null = null;
  /** bookId, der gerade in ein Repertoire umgewandelt wird (Button-Sperre). */
  converting: number | null = null;
  private offlineFiles = new Set<string>();
  /** true = Server nicht erreichbar; Anzeige aus dem Offline-Cache (nur heruntergeladene Kurse). */
  offlineList = false;

  /** Aufgeklapptes Buch (Kapitelübersicht) bzw. null. */
  expandedBook: number | null = null;
  /** Lazy geladene Kapitel je Buch (bookId → Kapitel). */
  chaptersByBook: Record<number, CourseChapter[]> = {};
  /** Buch, dessen Kapitel gerade geladen werden. */
  loadingChapters: number | null = null;

  constructor(private courseService: CourseService, private snackbar: SnackbarService, private translate: TranslateService, private dialog: MatDialog, private auth: AuthService, private router: Router,
              private courseLang: CourseLanguageService, private confirm: ConfirmService) {}

  /** Darf der aktuelle Nutzer die Themen-Tags dieses Kurses setzen? Admin (alle) oder Besitzer. */
  canManageThemes(course: CourseListItem): boolean {
    return this.auth.isAdmin || course.isOwned;
  }

  /** Öffnet den Themen-Multi-Select; speichert nach Bestätigung buch-global und aktualisiert die Karte. */
  openThemesDialog(course: CourseListItem): void {
    const ref = this.dialog.open<CourseThemesDialogComponent, CourseThemesDialogData, string[] | undefined>(
      CourseThemesDialogComponent, {
        width: '360px', maxWidth: '95vw',
        data: { bookId: course.bookId, displayName: course.displayName, themes: course.themes ?? [] },
      });
    ref.afterClosed().subscribe(themes => {
      if (!themes) return; // abgebrochen
      this.courseService.setCourseThemes(course.bookId, themes).subscribe({
        next: res => {
          course.themes = res.themes; // effektive Keys (Default „tactics" wenn leer)
          this.snackbar.info(this.translate.instant('courses.themes.saved', { name: course.displayName }), { action: 'common.ok', duration: 2000 });
        },
        error: () => this.snackbar.warn(this.translate.instant('courses.themes.error')),
      });
    });
  }

  /** Angefangene, noch nicht abgeschlossene Kurse — „In Arbeit". Erscheinen ZUSÄTZLICH oben,
   *  bleiben aber auch in ihrer normalen Sektion (öffentlich/eigene). Reihenfolge = zuletzt
   *  verwendet zuerst (durch sortCourses bereits vorsortiert). */
  // Vorberechnete Sektionen statt Getter-Kaskade: die fünf Getter liefen bei JEDEM
  // Change-Detection-Zyklus (Default-Strategie: jedes Event der Seite), und jeder davon
  // filterte die ganze Liste erneut — 8+ Array-Durchläufe pro Zyklus, obwohl sich die
  // Eingaben (Liste, Suche, Pin/Fortschritt) nur an klar benannten Stellen ändern.
  // Dort ruft jetzt jeder Schreiber {@link rebuildSections}.
  /** Kurse nach Suchtext gefiltert (Titel, case-insensitive); Basis für alle Sektionen. */
  filtered: CourseListItem[] = [];
  inProgressCourses: CourseListItem[] = [];
  /** Kurse, die andere Nutzer mit mir geteilt haben (eigene Sektion „Mit mir geteilt"). */
  sharedCourses: CourseListItem[] = [];
  /** Öffentliche Kurse — über eine Gruppe freigegeben (bzw. globale Admin-Bücher). */
  publicCourses: CourseListItem[] = [];
  /** Eigene Kurse (`isOwned`): aus Chessable importiert, aber genauso über „Kurs erstellen" leer
   *  angelegt, per „Partien importieren" oder aus einem Repertoire umgewandelt — deshalb heißt die
   *  Sektion „Eigene Kurse" und nicht „Chessable-Kurse". */
  ownCourses: CourseListItem[] = [];
  /** Die vier Abschnitte in Anzeige-Reihenfolge (Überschrift, ⓘ-Text, Karten) — EIN Kartenblock im Template
   *  statt vier gleicher (Codereview F3-023); leere Abschnitte blendet das Template aus. */
  sections: CourseSection[] = [];

  /** Sektionen aus Liste + Suche neu ableiten. MUSS nach jeder Änderung an `courses`
   *  (Zuweisung ODER Mutation von isPinned/solvedCount/puzzleCount/isShared/isOwned) laufen —
   *  ein vergessener Aufruf zeigt veraltete Sektionen, bis der nächste Schreiber kommt. */
  private rebuildSections(): void {
    const q = this._search.trim().toLowerCase();
    this.filtered = !q
      ? this.courses
      : this.courses.filter(c => (c.displayName || '').toLowerCase().includes(q));
    // „In Arbeit" umfasst zwei Fälle:
    //  • tatsächlich begonnen (≥1 gelöst) und noch nicht fertig → nach Reset (solvedCount=0)
    //    verschwindet der Kurs hier wieder.
    //  • angepinnte, noch nicht fertige Kurse — der User will die ganz oben sehen (Schnellzugriff),
    //    auch bevor er sie zum ersten Mal angeklickt hat.
    // Angepinnte zuerst; sortCourses hat schon nach lastActivityAt sortiert, die Ordnung bleibt.
    const eligible = this.filtered.filter(c =>
      c.puzzleCount > 0 && c.solvedCount < c.puzzleCount &&
      (c.isPinned || c.solvedCount > 0));
    this.inProgressCourses = [
      ...eligible.filter(c => c.isPinned),
      ...eligible.filter(c => !c.isPinned),
    ];
    // UI-Sweep 2026-10-11 (r2-rp-course-dupes): was unter „In Arbeit" steht, erscheint nicht ein zweites Mal
    // als identische Karte in seiner normalen Sektion.
    const inProgress = new Set(this.inProgressCourses.map(c => c.bookId));
    const rest = this.filtered.filter(c => !inProgress.has(c.bookId));
    this.sharedCourses = rest.filter(c => c.isShared);
    this.publicCourses = rest.filter(c => !c.isOwned && !c.isShared);
    this.ownCourses = rest.filter(c => c.isOwned);
    this.sections = [
      { titleKey: 'courses.sectionInProgress', hintKey: 'courses.sectionInProgressHint', items: this.inProgressCourses },
      { titleKey: 'courses.sectionSharedWithMe', hintKey: 'courses.sectionSharedWithMeHint', items: this.sharedCourses },
      { titleKey: 'courses.sectionPublic', hintKey: 'courses.sectionPublicHint', items: this.publicCourses },
      { titleKey: 'courses.sectionOwn', hintKey: 'courses.sectionOwnHint', items: this.ownCourses },
    ];
    // Gleichnamige Kurse (über die GANZE Liste, nicht nur die Suche) bekommen ihre Herkunft als Untertitel.
    const byTitle = new Map<string, number>();
    for (const c of this.courses) {
      const key = (c.displayName || '').trim().toLowerCase();
      byTitle.set(key, (byTitle.get(key) ?? 0) + 1);
    }
    this.duplicateTitles = new Set([...byTitle].filter(([, n]) => n > 1).map(([k]) => k));
    this.reimportCount = this.courses.filter(c => c.needsReimport).length;
  }

  /** UI-Sweep 2026-10-11: Titel, die mehrfach vorkommen (klein, getrimmt). */
  private duplicateTitles = new Set<string>();
  /** Zahl der Kurse mit „Neu holen"-Markierung (r2-rp-course-warn). */
  reimportCount = 0;
  /** Erklärung zur Markierung aufgeklappt? */
  reimportOpen = false;

  /** Herkunft für die Karte — nur bei gleichnamigen Kursen, sonst null (r2-rp-course-dupes). */
  originOf(c: CourseListItem): { chessable: boolean; owner: string | null } | null {
    if (!this.duplicateTitles.has((c.displayName || '').trim().toLowerCase())) return null;
    const key = `${c.bookId}`;
    let o = this.originCache.get(key);
    if (!o || o.chessable !== !!c.isChessableImport || o.owner !== (c.ownerUsername ?? null)) {
      o = { chessable: !!c.isChessableImport, owner: c.ownerUsername ?? null };
      this.originCache.set(key, o);
    }
    return o;
  }
  /** Stabile Objekte je Kurs — sonst bekäme die Karte bei jedem Durchlauf ein neues Input-Objekt. */
  private originCache = new Map<string, { chessable: boolean; owner: string | null }>();

  isOffline(c: CourseListItem): boolean {
    return this.offlineFiles.has(c.fileName);
  }

  /** Kurs fürs Dashboard an-/abpinnen (optimistisch, Rollback bei Fehler). */
  togglePin(c: CourseListItem): void {
    const target = !c.isPinned;
    this.pinning = c.bookId;
    c.isPinned = target; // optimistisch
    this.rebuildSections();
    const req = target ? this.courseService.pinCourse(c.bookId) : this.courseService.unpinCourse(c.bookId);
    req.subscribe({
      next: () => {
        this.pinning = null;
        this.snackbar.info(this.translate.instant(target ? 'courses.pinned' : 'courses.unpinned', { name: c.displayName }), { action: 'common.ok', duration: 2000 });
      },
      error: () => {
        c.isPinned = !target; // Rollback
        this.rebuildSections();
        this.pinning = null;
        this.snackbar.info(this.translate.instant('courses.pinFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }

  /** Kapitelübersicht eines Buchs auf-/zuklappen; Kapitel werden beim ersten Öffnen lazy geladen. */
  toggleChapters(c: CourseListItem): void {
    if (this.expandedBook === c.bookId) { this.expandedBook = null; return; }
    this.expandedBook = c.bookId;
    if (this.chaptersByBook[c.bookId]) return; // schon geladen
    this.loadingChapters = c.bookId;
    // In der gewählten Sprache des Kurses (Kurs-Übersetzung): die Kapitel tragen dann ihr `label`.
    this.courseService.getChapters(c.bookId, this.courseLang.requestLang({ bookId: c.bookId, fileName: c.fileName })).subscribe({
      next: chapters => {
        this.chaptersByBook[c.bookId] = chapters;
        this.loadingChapters = null;
      },
      error: () => {
        this.loadingChapters = null;
        this.snackbar.info(this.translate.instant('courses.chaptersLoadFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }

  /** Buch offline speichern (alle Puzzles laden + cachen) bzw. den Cache wieder entfernen. */
  toggleOffline(c: CourseListItem): void {
    if (this.isOffline(c)) {
      removeBookOffline(c.fileName);
      this.offlineFiles.delete(c.fileName);
      this.snackbar.info(this.translate.instant('courses.offlineRemoved', { name: c.displayName }), { action: 'common.ok', duration: 2000 });
      return;
    }
    this.savingOffline = c.bookId;
    // Die Kopie merkt sich ihre Sprache — weicht die Wahl später ab, bietet die Kursseite „neu herunterladen" an.
    const lang = this.courseLang.requestLang({ bookId: c.bookId, fileName: c.fileName });
    this.courseService.getBookPuzzles(c.bookId, lang).subscribe({
      next: puzzles => {
        // Cache-Schreiben kann fehlschlagen (Quota/Privatmodus) — dann EHRLICH als Fehler melden
        // statt ein Offline-Häkchen zu zeigen, hinter dem keine Kopie liegt.
        const saved = saveBookOffline(c.fileName, puzzles, c.bookId, lang);
        this.savingOffline = null;
        if (!saved) {
          this.snackbar.info(this.translate.instant('courses.offlineFailed'), { action: 'common.ok', duration: 3000 });
          return;
        }
        this.offlineFiles.add(c.fileName);
        this.snackbar.info(this.translate.instant('courses.offlineSaved', { name: c.displayName, count: puzzles.length }), { action: 'common.ok', duration: 2500 });
      },
      error: () => {
        this.savingOffline = null;
        this.snackbar.info(this.translate.instant('courses.offlineFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }

  ngOnInit(): void {
    this.offlineFiles = new Set(cachedBookFileNames());
    this.loadCourses();
  }

  /**
   * Angefangene Kurse (lastActivityAt gesetzt) nach vorn — nach letzter Verwendung absteigend;
   * noch nicht angefangene danach, alphabetisch. Wird je Sektion (öffentlich/eigene) durch die
   * order-erhaltenden Filter-Getter beibehalten.
   */
  private sortCourses(list: CourseListItem[]): CourseListItem[] {
    return [...list].sort((a, b) => {
      const aT = a.lastActivityAt ? Date.parse(a.lastActivityAt) : null;
      const bT = b.lastActivityAt ? Date.parse(b.lastActivityAt) : null;
      if (aT !== null && bT !== null) return bT - aT;   // beide angefangen → zuletzt verwendet zuerst
      if (aT !== null) return -1;                        // nur a angefangen → vor b
      if (bT !== null) return 1;                         // nur b angefangen → vor a
      return a.displayName.localeCompare(b.displayName); // beide nicht → alphabetisch
    });
  }

  loadCourses(): void {
    this.loading = true;
    this.loadError = false;
    this.courseService.getCourses().subscribe({
      next: courses => {
        this.courses = this.sortCourses(courses);
        this.rebuildSections();
        this.offlineList = false;
        // Dateiname ↔ Kurs-Id: damit ein einzelnes Buch-Puzzle die Sprachwahl seines Kurses findet.
        this.courseLang.rememberFiles(courses.map(c => ({ bookId: c.bookId, fileName: c.fileName })));
        saveCourseListCache(courses);   // Offline-Fallback aktuell halten
        this.loading = false;
      },
      error: () => {
        // Offline/Server weg → letzte bekannte Liste zeigen, beschränkt auf heruntergeladene
        // (offline spielbare) Kurse. Ohne Cache: Ladefehler an der Stelle des Leerhinweises (d3).
        const cached = loadCourseListCache<CourseListItem>().filter(c => this.offlineFiles.has(c.fileName));
        if (cached.length > 0) {
          this.courses = this.sortCourses(cached);
          this.rebuildSections();
          this.offlineList = true;
        } else {
          this.loadError = true;
        }
        this.loading = false;
      }
    });
  }

  reset(course: CourseListItem): void {
    this.confirm.ask('courses.resetConfirm', { name: course.displayName }).subscribe(ok => {
      if (!ok) return;
      this.courseService.reset(course.bookId).subscribe({
        next: p => {
          course.solvedCount = p.solvedCount;
          course.progressPercent = p.progressPercent;
          this.rebuildSections();
          delete this.chaptersByBook[course.bookId]; // Kapitel-Fortschritt neu laden beim nächsten Öffnen
        },
        error: () => this.snackbar.info(this.translate.instant('courses.resetFailed'), { action: 'common.ok', duration: 3000 })
      });
    });
  }

  /** Öffnet den „Kurs teilen"-Dialog (Freunde auswählen / Freigaben verwalten). */
  openShareDialog(course: CourseListItem): void {
    this.dialog.open<ShareCourseDialogComponent, ShareCourseDialogData>(
      ShareCourseDialogComponent, {
        width: '440px', maxWidth: '95vw',
        data: { bookId: course.bookId, courseName: course.displayName }
      });
  }

  /** Öffnet den „Kurs verknüpfen"-Dialog (Buch↔Workbook). Kandidaten = alle anderen Kurse der Liste. */
  openLinkDialog(course: CourseListItem): void {
    const candidates = this.courses
      .filter(c => c.bookId !== course.bookId)
      .map(c => ({ bookId: c.bookId, displayName: c.displayName }));
    const ref = this.dialog.open<LinkCourseDialogComponent, LinkCourseDialogData, boolean>(
      LinkCourseDialogComponent, {
        width: '440px', maxWidth: '95vw',
        data: {
          bookId: course.bookId, displayName: course.displayName,
          currentLinkedBookId: course.linkedBookId ?? null,
          currentLinkedName: course.linkedDisplayName ?? null,
          candidates,
        }
      });
    ref.afterClosed().subscribe(changed => { if (changed) this.loadCourses(); });
  }

  /** Öffnet „Neuen Kurs erstellen"; legt nach Bestätigung an — mit angehängtem PGN oder leer. */
  openCreateDialog(): void { this.openDialog('course'); }

  /** Öffnet „Eigene Partien importieren" — derselbe Dialog, aber die PGN-Datei ist Pflicht.
   *  Eigener Knopf, weil niemand seine Partien unter „Kurs erstellen" sucht. */
  openImportDialog(): void { this.openDialog('games'); }

  private openDialog(mode: 'course' | 'games'): void {
    if (this.creating) return;
    const ref = this.dialog.open<CreateCourseDialogComponent, CreateCourseDialogData, CreateCourseDialogResult>(
      CreateCourseDialogComponent, { width: '440px', maxWidth: '95vw', data: { mode } });
    ref.afterClosed().subscribe(result => {
      if (!result) return;
      this.createCourse(result.name, result.file);
    });
  }

  /** Legt den Kurs an und sortiert ihn in die Liste ein. Ein LEERER Kurs führt direkt auf seine
   *  Detailseite — dort kommen Kapitel und Stellungen hinein, ohne die er nur nutzlos herumstünde. */
  createCourse(name: string, file: File | null): void {
    this.creating = true;
    this.courseService.createCourse(name, file).subscribe({
      next: course => {
        this.creating = false;
        // Neuen Kurs einsortieren (statt kompletten Reload) + Menü/Navbar-Zugriff neu prüfen lassen.
        this.courses = this.sortCourses([...this.courses.filter(c => c.bookId !== course.bookId), course]);
        this.rebuildSections();
        this.courseService.notifyAccessChanged();
        if (!file) { this.router.navigate(['/courses', course.bookId]); return; }
        this.snackbar.info(this.translate.instant('courses.create.success', { name: course.displayName, count: course.puzzleCount }), { action: 'common.ok', duration: 3000 });
      },
      error: err => {
        this.creating = false;
        const msg = apiErrorText(err, this.translate, 'courses.create.failed');
        // Eröffnungsrepertoire statt Kurs-PGN: den Weg dorthin gleich anbieten (gemeldet 2026-10-07).
        if (err?.error?.code === 'course_pgn_is_repertoire') {
          this.snackbar.info(msg, { action: 'courses.create.toRepertoires', duration: 15000 })
            .onAction().subscribe(() => this.router.navigate(['/repertoires']));
          return;
        }
        this.snackbar.info(msg, { action: 'common.ok', duration: 6000 });
      }
    });
  }

  /** Eigenen Kurs löschen (mit Rückfrage). */
  deleteCourse(course: CourseListItem): void {
    this.confirm.ask('courses.deleteConfirm', { name: course.displayName }).subscribe(ok => {
      if (!ok) return;
      this.deleting = course.bookId;
      this.courseService.deleteCourse(course.bookId).subscribe({
        next: () => {
          this.deleting = null;
          this.courses = this.courses.filter(c => c.bookId !== course.bookId);
          this.rebuildSections();
          delete this.chaptersByBook[course.bookId];
          this.courseService.notifyAccessChanged();
        },
        error: () => {
          this.deleting = null;
          this.snackbar.info(this.translate.instant('courses.deleteFailed'), { action: 'common.ok', duration: 3000 });
        }
      });
    });
  }

  /** Kurs in ein neues Repertoire umwandeln. Beim EIGENEN Kurs verschiebt der Server: er legt das
   *  Repertoire an und löscht danach den Kurs samt Fortschritt und Freigaben ALLER Nutzer — deshalb
   *  vorher die Rückfrage wie beim Löschen. Geteilte Gruppen-/Admin-Kurse bleiben (Kopie, ohne Rückfrage). */
  convertToRepertoire(course: CourseListItem): void {
    (course.isOwned ? this.confirm.ask('courses.convertMoveConfirm', { name: course.displayName }) : of(true)).subscribe(ok => {
      if (!ok) return;
      this.converting = course.bookId;
      this.courseService.convertToRepertoire(course.bookId).subscribe({
        next: rep => {
          this.converting = null;
          // Verschieben: ein EIGENER Kurs wurde serverseitig entfernt → auch aus der Liste nehmen.
          // Geteilte Gruppen-/Admin-Kurse bleiben bestehen (gehören dem User nicht).
          if (course.isOwned) {
            this.courses = this.courses.filter(c => c.bookId !== course.bookId);
          this.rebuildSections();
            delete this.chaptersByBook[course.bookId];
            this.courseService.notifyAccessChanged();
          }
          this.snackbar.info(this.translate.instant(course.isOwned ? 'courses.movedToRepertoire' : 'courses.convertedToRepertoire', { name: rep.name }), { action: 'common.ok', duration: 3000 });
        },
        error: () => {
          this.converting = null;
          this.snackbar.info(this.translate.instant('courses.convertToRepertoireFailed'), { action: 'common.ok', duration: 3000 });
        }
      });
    });
  }

  downloadPgn(course: CourseListItem): void {
    this.downloadingPgn = course.bookId;
    this.courseService.downloadPgn(course.bookId).subscribe({
      next: blob => {
        this.downloadingPgn = null;
        const safe = (course.displayName || 'course').replace(/[^A-Za-z0-9]+/g, '_');
        downloadBlob(blob, `${safe}.pgn`);
      },
      error: () => {
        this.downloadingPgn = null;
        this.snackbar.info(this.translate.instant('courses.downloadFailed'), { action: 'common.ok', duration: 3000 });
      }
    });
  }
}
