import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable, of } from 'rxjs';
import { catchError, map, switchMap, take, timeout } from 'rxjs/operators';
import { LoadedProfile, PreferencesService, ProfilePreferences } from './preferences.service';
import { mergeStamped } from './stamped-choices';
import { SolveModeDialogComponent, SolveModeDialogData, SolveModeDialogResult } from '../shared/solve-mode/solve-mode-dialog.component';

/** Die beiden Spielweisen. Die Zeichenketten sind der Vertrag zum Server (Spalte `Mode`
 *  auf PuzzleAttempts/CourseAttempts/BookPuzzleAttempts/EndlessSessions/WeeklyPostAttempts)
 *  — nicht umbenennen ohne Migration. */
export type SolveMode = 'training' | 'easy';

/** Speicher-Schlüssel im localStorage — Namensschema wie die übrigen Einstellungen. */
const STORE_KEY = 'rookhub_solve_modes';

/**
 * Deckel gegen unbegrenztes Wachstum: pro Kurs entsteht ein Eintrag, und die Zahl der Kurse
 * wächst. Bei Überschreitung fliegen die ältesten Einträge raus — die werden ohnehin am
 * ehesten neu erfragt.
 */
const MAX_EINTRAEGE = 200;

/**
 * Eintrag der geräteweiten Grundwahl („für alle Puzzle-Bereiche übernehmen" im Dialog). Gilt für
 * die Bereiche OHNE Id (puzzles, daily, book, endless), die keine eigene Wahl haben — Kurse und
 * Wochenposts (`course:<id>`, `weekly:<id>`) fragen weiter einzeln.
 */
const GRUNDWAHL = '*';

/**
 * UI-Sweep 2026-10-11 (r2-rp-mode-title): Grundwahl für ALLE Kurse. Gesetzt, wenn im Kursdialog „für alle
 * Puzzle-Bereiche übernehmen" angehakt ist — sonst fragte jeder Kurs wieder. Eine eigene Wahl je Kurs
 * (über das ⋮-Menü) schlägt sie weiterhin.
 */
const KURS_GRUNDWAHL = 'course:*';

interface Eintrag { mode: SolveMode; at: number; }

/** So lange wartet `ensure` höchstens auf das Profil, bevor es mit dem Gerätestand entscheidet (Server träge/weg). */
export const PROFILE_WAIT_MS = 5000;

/**
 * Spielweise je Bereich: „Einfachmodus" (Figuren normal ziehbar) oder „Trainingsmodus"
 * (Brett eingefroren bzw. die vom Nutzer eingestellte Visualisierungsstufe).
 *
 * Gefragt wird **einmalig je Bereich** — für Kurse zusätzlich je Kurs, weil sich ein
 * Taktikbuch anders trainiert als ein Eröffnungskurs. Danach gilt die gemerkte Wahl
 * wortlos; umschalten kann man jederzeit über die Aktionsleiste des Solvers.
 * In den Bereichen ohne Id bietet der Dialog „für alle Puzzle-Bereiche übernehmen" an
 * (voreingestellt, UX-004): dann wird die Wahl zur geräteweiten Grundwahl, und Puzzles,
 * Tagespuzzle, Buch-Puzzles und Endless fragen nicht noch einmal einzeln.
 *
 * Die Wahl liegt im localStorage UND — für Angemeldete — im Profil (`GET/PUT /api/profile`, Feld `solveModes`;
 * 1.015.1). Vorher nur am Gerät: der Schlüssel war seit F1-010 als Konto-Spur eingestuft und wurde beim Abmelden,
 * beim Ablauf der Sitzung und beim Wechsel der Anmeldung zwischen den Hubs geräumt — die Frage kam immer wieder, und
 * jedes Gerät fragte einzeln. Jetzt:
 * - das Profil gewinnt, bei Konflikt je Bereich der jüngere Eintrag (`at`);
 * - eine neue Wahl geht lokal UND ins Profil (der Server führt je Bereich zusammen);
 * - hat das Profil noch nichts, das Gerät aber schon, wird beim ersten Laden hochgeladen — niemand wird neu gefragt;
 * - `ensure` fragt erst, wenn das Profil geladen ist (höchstens {@link PROFILE_WAIT_MS}), sonst käme der Dialog direkt
 *   nach dem Anmelden auf einem neuen Gerät doch;
 * - Gäste (Tagespuzzle, geteilte Puzzles) bleiben beim Gerätestand.
 * Der lokale Schlüssel ist wieder eine Gerätevorliebe (`local-storage-keys.ts`, wie das Brett-Thema).
 */
