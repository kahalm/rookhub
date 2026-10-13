import { ChangeDetectionStrategy, Component, DestroyRef, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LeagueApiService } from '@lh/core/league-api.service';
import { PLAYER_CARD_API } from '@rh/shared/player-card/player-card-api';
import { PlayerCardComponent } from '@rh/shared/player-card/player-card.component';
import { cardDate, cardLang } from '@rh/shared/player-card/card-lang';
import { PrepAccountsComponent } from './prep-accounts.component';
import { PrepApiService } from './prep-api.service';
import { PrepCardApi } from './prep-card-api';
import { PrepLeagueApi } from './prep-league-api';
import { PrepOptions } from './prep.models';

/** Die Gestaltung der Karte als eigenes Style-Bündel (angular.json: bundleName prep-card, inject: false, Quelle
 * prep-card.scss → leaguehub.scss) — erst hier, beim Öffnen einer Spielerseite, geladen. */
export const PREP_CARD_CSS = 'prep-card.css';

export function loadCardStyles(doc: Document): void {
  if (doc.head.querySelector('link[data-prep-card]')) return;
  const link = doc.createElement('link');
  link.rel = 'stylesheet';
  link.href = PREP_CARD_CSS;
  link.setAttribute('data-prep-card', '');
  doc.head.appendChild(link);
}

/**
 * Spielerseite der Spielervorbereitung (`/prep/:id`, Recht `prep.view`): die Spielerkarte von LeagueHub als Teil der Seite,
 * gespeist aus dem Partiebestand (`PrepCardApi`). Darüber, was geladen ist — die jüngsten Partien, „alle laden"
 * (höchstens so viele, wie der Server erlaubt, und es dauert) — und der Namens-Zwilling als Schalter.
 */
@Component({
  selector: 'app-prep-player',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // PrepLeagueApi: die geteilten Bausteine der Konto-Suche übernehmen/verwerfen/prüfen über /api/prep (Phase 4); eine Instanz,
  // damit der Abschnitt „Online-Konten suchen“ die Absage einer (i)-Prüfung zeigen kann.
  providers: [PrepCardApi, { provide: PLAYER_CARD_API, useExisting: PrepCardApi },
    PrepLeagueApi, { provide: LeagueApiService, useExisting: PrepLeagueApi }],
  imports: [RouterLink, TranslatePipe, MatButtonModule, MatCheckboxModule, MatTooltipModule, PlayerCardComponent, PrepAccountsComponent],
  styleUrl: './prep-player.component.scss',
  template: `
    <div class="prep prep-player">
      <a class="back" routerLink="/prep" [queryParams]="backQuery()">← {{ 'prep.back' | translate }}</a>
      @if (scope(); as s) {
        <!-- Ladestand als EINE leise Zeile statt eigenem Kasten (z1-rk-prep-vorlauf); „kann dauern" und der Zwillings-Hinweis
             stehen im Tooltip. -->
        <section class="prep-scope" aria-live="polite">
          <span class="scope-text">
            @if (s.limited) {
              {{ 'prep.loadedSome' | translate: { loaded: fmt(s.loaded), total: fmt(s.games), since: sinceText(s.since) } }}
            } @else {
              {{ 'prep.loadedAll' | translate: { total: fmt(s.games) } }}
            }
          </span>
          @if (s.limited && !options().all) {
            <button mat-button type="button" class="load-all" [disabled]="loading()" (click)="loadAll()"
                    [matTooltip]="'prep.loadAllHint' | translate" [attr.title]="'prep.loadAllHint' | translate">
              {{ 'prep.loadAll' | translate: { max: fmt(s.max) } }}</button>
          } @else if (s.limited) {
            <span class="scope-text">{{ 'prep.capped' | translate: { max: fmt(s.max) } }}</span>
          }
          @if (s.twin; as t) {
            <mat-checkbox class="twin" [checked]="options().twin" [disabled]="loading()" (change)="setTwin($event.checked)"
                          [matTooltip]="'prep.twinHint' | translate" [attr.title]="'prep.twinHint' | translate">
              {{ 'prep.twin' | translate: { games: fmt(t.games) } }}</mat-checkbox>
          }
        </section>
      }
      @if (loading() && options().all) { <p class="hint" role="status">{{ 'prep.loadingAll' | translate: { max: fmt(scope()?.max ?? 0) } }}</p> }
      <div class="prep-card-scope">
        <lh-player-card [inline]="true" [note]="'prep.sources' | translate" />
        @if (scope(); as s) {
          @if (s.accountSearch) { <app-prep-accounts [playerId]="s.id" (changed)="reloadCard()" /> }
        }
      </div>
    </div>
  `,
})
export class PrepPlayerComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly cardApi = inject(PrepCardApi);
  private readonly prep = inject(PrepApiService);
  private readonly translate = inject(TranslateService);
  private readonly card = viewChild.required(PlayerCardComponent);

  readonly scope = this.cardApi.scope;
  readonly options = this.cardApi.options;
  private readonly id = signal<number | null>(null);
  /** Lädt die Karte gerade (auch nach „alle laden" oder dem Zwilling). */
  readonly loading = computed(() => this.card().loading());
  readonly backQuery = computed(() => (this.prep.lastQuery() ? { q: this.prep.lastQuery() } : {}));

  constructor() {
    loadCardStyles(inject(DOCUMENT));
    let rendered = false;
    afterNextRender(() => {
      rendered = true;
      this.open();
    });
    this.route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(p => {
      const id = Number(p.get('id'));
      this.id.set(Number.isInteger(id) && id > 0 ? id : null);
      this.cardApi.options.set({ all: false, twin: false });
      this.cardApi.scope.set(null);
      if (rendered) this.open();
    });
  }

  /** Nach dem Übernehmen eines Kontos: die Karte frisch (das Konto steht dann dort). */
  reloadCard(): void {
    void this.card().reloadCard();
  }

  loadAll(): void {
    this.set({ ...this.options(), all: true });
  }

  setTwin(twin: boolean): void {
    this.set({ ...this.options(), twin });
  }

  private set(o: PrepOptions): void {
    this.cardApi.options.set(o);
    this.open();
  }

  private open(): void {
    const id = this.id();
    if (id !== null) void this.card().open(String(id), null, null, null);
  }

  /** „seit …" im Ladestand: das PGN-Datum der API („2003.02.15") über den Datumshelfer der Karte in der Sprache der
   *  Oberfläche („Feb 15, 2003" / „15.02.2003"), wie die Daten in der Karte selbst (Englisch-Nachprüfung e1). */
  sinceText(d: string | null | undefined): string {
    return cardDate(d, cardLang(this.translate));
  }

  /** Zahlen in der Schreibweise der gewählten Sprache („3.000" / „3,000"). */
  fmt(n: number): string {
    return n.toLocaleString(this.translate.currentLang() || 'en');
  }
}
