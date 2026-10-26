import { CalcChapterModel } from './calc-chapter-model';
import { CalcPositionGroup } from './calc-chapters.util';
import { CalcPositionListItem } from './calculation.service';
import { CALC_GRADE_OPTIONS, CalcGrade, CalcReview, formatScore, gradePoints } from './calc-review.util';
import { formatSecondsClock } from '../../../shared/clock-format.util';
import { labelOr } from '../course-language.util';

/** Was die Beschriftungen der Kalkulations-Seite lesen (alles live). */
export interface CalcLabelsHost {
  t(key: string, params?: object): string;
  chapters: CalcChapterModel;
  /** Stand der ANGEZEIGTEN Stellung. */
  review(): CalcReview;
  /** Sekunden der laufenden Sitzung an dieser Stellung. */
  liveSeconds(): number;
  /** Laufzustand des Kapitel-Trainings als Übersetzungsschlüssel. */
  trainingStateKey(): string;
}

/**
 * Anzeige-Texte der Kalkulations-Seite (ohne Angular): Kapitel-/Stellungsnamen, Punkte, Zeiten und
 * Erklärungen. Rechnet nichts, das gespeichert wird — nur, wie es dasteht.
 */
export class CalcLabels {
  constructor(private readonly host: CalcLabelsHost) {}

  /** Anzeigename des Kapitels („ohne Kapitel" ist auch einer). */
  get chapterName(): string {
    const group = this.host.chapters.chapter;
    if (!group) return '';
    return group.label || group.chapter || this.host.t('courses.noChapter');
  }

  get nextChapterName(): string {
    const next = this.host.chapters.nextChapter;
    if (!next) return '';
    return next.label || next.chapter || this.host.t('courses.noChapter');
  }

  /** Beschriftung des Knopfes: unbewertet „Ergebnis", sonst die gewählte Stufe. */
  get gradeButtonLabel(): string {
    const grade = this.host.review().grade;
    return grade === null
      ? this.host.t('calc.review.result')
      : this.gradeShortLabel(grade);
  }

  /** Erklärung an der Uhr: Laufzustand plus der Hinweis, dass man sie anklicken kann. */
  get timerTooltip(): string {
    return `${this.host.t(this.host.trainingStateKey())}\n`
      + this.host.t('calc.timer.editHint');
  }

  /**
   * Erklärung am Kapitelstand in der Befehlszeile. Die BUCH-Summe steht hier statt als eigene
   * Zeile: interessant, aber nie dringend — und als Dauerzeile war sie die vierte Punktzahl auf
   * einem Schirm.
   */
  get pointsTooltip(): string {
    const chapter = this.host.t('calc.review.totalPointsChapterHint');
    const book = this.host.t('calc.review.bookPoints', {
      score: formatScore(this.host.chapters.bookPoints, this.host.chapters.bookMaxPoints),
    });
    return `${chapter}\n${book}`;
  }

  positionLabel(item: CalcPositionListItem): string {
    const title = labelOr(item.titleLabel, item.title);
    return title?.trim() ? title : `#${this.chapterNumberOf(item)}`;
  }

  /** Die ANGEZEIGTE Nummer einer Stellung (Position im Kapitel, siehe {@link CalcChapterModel.chapterNumberOf}). */
  chapterNumberOf(item: CalcPositionListItem): string {
    return this.host.chapters.chapterNumberOf(item);
  }

  /** Rechenzeit an der ANGEZEIGTEN Stellung inkl. der laufenden Sitzung. */
  get positionTimeDisplay(): string {
    return formatSecondsClock(this.host.review().secondsSpent + this.host.liveSeconds());
  }

  timeDisplay(seconds: number): string {
    return formatSecondsClock(seconds);
  }

  /** Punkte der ANGEZEIGTEN Stellung („2 / 4"); null-Stufe hat keine Punktzahl, sondern gar keine. */
  get positionPoints(): number {
    return gradePoints(this.host.review().grade);
  }

  /** Punkte einer Stellung aus der Sprungliste. */
  pointsOf(item: CalcPositionListItem): number {
    return gradePoints(item.grade);
  }

  /** Kurzform der Stufe für die Sprungliste („Zug, ohne Hauptfolge"). */
  gradeShortLabel(grade: CalcGrade): string {
    return this.host.t(CALC_GRADE_OPTIONS[grade].shortKey);
  }

  /** Punktzahl IMMER mit Maximum — „14" allein ist ohne die Zahl der Stellungen nicht lesbar. */
  scoreDisplay(points: number, max: number): string {
    return formatScore(points, max);
  }

  /** Kapitel-Kopf der Sprungliste: Name plus die Summen des Kapitels („14 / 24 Pkt · 12:30"). */
  chapterLabel(group: CalcPositionGroup): string {
    const name = group.label || group.chapter || this.host.t('courses.noChapter');
    const score = formatScore(group.points, group.maxPoints);
    const summary = group.seconds
      ? this.host.t('calc.review.chapterSummary', { score, time: formatSecondsClock(group.seconds) })
      : this.host.t('calc.review.chapterScore', { score });
    return `${name} · ${summary}`;
  }
}
