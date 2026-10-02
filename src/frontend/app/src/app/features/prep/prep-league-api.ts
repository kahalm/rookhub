import { Injectable, inject } from '@angular/core';
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

  override acceptSuggestion(id: number, sure: boolean): Promise<Account> {
    return this.prep.acceptSuggestion(id, sure);
  }

  override rejectSuggestion(id: number): Promise<unknown> {
    return this.prep.rejectSuggestion(id);
  }

  override accountChecks(kind: 'account' | 'suggestion', id: number): Promise<AccountChecks> {
    return kind === 'suggestion' ? this.prep.suggestionChecks(id) : Promise.reject(new Error('Eingetragene Konten prüft LeagueHub.'));
  }
}
