import { signal } from '@angular/core';
import { Observable } from 'rxjs';
import {
  DirectoryCalendarDay, DirectoryEntry, DirectoryFilter, DirectoryMapResponse, DirectoryPage,
} from './tournament-directory.model';

/*
 * Die drei Lader des Turnierkalenders — Liste, Karte, Monat — je mit ihren Signalen und einem
 * Zaehler gegen ueberholte Antworten. Herausgezogen aus `TournamentDirectoryComponent`
 * (Zerlegung Z28); die Komponente entscheidet weiter, WANN welcher laeuft (`reload`).
 *
 * Alles, was aus einer HTTP-Antwort kommt, liegt in Signalen — siehe Klassenkommentar der
 * Komponente (Angular 22 + fetch: der Abonnent laeuft ausserhalb der Zone).
 */

/** Die Liste, seitenweise. */
export class DirectoryListLoader {
  readonly entries = signal<DirectoryEntry[]>([]);
  readonly total = signal(0);
  readonly truncated = signal(false);
  readonly loading = signal(false);
  /**
   * Seite 1 der Liste ist gescheitert. Ohne diese Marke stand dort der Leer-Text „Zu diesen
   * Filtern gibt es keine Turniere" — der Nutzer schaltete Filter durch, statt es erneut zu
   * versuchen; der eigentliche Fehler stand nur fuenf Sekunden in einem Snackbar (UX-040).
   */
  readonly failed = signal(false);
  page = 1;

  /**
   * Zaehler gegen ueberholte Antworten. Ohne ihn liest der Antwort-Handler `page` zum
   * ANTWORTZEITPUNKT: „Mehr laden" (Seite 2) anstossen, sofort den Filter wechseln — die
   * Antwort auf Seite 2 des ALTEN Filters trifft ein, sieht `page === 1` und ERSETZT die Liste
   * damit. Der falsche Stand bleibt dann stehen, samt Gesamtzahl.
   */
  private generation = 0;

  constructor(
    private readonly search: (filter: DirectoryFilter, page: number, pageSize: number) => Observable<DirectoryPage>,
    private readonly pageSize: number,
    /** „Mehr anzeigen" ist gescheitert — die Liste bis hierher stimmt. */
    private readonly moreFailed: () => void,
  ) {}

  /** Die Seite `page` holen: Seite 1 ersetzt die Liste, jede weitere haengt an. */
  load(filter: DirectoryFilter): void {
    this.loading.set(true);
    this.failed.set(false);
    const generation = ++this.generation;
    const requestedPage = this.page;
    this.search(filter, requestedPage, this.pageSize).subscribe({
      next: page => {
        if (generation !== this.generation) return;
        this.applyPage(page, requestedPage);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.applyFailure(requestedPage);
      },
    });
  }

  private applyPage(page: DirectoryPage, requestedPage: number): void {
    this.entries.set(requestedPage === 1 ? page.items : [...this.entries(), ...page.items]);
    this.total.set(page.total);
    this.truncated.set(page.truncated);
    this.loading.set(false);
  }

  private applyFailure(requestedPage: number): void {
    this.loading.set(false);
    if (requestedPage === 1) {
      // Die alte Liste gehoert zu einem anderen Filter — stehen bleibt die Fehlerkarte.
      this.entries.set([]);
      this.total.set(0);
      this.truncated.set(false);
      this.failed.set(true);
    } else {
      // „Mehr anzeigen" gescheitert: die Liste bis hierher stimmt, nur der Rest fehlt.
      // `loadMore` hat `page` schon hochgezaehlt — ohne Ruecksetzen fragte der naechste Klick
      // (der Erholungsweg) die Seite DANACH an und uebersprang still eine ganze Seite.
      this.page = requestedPage - 1;
      this.moreFailed();
    }
  }
}

/** Die Punkte des Kartenausschnitts. */
export class DirectoryPinsLoader {
  readonly pins = signal<DirectoryEntry[]>([]);
  /**
   * Der Ausschnitt hat mehr Turniere, als die Karte traegt — es fehlen die spaetesten. Ohne das
   * stand unter einer gekappten Karte nur „N Turniere im Ausschnitt", und ganze Monate wirkten leer.
   */
  readonly truncated = signal(false);
  readonly loading = signal(false);
  /** Der letzte Kartenausschnitt kam nicht — sonst stuende dort „0 Turniere im Ausschnitt". */
  readonly failed = signal(false);

  /**
   * Beim erneuten Betreten des Karten-Reiters laufen zwei Abfragen gegeneinander: `reload()`
   * fragt mit dem GEMERKTEN Ausschnitt, die frisch aufgebaute Karte meldet direkt danach ihren
   * eigenen. Ohne Zaehler gewinnt die zufaellig spaetere Antwort.
   */
  private generation = 0;

  constructor(
    private readonly fetch: (filter: DirectoryFilter, bounds: string) => Observable<DirectoryMapResponse>,
  ) {}

  load(filter: DirectoryFilter, bounds: string): void {
    this.loading.set(true);
    const generation = ++this.generation;
    this.failed.set(false);
    this.fetch(filter, bounds).subscribe({
      next: res => {
        if (generation !== this.generation) return;
        this.pins.set(res.items);
        this.truncated.set(res.truncated);
        this.loading.set(false);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.loading.set(false);
        // Wie Liste und Kalender: die alten Punkte (und „es fehlen die spaetesten") gehoeren zu
        // einem anderen Ausschnitt oder Filter — stehen bleibt die Fehlerzeile.
        this.pins.set([]);
        this.truncated.set(false);
        this.failed.set(true);
      },
    });
  }
}

/** Die Tage eines Monats. */
export class DirectoryCalendarLoader {
  readonly days = signal<DirectoryCalendarDay[]>([]);
  readonly loading = signal(false);
  /** Der Monat kam nicht — sonst stuende in der Handy-Agenda „In diesem Monat kein Turnier". */
  readonly failed = signal(false);

  /**
   * Zaehler gegen ueberholte Antworten (Codereview F6-005): zweimal schnell weitergeblaettert, und
   * die langsamere November-Antwort kam NACH der Dezember-Antwort — das Raster zeigte Dezember mit
   * den Tagen des Novembers, also leer, die Agenda „kein Turnier". Ebenso ueberschrieb die
   * spaete Antwort eines alten Filters die neue.
   */
  private generation = 0;

  constructor(
    private readonly fetch: (filter: DirectoryFilter, year: number, month: number) => Observable<DirectoryCalendarDay[]>,
  ) {}

  load(filter: DirectoryFilter, year: number, month: number): void {
    this.loading.set(true);
    this.failed.set(false);
    const generation = ++this.generation;
    this.fetch(filter, year, month).subscribe({
      next: days => {
        if (generation !== this.generation) return;
        this.days.set(days);
        this.loading.set(false);
        this.failed.set(false);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.loading.set(false);
        // Die Tage gehoeren zu einem anderen Monat oder Filter — die Fehlerzeile sagt, was los ist.
        this.days.set([]);
        this.failed.set(true);
      },
    });
  }
}