@Injectable({ providedIn: 'root' })
export class SolveModeService {
  private readonly dialog = inject(MatDialog);
  private readonly prefs = inject(PreferencesService);
  /** Zuletzt eingearbeitetes Profil (je Konto) — dasselbe Objekt wird nicht zweimal zusammengeführt/hochgeladen. */
  private merged: { userId: number; profile: ProfilePreferences } | null = null;

  constructor() {
    // Optional: Specs ersetzen den PreferencesService oft durch ein schlichtes Objekt.
    this.prefs.profileLoaded$?.subscribe(p => this.mergeProfile(p));
  }

  /** Bereichs-Schlüssel. Kurse/Bücher bekommen ihre Id angehängt. */
  static scopeCourse(bookId: number): string { return `course:${bookId}`; }
  static scopeWeekly(weeklyId: number): string { return `weekly:${weeklyId}`; }
  /** Woodpecker-Training: Linien aus Kursen — eigener Bereich je Plan, fällt wie ein Kurs auf die Kurs-Grundwahl zurück. */
  static scopeWoodpecker(planId: number): string { return `woodpecker:${planId}`; }

  /** Bereich ohne Id (puzzles, daily, book, endless)? Nur dort gilt die Grundwahl. */
  static isShared(scope: string): boolean { return scope !== GRUNDWAHL && !scope.includes(':'); }

  /** Ein einzelner Kurs (nicht die Kurs-Grundwahl selbst)? Ein Woodpecker-Plan zählt mit (seine Linien sind Kurslinien). */
  static isCourse(scope: string): boolean {
    return (scope.startsWith('course:') && scope !== KURS_GRUNDWAHL) || scope.startsWith('woodpecker:');
  }

  /** Gemerkte Wahl, oder null wenn für diesen Bereich noch nie gefragt wurde. Ein Bereich ohne
   *  eigene Wahl fällt auf die Grundwahl zurück, sofern er einer ohne Id ist. */
  get(scope: string): SolveMode | null {
    const alle = this.lesen();
    return modeOf(alle[scope])
      ?? (SolveModeService.isShared(scope) ? modeOf(alle[GRUNDWAHL]) : null)
      ?? (SolveModeService.isCourse(scope) ? modeOf(alle[KURS_GRUNDWAHL]) : null);
  }

  set(scope: string, mode: SolveMode): void {
    this.setMany({ [scope]: mode });
  }

  /** Mehrere Wahlen auf einmal (Dialog mit „für alle übernehmen“) — lokal und in EINEM PUT ins Profil. */
  private setMany(wahl: Record<string, SolveMode>): void {
    const alle = this.lesen();
    const neu: Record<string, Eintrag> = {};
    const at = Date.now();
    for (const [scope, mode] of Object.entries(wahl)) neu[scope] = alle[scope] = { mode, at };
    this.schreiben(alle);
    this.hochladen(neu);
  }

  /** Wahl für einen Bereich vergessen — der nächste Einstieg fragt wieder (bzw. nimmt die Grundwahl).
   *  Nur am Gerät: das Profil kennt kein Löschen und bringt die Wahl beim nächsten Laden zurück. */
  clear(scope: string): void {
    const alle = this.lesen();
    if (!(scope in alle)) return;
    delete alle[scope];
    this.schreiben(alle);
  }

