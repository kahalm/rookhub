import { Injectable, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { LeagueApiService } from '@lh/core/league-api.service';
import { Account, AccountChecks } from '@lh/core/league.models';
import { PrepApiService } from './prep-api.service';

/**
 * Auf der Spielerseite der Spielervorbereitung an Stelle des `LeagueApiService` (nur in ihrem Teilbaum): die geteilten Bausteine
 * der Konto-Suche (`lh-account-suggestions`, `lh-account-checks`) übernehmen, verwerfen und prüfen so über `/api/prep/*`
 * (`prep.manage`) statt über LeagueHub (`league.manage`) — ohne dass an ihnen etwas geändert werden muss. Alles andere bleibt,
 * wie es ist; Konten pflegt die Seite ohnehin nicht.
 */
@Injectable()
export class PrepLeagueApi extends LeagueApiService {
  private readonly prep = inject(PrepApiService);

  /**
   * Warum die letzte (i)-Prüfung abgesagt wurde (Text-Schlüssel), sonst `null`. Die (i)-Prüfung geht durch denselben Türsteher
   * wie die Suche (eine zur Zeit → 409 `busy`; bremst Lichess/chess.com → 503 `rateLimited`); der geteilte Baustein sagt dann
   * nur „ließ sich gerade nicht laden" — den Grund zeigt der Abschnitt „Online-Konten suchen".
   */
  readonly checkNote = signal<string | null>(null);

  override acceptSuggestion(id: number, sure: boolean): Promise<Account> {
    return this.prep.acceptSuggestion(id, sure);
  }

  override rejectSuggestion(id: number): Promise<unknown> {
    return this.prep.rejectSuggestion(id);
  }

  override async accountChecks(kind: 'account' | 'suggestion', id: number): Promise<AccountChecks> {
    if (kind !== 'suggestion') throw new Error('Eingetragene Konten prüft LeagueHub.');
    this.checkNote.set(null);
    try {
      return await this.prep.suggestionChecks(id);
    } catch (err) {
      const reason = err instanceof HttpErrorResponse ? err.error?.reason as string | undefined : undefined;
      this.checkNote.set(CHECK_ERRORS[reason ?? ''] ?? null);
      throw err;
    }
  }
}

/** Absagen der (i)-Prüfung → Text. */
export const CHECK_ERRORS: Record<string, string> = {
  busy: 'prep.accounts.checksBusy',
  rateLimited: 'prep.accounts.checksRateLimited',
};
