import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { LEGAL_SITE, legalBackLink } from './legal-site';

/**
 * Öffentliche Datenschutzerklärung (DSGVO). Route: /privacy — wird auch als
 * Privacy-Policy-URL in der Google Play Console hinterlegt.
 * ENTWURF: rechtlich von Betreiber/Anwalt zu prüfen; Betreiber-Daten im Impressum.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-privacy',
  standalone: true,
  imports: [CommonModule, MatCardModule, RouterModule, TranslatePipe],
  template: `
    <div class="legal-container">
      <mat-card>
        <mat-card-header><h1 mat-card-title>{{ 'legal.privacy.title' | translate }}</h1></mat-card-header>
        <mat-card-content>
          <p class="muted">{{ 'legal.privacy.updated' | translate }}</p>
          <!-- Inhaltsverzeichnis (UX-057): am Handy ist die Seite drei Bildschirmhoehen lang. Gesprungen wird per Klick
               (scrollIntoView + Fokus auf die Ueberschrift), nicht ueber den Router — KidHub/LeagueHub/ClubHub scrollen
               bei jeder Navigation nach oben (scrollPositionRestoration 'top'). -->
          <nav class="toc" aria-labelledby="privacy-toc-title">
            <p class="toc-title" id="privacy-toc-title">{{ 'legal.privacy.tocTitle' | translate }}</p>
            <ul>
              @for (s of toc; track s.id) {
                <li><a [href]="tocHref(s.id)" (click)="jump($event, s.id)">{{ s.title | translate }}</a></li>
              }
            </ul>
          </nav>
          @if (kind === 'kidhub') {
            <!-- Kinderseite: das Wichtigste in einfacher Sprache vorneweg, die ausfuehrliche Fassung fuer Eltern
                 darunter (Art. 12 Abs. 1 DSGVO; Codereview F7-003). -->
            <p>{{ 'legal.privacy.kidIntro' | translate }}</p>
            <h2 id="privacy-kid" tabindex="-1">{{ 'legal.privacy.kidTitle' | translate }}</h2>
            <ul>
              <li>{{ 'legal.privacy.kidDevice' | translate }}</li>
              <li>{{ 'legal.privacy.kidAccount' | translate }}</li>
              <li>{{ 'legal.privacy.kidParents' | translate }}</li>
              <li>{{ 'legal.privacy.kidNoTracking' | translate }}</li>
            </ul>
            <p>{{ 'legal.privacy.kidDetails' | translate }}</p>
          } @else {
            <p>{{ 'legal.privacy.intro' | translate }}</p>
          }

          <h2 id="privacy-controller" tabindex="-1">{{ 'legal.privacy.controllerTitle' | translate }}</h2>
          @if (site.imprint) {
            <p>{{ 'legal.privacy.controller' | translate }} (<a routerLink="/impressum">{{ 'legal.impressum.title' | translate }}</a>).</p>
          } @else {
            <!-- Ohne Impressum steht der Kontakt des Verantwortlichen hier; Name/Anschrift nennt der Betreiber
                 bewusst nicht (Entscheidung 2026-09-30), nur die Kontaktadresse der Oberflaeche. -->
            <p>{{ 'legal.privacy.controllerNamed' | translate }}<br>
              <a [href]="'mailto:' + site.contactEmail">{{ site.contactEmail }}</a></p>
          }

          @if (kind === 'leaguehub') {
            <!-- LeagueHub verarbeitet Daten von Ligaspielern ohne Konto und gibt sie ueber Teilen-Links weiter:
                 Informationspflicht nach Art. 14 DSGVO (Codereview F7-006). -->
            <h2 id="privacy-league" tabindex="-1">{{ 'legal.privacy.leagueTitle' | translate }}</h2>
            <p>{{ 'legal.privacy.leagueIntro' | translate }}</p>
            @if (site.leagueClub?.(); as club) { <p>{{ 'legal.privacy.leagueClub' | translate: { club } }}</p> }
            <ul>
              <li>{{ 'legal.privacy.leagueSources' | translate }}</li>
              <li>{{ 'legal.privacy.leagueData' | translate }}</li>
              <li>{{ 'legal.privacy.leagueBasis' | translate }}</li>
              <li>{{ 'legal.privacy.leagueRecipients' | translate }}</li>
              <li>{{ 'legal.privacy.leagueRetention' | translate }}</li>
              <li>{{ 'legal.privacy.leagueObjection' | translate }}</li>
            </ul>
          }

          <h2 id="privacy-data" tabindex="-1">{{ 'legal.privacy.dataTitle' | translate }}</h2>
          <p>{{ 'legal.privacy.dataIntro' | translate }}</p>
          <ul>
            <li>{{ 'legal.privacy.dataAccount' | translate }}</li>
            <li>{{ 'legal.privacy.dataProfile' | translate }}</li>
            <li>{{ 'legal.privacy.dataUsage' | translate }}</li>
            <li>{{ 'legal.privacy.dataTechnical' | translate }}</li>
            <li>{{ 'legal.privacy.dataIpCountry' | translate }}</li>
            <li>{{ 'legal.privacy.dataScoresheet' | translate }}</li>
          </ul>

          <h2 id="privacy-purposes" tabindex="-1">{{ 'legal.privacy.purposesTitle' | translate }}</h2>
          <p>{{ 'legal.privacy.purposes' | translate }}</p>

          <h2 id="privacy-third" tabindex="-1">{{ 'legal.privacy.thirdTitle' | translate }}</h2>
          <p>{{ 'legal.privacy.thirdIntro' | translate }}</p>
          <ul>
            <li>{{ 'legal.privacy.thirdDiscord' | translate }}</li>
            <li>{{ 'legal.privacy.thirdChessresults' | translate }}</li>
            <li>{{ 'legal.privacy.thirdChesssites' | translate }}</li>
            <li>{{ 'legal.privacy.thirdLogging' | translate }}</li>
            <li>{{ 'legal.privacy.thirdHosting' | translate }}</li>
            <li>{{ 'legal.privacy.thirdAnthropic' | translate }}</li>
            <li>{{ 'legal.privacy.thirdTextLlm' | translate }}</li>
          </ul>

          <!-- Formular-Fotos gehen an Anthropic (USA) — auch anonym ueber LeagueHub-Teilen-Links (Codereview A6-008).
               Nacherzaehlung/Erklaerungen/Roast verlangen IClaudeJsonClient.IsLocal (nie Anthropic), laufen aber auf dem
               DGX Spark eines anderen Betreibers, erreicht ueber das Internet (TextLlm/Embedding) — also KEIN „bleibt bei uns“.
               ENTWURF (Betreiber): den Betreiber dieses Servers namentlich als Empfaenger nennen und die Rolle
               (Auftragsverarbeitung) klaeren; bis dahin nennt der Text ihn nur als Empfaenger-Kategorie. -->
          <h2 id="privacy-ai" tabindex="-1">{{ 'legal.privacy.aiTitle' | translate }}</h2>
          <p>{{ 'legal.privacy.aiScoresheet' | translate }}</p>
          <p>{{ 'legal.privacy.aiLocal' | translate }}</p>

          <!-- Discord-Bot (Codereview S4-008): Stand ab schach-bot v2.83.14 — DM-Log (core/dm_log.py: ein- und ausgehend,
               300 Zeichen, ?dl=-Token maskiert, 30 Tage, /dm-log nur Admins/Moderatoren), Spiel-Status nur fuer
               /motivation-Abonnenten (commands/motivation.py, nach ES nur die Laenge der DM), Befehlsprotokoll
               (core/command_log.py) und Puzzle-Reaktionen in den Monats-Indizes schach-bot-logs-* / schach-bot-events-*.
               ENTWURF (Betreiber): fuer diese Indizes gibt es keine Loeschfrist (docs/log-retention.md nimmt sie bewusst
               aus) — sobald eine eingerichtet ist, hier nennen. Der KI-Chat per DM (commands/chat.py, Anthropic) ist ohne
               CLAUDE_API_KEY aus (Prod: nicht gesetzt); wird er eingeschaltet, gehoert er in diesen Abschnitt. -->
          <h2 id="privacy-bot" tabindex="-1">{{ 'legal.privacy.botTitle' | translate }}</h2>
          <p>{{ 'legal.privacy.botIntro' | translate }}</p>
          <ul>
            <li>{{ 'legal.privacy.botDmLog' | translate }}</li>
            <li>{{ 'legal.privacy.botActivity' | translate }}</li>
            <li>{{ 'legal.privacy.botLogs' | translate }}</li>
          </ul>

          <!-- Turnierdaten (Codereview S3-018): Fristen aus dem RetentionService des Crawlers (chessresults_crawler
               cb8f041, W4s-B23) — CrawlJobs 30 Tage nach Abschluss, PlayerClubs 180 Tage ohne Auffrischung; Turniere und
               Spieler bleiben, weil RookHub per CrawlerTournamentId auf sie verweist. ENTWURF (Betreiber): Rechtsgrundlage
               und Widerspruch fuer Spieler ohne Konto (Art. 14 DSGVO) und eine Frist fuer Turniere/Spieler festlegen. -->
          <h2 id="privacy-tournament" tabindex="-1">{{ 'legal.privacy.tournamentTitle' | translate }}</h2>
          <p>{{ 'legal.privacy.tournamentIntro' | translate }}</p>
          <ul>
            <li>{{ 'legal.privacy.tournamentData' | translate }}</li>
            <li>{{ 'legal.privacy.tournamentRetention' | translate }}</li>
          </ul>

          <h2 id="privacy-storage" tabindex="-1">{{ 'legal.privacy.storageTitle' | translate }}</h2>
          <p>{{ 'legal.privacy.storage' | translate }}</p>

          <h2 id="privacy-retention" tabindex="-1">{{ 'legal.privacy.retentionTitle' | translate }}</h2>
          <p>{{ 'legal.privacy.retention' | translate }} <a routerLink="/account-deletion">{{ 'legal.privacy.retentionLink' | translate }}</a>.</p>

          <h2 id="privacy-rights" tabindex="-1">{{ 'legal.privacy.rightsTitle' | translate }}</h2>
          <p>{{ 'legal.privacy.rights' | translate }}</p>

          <h2 id="privacy-contact" tabindex="-1">{{ 'legal.privacy.contactTitle' | translate }}</h2>
          <p>{{ 'legal.privacy.contact' | translate }}: <a [href]="'mailto:' + site.contactEmail">{{ site.contactEmail }}</a></p>

          <p class="back">
            <!-- Aus der App gekommen: ein Schritt zurueck; direkt aufgerufen: das Ersatzziel (UX-017). -->
            @if (back.history) {
              <a [href]="back.href" (click)="back.go($event)">{{ back.label | translate }}</a>
            } @else {
              <a [routerLink]="back.link">{{ back.label | translate }}</a>
            }
          </p>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .legal-container { padding: 2rem; display: flex; justify-content: center; }
    mat-card { max-width: 760px; width: 100%; }
    /* Theme-Token statt festem Hellblau/-grau (F7-015, wie die Anmeldemaske): #90caf9 hatte im hellen Modus — KidHub
       immer, RookHub/LeagueHub auf Wunsch — 1,75:1 auf Weiss, #bdbdbd 1,9:1. */
    /* Abschnitte als h2 unter dem h1-Titel (UX-057: vorher kein h1 und alles h4) — Aussehen wie vorher. */
    h2 { margin: 1.25rem 0 0.25rem; font-size: 1em; font-weight: bold; color: var(--mat-sys-primary); scroll-margin-top: 1rem; }
    h2:focus:not(:focus-visible) { outline: none; }
    .toc { margin: 0.25rem 0 0.5rem; }
    .toc-title { margin: 0; font-weight: bold; }
    .toc ul { margin: 0.25rem 0 0; padding-left: 1.25rem; }
    .toc a { display: inline-block; padding: 4px 0; }
    @media (pointer: coarse) { .toc a { padding: 10px 0; } }
    a { color: var(--mat-sys-primary); }
    .muted { color: var(--mat-sys-on-surface-variant); font-size: 0.85rem; }
    .back { margin-top: 1.5rem; }
  `]
})
export class PrivacyComponent {
  /** Kontakt und Impressum je Oberflaeche (KidHub: eigene Adresse, kein Impressum). */
  readonly site = inject(LEGAL_SITE);
  readonly kind = this.site.kind ?? 'rookhub';
  /** „Zurueck" dorthin, wo man herkam; direkt aufgerufen zur Anmeldung (KidHub: Startseite). */
  readonly back = legalBackLink('legal.privacy.back');
  private readonly location = inject(Location);

  /** Inhaltsverzeichnis — dieselben Abschnitte (Kennung = `id` der h2) in derselben Reihenfolge wie im Template. */
  readonly toc: readonly { id: string; title: string }[] = [
    ...(this.kind === 'kidhub' ? [{ id: 'privacy-kid', title: 'legal.privacy.kidTitle' }] : []),
    { id: 'privacy-controller', title: 'legal.privacy.controllerTitle' },
    ...(this.kind === 'leaguehub' ? [{ id: 'privacy-league', title: 'legal.privacy.leagueTitle' }] : []),
    { id: 'privacy-data', title: 'legal.privacy.dataTitle' },
    { id: 'privacy-purposes', title: 'legal.privacy.purposesTitle' },
    { id: 'privacy-third', title: 'legal.privacy.thirdTitle' },
    { id: 'privacy-ai', title: 'legal.privacy.aiTitle' },
    { id: 'privacy-bot', title: 'legal.privacy.botTitle' },
    { id: 'privacy-tournament', title: 'legal.privacy.tournamentTitle' },
    { id: 'privacy-storage', title: 'legal.privacy.storageTitle' },
    { id: 'privacy-retention', title: 'legal.privacy.retentionTitle' },
    { id: 'privacy-rights', title: 'legal.privacy.rightsTitle' },
    { id: 'privacy-contact', title: 'legal.privacy.contactTitle' },
  ];

  /** Adresse fuer „im neuen Tab oeffnen": diese Seite mit Sprungmarke (ein blankes `#…` loeste sich gegen
   *  `<base href="/">` auf die Startseite auf). */
  tocHref(id: string): string {
    return `${this.location.path(false).split('#')[0]}#${id}`;
  }

  /** Zum Abschnitt springen und die Ueberschrift fokussieren (Screenreader und Tastatur landen dort). Strg/Mittelklick
   *  (neuer Tab) bleibt beim schlichten Link. */
  jump(e: MouseEvent, id: string): void {
    if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
    const target = document.getElementById(id);
    if (!target) return;
    e.preventDefault();
    target.scrollIntoView({ block: 'start' });
    target.focus({ preventScroll: true });
  }
}
