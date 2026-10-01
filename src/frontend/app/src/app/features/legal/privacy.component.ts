import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
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
        <mat-card-header><mat-card-title>{{ 'legal.privacy.title' | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <p class="muted">{{ 'legal.privacy.updated' | translate }}</p>
          @if (kind === 'kidhub') {
            <!-- Kinderseite: das Wichtigste in einfacher Sprache vorneweg, die ausfuehrliche Fassung fuer Eltern
                 darunter (Art. 12 Abs. 1 DSGVO; Codereview F7-003). -->
            <p>{{ 'legal.privacy.kidIntro' | translate }}</p>
            <h4>{{ 'legal.privacy.kidTitle' | translate }}</h4>
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

          <h4>{{ 'legal.privacy.controllerTitle' | translate }}</h4>
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
            <h4>{{ 'legal.privacy.leagueTitle' | translate }}</h4>
            <p>{{ 'legal.privacy.leagueIntro' | translate }}</p>
            <ul>
              <li>{{ 'legal.privacy.leagueSources' | translate }}</li>
              <li>{{ 'legal.privacy.leagueData' | translate }}</li>
              <li>{{ 'legal.privacy.leagueBasis' | translate }}</li>
              <li>{{ 'legal.privacy.leagueRecipients' | translate }}</li>
              <li>{{ 'legal.privacy.leagueRetention' | translate }}</li>
              <li>{{ 'legal.privacy.leagueObjection' | translate }}</li>
            </ul>
          }

          <h4>{{ 'legal.privacy.dataTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.dataIntro' | translate }}</p>
          <ul>
            <li>{{ 'legal.privacy.dataAccount' | translate }}</li>
            <li>{{ 'legal.privacy.dataProfile' | translate }}</li>
            <li>{{ 'legal.privacy.dataUsage' | translate }}</li>
            <li>{{ 'legal.privacy.dataTechnical' | translate }}</li>
            <li>{{ 'legal.privacy.dataIpCountry' | translate }}</li>
            <li>{{ 'legal.privacy.dataScoresheet' | translate }}</li>
          </ul>

          <h4>{{ 'legal.privacy.purposesTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.purposes' | translate }}</p>

          <h4>{{ 'legal.privacy.thirdTitle' | translate }}</h4>
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
          <h4>{{ 'legal.privacy.aiTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.aiScoresheet' | translate }}</p>
          <p>{{ 'legal.privacy.aiLocal' | translate }}</p>

          <!-- Discord-Bot (Codereview S4-008): Stand ab schach-bot v2.83.14 — DM-Log (core/dm_log.py: ein- und ausgehend,
               300 Zeichen, ?dl=-Token maskiert, 30 Tage, /dm-log nur Admins/Moderatoren), Spiel-Status nur fuer
               /motivation-Abonnenten (commands/motivation.py, nach ES nur die Laenge der DM), Befehlsprotokoll
               (core/command_log.py) und Puzzle-Reaktionen in den Monats-Indizes schach-bot-logs-* / schach-bot-events-*.
               ENTWURF (Betreiber): fuer diese Indizes gibt es keine Loeschfrist (docs/log-retention.md nimmt sie bewusst
               aus) — sobald eine eingerichtet ist, hier nennen. Der KI-Chat per DM (commands/chat.py, Anthropic) ist ohne
               CLAUDE_API_KEY aus (Prod: nicht gesetzt); wird er eingeschaltet, gehoert er in diesen Abschnitt. -->
          <h4>{{ 'legal.privacy.botTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.botIntro' | translate }}</p>
          <ul>
            <li>{{ 'legal.privacy.botDmLog' | translate }}</li>
            <li>{{ 'legal.privacy.botActivity' | translate }}</li>
            <li>{{ 'legal.privacy.botLogs' | translate }}</li>
          </ul>

          <h4>{{ 'legal.privacy.storageTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.storage' | translate }}</p>

          <h4>{{ 'legal.privacy.retentionTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.retention' | translate }} <a routerLink="/account-deletion">/account-deletion</a>.</p>

          <h4>{{ 'legal.privacy.rightsTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.rights' | translate }}</p>

          <h4>{{ 'legal.privacy.contactTitle' | translate }}</h4>
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
    h4 { margin: 1.25rem 0 0.25rem; color: #90caf9; }
    a { color: #90caf9; }
    .muted { color: #bdbdbd; font-size: 0.85rem; }
    .back { margin-top: 1.5rem; }
  `]
})
export class PrivacyComponent {
  /** Kontakt und Impressum je Oberflaeche (KidHub: eigene Adresse, kein Impressum). */
  readonly site = inject(LEGAL_SITE);
  readonly kind = this.site.kind ?? 'rookhub';
  /** „Zurueck" dorthin, wo man herkam; direkt aufgerufen zur Anmeldung (KidHub: Startseite). */
  readonly back = legalBackLink('legal.privacy.back');
}