  /**
   * Liefert die Spielweise für einen Bereich und fragt nur, wenn es noch keine gibt.
   * `data.scopeLabel` benennt im Dialog, worum es geht (Kursname, „Tagespuzzle", …).
   *
   * Bewusst ein blockierender Dialog: die Wahl entscheidet, wie gelöst UND wie gewertet wird,
   * und lässt sich hinterher nicht rückwirkend korrigieren. Sie kommt aber nur EINMAL je
   * Bereich — wer schon gewählt hat, sieht nie wieder einen Dialog.
   */
  /** Schmaler Bildschirm (Handy): der Dialog kommt als Blatt vom unteren Rand. Grenze wie im CSS (`599.98px`, docs/DESIGN.md
   *  4.2) — bei genau 600 px gilt sonst die TS-Regel als Handy, das CSS aber nicht (1-px-Überlappung). */
  static narrow(): boolean {
    return typeof matchMedia === 'function' && matchMedia('(max-width: 599.98px)').matches;
  }

  ensure(scope: string, data: SolveModeDialogData = {}): Observable<SolveMode> {
    const gemerkt = this.get(scope);
    if (gemerkt) return of(gemerkt);
    // Angemeldet, Profil noch nicht eingearbeitet: erst das Profil abwarten — es kennt die Wahl vielleicht schon.
    if (this.profilAusstehend()) {
      return this.prefs.serverProfile().pipe(
        take(1),
        timeout({ first: PROFILE_WAIT_MS, with: () => of(null) }),
        catchError(() => of(null)),
        switchMap(profile => {
          const userId = this.prefs.accountId();
          if (profile && userId != null) this.mergeProfile({ userId, profile });
          return this.fragen(scope, data);
        }),
      );
    }
    return this.fragen(scope, data);
  }

  /** Muss `ensure` noch auf das Profil warten? Nur angemeldet und solange dieses Konto nicht eingearbeitet ist. */
  private profilAusstehend(): boolean {
    if (typeof this.prefs.serverProfile !== 'function' || typeof this.prefs.accountId !== 'function') return false;
    const userId = this.prefs.accountId();
    return userId != null && this.merged?.userId !== userId;
  }

  /** Gemerkte Wahl oder — erst jetzt — der Dialog. */
  private fragen(scope: string, data: SolveModeDialogData): Observable<SolveMode> {
    const gemerkt = this.get(scope);
    if (gemerkt) return of(gemerkt);
    const shared = SolveModeService.isShared(scope);
    const course = SolveModeService.isCourse(scope);
    return this.openDialog({ ...data, offerApplyAll: shared || course })
      .pipe(map((r: SolveModeDialogResult | undefined) => {
        // Abgebrochen (Escape ist durch disableClose aus, aber defensiv) → Trainingsmodus,
        // das bisherige Verhalten. Gemerkt wird nur eine echte Wahl.
        const mode: SolveMode = r?.mode === 'easy' ? 'easy' : 'training';
        if (r) {
          const wahl: Record<string, SolveMode> = { [scope]: mode };
          if ((shared || course) && r.applyAll) wahl[GRUNDWAHL] = mode;
          if (course && r.applyAll) wahl[KURS_GRUNDWAHL] = mode;
          this.setMany(wahl);
        }
        return mode;
      }));
  }

  /**
   * Der gemeinsame Spielweisen-Dialog, ohne etwas zu merken. Benutzt von `ensure` und — UI-Sweep 2026-10-11
   * (r2-rk-weekly-mode-dialog) — vom Wochenpost, der seine Wahl selbst je Post merkt; vorher hatte der eine eigene,
   * anders aussehende Abfrage mit anderem Erklärtext.
   */
  openDialog(data: SolveModeDialogData = {}): Observable<SolveModeDialogResult | undefined> {
    return this.dialog
      // autoFocus 'dialog': den Container fokussieren, nicht die erste Karte — sonst trägt
      // „Trainingsmodus" den Fokusrahmen und sieht vorausgewählt aus (UX-004).
      // Neues Design (UI-Sweep 2026-10-10, Texte unverändert): deutlich abgedunkelter Hintergrund — ohne ihn hing der
      // Dialog scheinbar schief über dem Brett —, am Handy als Blatt am unteren Rand.
      .open(SolveModeDialogComponent, {
        ...(SolveModeService.narrow()
          ? { width: '100vw', maxWidth: '100vw', position: { bottom: '0' }, panelClass: ['solve-mode-panel', 'solve-mode-sheet'] }
          : { width: '560px', maxWidth: '94vw', panelClass: 'solve-mode-panel' }),
        hasBackdrop: true, backdropClass: 'solve-mode-backdrop',
        disableClose: true, autoFocus: 'dialog',
        data,
      })
      .afterClosed();
  }

