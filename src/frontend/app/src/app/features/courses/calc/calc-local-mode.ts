import { CalcBackend, CalculationService } from './calculation.service';
import { LocalCalculationBackend } from './calc-local.backend';
import { readCalcNoticeDismissed, writeCalcNoticeDismissed } from './calc-local.util';

/**
 * Woher kommen die Daten der Kalkulations-Seite? (ohne Angular)
 * Angemeldet: der Server. Anonym (öffentlicher Kalkulations-Kurs per Kurz-URL): der
 * localStorage dieses Geräts. Genau EINE Öffnung gibt es serverseitig — LESEND, und nur für
 * ausdrücklich öffentlich freigegebene Bücher; geschrieben wird anonym gar nichts.
 * Dazu der ruhige Geräte-Hinweis und die Warnung, wenn der Gerätespeicher nicht mitspielt.
 */
export class CalcLocalMode {
  private localBackend: LocalCalculationBackend | null = null;

  /**
   * Der Gerätespeicher hat zuletzt NICHT mitgespielt (Privatmodus, Quota voll, gesperrt). Dann ist
   * „liegt nur auf diesem Gerät" die falsche Auskunft — es liegt gerade NIRGENDS. Der Hinweis oben
   * wechselt deshalb auf eine Warnung. Wird bei der nächsten erfolgreichen Speicherung zurückgesetzt.
   */
  saveFailed = false;

  /**
   * Der ruhige Hinweis „liegt nur auf diesem Gerät" wurde für DIESEN Kurs weggeklickt
   * (localStorage, siehe `calc-local.util.ts`). Er ist ein Vorschlag — den darf man abstellen.
   */
  noticeDismissed = false;

  /**
   * Die WARNUNG „kann gerade gar nicht gespeichert werden" wurde weggeklickt. Bewusst NUR im
   * Speicher und bewusst nicht dauerhaft: sie meldet Datenverlust, keinen Vorschlag — beim
   * nächsten fehlgeschlagenen Schreibversuch steht sie wieder da (siehe {@link setSaveFailed}).
   */
  private warningDismissed = false;

  /** Niemand angemeldet → Stellungen lesend vom öffentlichen Endpoint, alles Selbstgemachte
   *  in den localStorage. Serverseitige Persistenz bleibt angemeldeten Nutzern vorbehalten. */
  enter(api: CalculationService, bookId: number, loggedIn: boolean): void {
    if (!loggedIn && Number.isFinite(bookId)) {
      this.localBackend = new LocalCalculationBackend(api, bookId);
    }
    this.noticeDismissed = readCalcNoticeDismissed(bookId);
  }

  /** Server oder Gerät — die einzige Stelle, die diesen Unterschied kennt. */
  backend(server: CalcBackend): CalcBackend { return this.localBackend ?? server; }

  /** Läuft alles nur lokal? (dezenter Hinweis + Anmelde-Link in der Ansicht) */
  get localOnly(): boolean { return this.localBackend !== null; }

  /** Der ruhige Geräte-Hinweis (anonym, Speicher tut es) — wegklickbar. */
  get showNotice(): boolean {
    return this.localOnly && !this.saveFailed && !this.noticeDismissed;
  }

  /** Die Warnung, dass gerade NICHTS gespeichert werden kann — nur für die Sitzung wegklickbar. */
  get showWarning(): boolean {
    return this.localOnly && this.saveFailed && !this.warningDismissed;
  }

  dismissNotice(bookId: number): void {
    this.noticeDismissed = true;
    writeCalcNoticeDismissed(bookId);
  }

  /** Nur für diese Sitzung: der nächste Fehlschlag holt die Warnung zurück. */
  dismissWarning(): void {
    this.warningDismissed = true;
  }

  /** Einzige Stelle, die `saveFailed` setzt: jeder NEUE Fehlschlag zeigt die Warnung wieder. */
  setSaveFailed(failed: boolean): void {
    if (failed) this.warningDismissed = false;
    this.saveFailed = failed;
  }
}
