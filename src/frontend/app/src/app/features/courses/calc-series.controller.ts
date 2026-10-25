import { MatDialog } from '@angular/material/dialog';
import { TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { formatScore } from './calc/calc-review.util';
import { CalculationService } from './calc/calculation.service';
import { CalcEdition, CalcEditionsService } from './calc-editions.service';
import { CalcEditionDialogComponent, CalcEditionDialogResult } from './calc-edition-dialog.component';
import { CalcMembersDialogComponent } from './calc-members-dialog.component';
import { CourseManageChapter } from './course.types';

/** Was die Kalk-Serie von der Seite braucht, die sie trägt (Kurs-Detailseite). */
export interface CalcSeriesHost {
  readonly bookId: number;
  /** Darf der Nutzer die Ausgaben pflegen? (Besitzer/Admin — `canManage` des Kurs-DTO.) */
  readonly canManage: boolean;
  /** Seiten-Sperre während einer Speicherung (`busy` der Seite). */
  setBusy(busy: boolean): void;
  /** Abo an die Lebensdauer der Seite hängen. */
  track(sub: Subscription): void;
}

/**
 * Kalkulations-Serie (Calc-Serie/Noel) und Kapitelpunkte der Kurs-Detailseite — herausgelöst aus der
 * Kapitel-Verwaltung (Codereview F3-021, Zerlegung Z29). Die Serie hat keinen eigenen Bildschirmbereich: ihre
 * Marken stehen in den Kapitelzeilen, ihre Menüpunkte in den ⋮-Menüs. Deshalb ein Zustandshalter ohne Vorlage
 * statt einer Kind-Komponente; die Seite reicht {@link edition} und {@link chapterScore} an die Zeilen weiter.
 */
export class CalcSeriesController {
  /** Kalkulations-Punkte je Kapitel (Schlüssel = Kapitelname, '' = „ohne Kapitel"); nur bewertete Kapitel. */
  chapterScores: Record<string, string> = {};
  /** Ausgaben je Kapitelname. Verwalter sehen ALLE (auch künftige), Betrachter nur freigegebene (Backend filtert). */
  editions: Record<string, CalcEdition> = {};

  constructor(
    private readonly host: CalcSeriesHost,
    private readonly calc: Pick<CalculationService, 'getBook'>,
    private readonly calcEditions: CalcEditionsService,
    private readonly dialog: MatDialog,
    private readonly snackbar: SnackbarService,
    private readonly translate: TranslateService,
  ) {}

  /** Nach jedem Laden der Seite: Kalkulationsbuch → Punkte + Ausgaben nachladen, sonst alles leeren. */
  reload(isCalculation: boolean): void {
    if (isCalculation) { this.loadChapterScores(); this.loadEditions(); }
    else { this.chapterScores = {}; this.editions = {}; }
  }

  /** Punkte-Anzeige eines Kapitels („14 / 24") — leer, wenn nicht bewertet / kein Kalkulationsbuch. */
  chapterScore(chapter: CourseManageChapter): string {
    return this.chapterScores[chapter.name ?? ''] ?? '';
  }

  /** Ausgabe eines Kapitels (oder null). */
  edition(chapter: CourseManageChapter): CalcEdition | null {
    return this.editions[chapter.name ?? ''] ?? null;
  }

  /** Kalkulations-Kapitelpunkte laden (eigener Endpoint, hält die Kurs-Detail-DTO frei von Kalkulations-Feldern).
   *  Best-effort: die Zahlen sind Beiwerk, ein Fehler blendet sie nur aus. Nur BEWERTETE Kapitel (ratedCount>0). */
  private loadChapterScores(): void {
    this.host.track(this.calc.getBook(this.host.bookId).subscribe({
      next: book => {
        const map: Record<string, string> = {};
        for (const c of book.chapters ?? []) {
          if ((c.ratedCount ?? 0) <= 0) continue;
          map[c.chapter ?? ''] = formatScore(c.points, c.maxPoints);
        }
        this.chapterScores = map;
      },
      error: () => { this.chapterScores = {}; },
    }));
  }

  /** Ausgaben laden. Verwalter: alle (auch künftige, für die Planung); Betrachter: nur freigegebene.
   *  Best-effort — ein Fehler blendet die Serien-Infos nur aus, der Kurs bleibt bedienbar. */
  private loadEditions(): void {
    const src = this.host.canManage
      ? this.calcEditions.manage(this.host.bookId)
      : this.calcEditions.visible(this.host.bookId);
    this.host.track(src.subscribe({
      next: list => {
        const map: Record<string, CalcEdition> = {};
        for (const e of list) map[e.chapter] = e;
        this.editions = map;
      },
      error: () => { this.editions = {}; },
    }));
  }

  /** Ausgaben-Dialog öffnen (nur Verwalter). Legt eine neue Ausgabe an oder bearbeitet die
   *  bestehende; nach Speichern/Löschen die Ausgaben neu laden. */
  openEdition(chapter: CourseManageChapter): void {
    if (!this.host.canManage) return;
    const name = chapter.name ?? '';
    if (!name) return; // „ohne Kapitel" kann keine geplante Ausgabe haben
    const ref = this.dialog.open(CalcEditionDialogComponent, {
      width: '440px',
      data: { chapter: name, edition: this.editions[name] ?? undefined },
    });
    this.host.track(ref.afterClosed().subscribe((res: CalcEditionDialogResult | undefined) => {
      if (!res) return;
      if ('delete' in res) {
        const ed = this.editions[name];
        if (!ed) return;
        this.host.setBusy(true);
        this.host.track(this.calcEditions.remove(this.host.bookId, ed.id).subscribe({
          next: () => this.done('calc.series.deleted'),
          error: () => this.failed(),
        }));
        return;
      }
      this.host.setBusy(true);
      this.host.track(this.calcEditions.upsert(this.host.bookId, res.save).subscribe({
        next: () => this.done('calc.series.saved'),
        error: () => this.failed(),
      }));
    }));
  }

  /** Serien-Verteiler verwalten (nur Verwalter): Mitglieder + Tester-Häkchen. Der Dialog pflegt
   *  seinen Zustand selbst; hier ist nichts nachzuladen (Mitgliedschaft betrifft den Zugriff, nicht
   *  die angezeigten Kapitel dieser Seite). */
  openMembers(): void {
    if (!this.host.canManage) return;
    this.dialog.open(CalcMembersDialogComponent, { width: '480px', data: { bookId: this.host.bookId } });
  }

  private done(key: string): void {
    this.host.setBusy(false);
    this.snackbar.quick(this.translate.instant(key));
    this.loadEditions();
  }

  private failed(): void {
    this.host.setBusy(false);
    this.snackbar.warn(this.translate.instant('calc.series.saveFailed'));
  }
}