  /**
   * Visualisierungsstufe zur Spielweise. „Einfach" ist immer Stufe 0 (Drag & Drop);
   * „Training" ist die GLOBAL eingestellte Stufe, mindestens 1 — wer Blindspiel, Dunkel oder
   * Unsichtbar gewählt hat, behält das. Ein hartes Erzwingen von Stufe 1 würde diese Nutzer
   * stillschweigend herabstufen.
   */
  levelFor(mode: SolveMode): number {
    return mode === 'easy' ? 0 : Math.max(1, this.prefs.visualization);
  }

  /** Umkehrung: aus einer Stufe die Spielweise ableiten — für den Fall, dass der Nutzer die
   *  Stufe direkt über die Einstellungen ändert und die gemerkte Wahl mitziehen soll. */
  modeForLevel(level: number): SolveMode {
    return level > 0 ? 'training' : 'easy';
  }

  /**
   * Geladenes Profil mit dem Gerät zusammenführen: Profil gewinnt, bei Konflikt der jüngere Eintrag. Was das Profil
   * nicht (oder nur älter) hat, wird hochgeladen — beim ersten Laden nach dieser Änderung der ganze Gerätestand.
   */
  private mergeProfile({ userId, profile }: LoadedProfile): void {
    if (this.merged?.userId === userId && this.merged.profile === profile) return;
    this.merged = { userId, profile };
    const { merged, upload, changed } = mergeStamped<Eintrag>(this.lesen(), profile.solveModes as Record<string, Eintrag> | null,
      (_k, e) => !!modeOf(e));
    if (changed) this.schreiben(merged);
    this.hochladen(upload);
  }

  /** Einträge ins Profil (nur angemeldet; der Server führt je Bereich zusammen). */
  private hochladen(eintraege: Record<string, Eintrag>): void {
    if (!Object.keys(eintraege).length || typeof this.prefs.saveToServer !== 'function') return;
    if (typeof this.prefs.accountId === 'function' && this.prefs.accountId() == null) return;
    this.prefs.saveToServer({ solveModes: eintraege });
  }

  private lesen(): Record<string, Eintrag> {
    try {
      const roh = localStorage.getItem(STORE_KEY);
      if (!roh) return {};
      const o = JSON.parse(roh);
      return o && typeof o === 'object' && !Array.isArray(o) ? o : {};
    } catch {
      return {};   // kaputter/gesperrter Speicher: lieber neu fragen als abstürzen
    }
  }

  private schreiben(alle: Record<string, Eintrag>): void {
    // Die Grundwahl zählt nicht mit und fliegt nie raus — sonst fragten alle Bereiche wieder.
    const keys = Object.keys(alle).filter(k => k !== GRUNDWAHL && k !== KURS_GRUNDWAHL);
    if (keys.length > MAX_EINTRAEGE) {
      // Älteste zuerst wegwerfen; sie werden beim nächsten Einstieg einfach neu erfragt.
      keys.sort((a, b) => (alle[a]?.at || 0) - (alle[b]?.at || 0));
      for (const k of keys.slice(0, keys.length - MAX_EINTRAEGE)) delete alle[k];
    }
    try { localStorage.setItem(STORE_KEY, JSON.stringify(alle)); } catch { /* Privatmodus */ }
  }
}

function modeOf(e: Eintrag | undefined): SolveMode | null {
  return e && (e.mode === 'easy' || e.mode === 'training') ? e.mode : null;
}
