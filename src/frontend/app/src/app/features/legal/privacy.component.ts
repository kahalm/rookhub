import { AfterViewInit, Component, ChangeDetectionStrategy, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { LEGAL_SITE, legalBackLink } from './legal-site';
import { LEGAL_PAGE_STYLES } from './legal-page.styles';

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
    <div class="legal-container" [class.legal-kid]="kind === 'kidhub'">
      <mat-card>
        <mat-card-header><h1 mat-card-title>{{ 'legal.privacy.title' | translate }}</h1></mat-card-header>
        <mat-card-content>
          <p class="muted">{{ 'legal.privacy.updated' | translate }}</p>
          <!-- Inhaltsverzeichnis (UX-057): am Handy ist die Seite drei Bildschirmhoehen lang. Gesprungen wird per Klick
               (scrollIntoView + Fokus auf die Ueberschrift), nicht ueber den Router — KidHub/LeagueHub/ClubHub scrollen
               bei jeder Navigation nach oben (scrollPositionRestoration 'top'). -->
          <!-- UI-Sweep 2026-10-11 (d2-t-privacy-toc-mobile, d2-l-datenschutz-lang): am Handy zugeklappt — offen fuellten
               die 11–12 Eintraege den ganzen ersten Bildschirm. Runde z1 (z1-rk-privacy-toc-offen): auch am Laptop
               (unter 900 px Hoehe) zugeklappt — offen begann der Text erst bei 70 % der Hoehe; offen nur auf grossen
               Bildschirmen, und aufgeklappt immer zweispaltig. -->
          <nav class="toc" aria-labelledby="privacy-toc-title" #tocNav>
            <details class="toc-fold" [open]="tocOpen()" (toggle)="tocOpen.set(tocFold.open)" #tocFold>
              <summary class="toc-title" id="privacy-toc-title">{{ 'legal.privacy.tocTitle' | translate }} ({{ toc.length }})</summary>
              <ul>
                @for (s of toc; track s.id) {
                  <li><a [href]="tocHref(s.id)" (click)="jump($event, s.id)">{{ s.title | translate }}</a></li>
                }
              </ul>
            </details>
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
          } @else if (kind === 'leaguehub') {
            <!-- Einleitung je Oberflaeche (UI-Sweep 2026-10-10, x-privacy-intro): LeagueHub stellte sich als
                 „RookHub ist eine Schach-Trainingsplattform (Puzzles, Endlosmodus …)“ vor. -->
            <p>{{ 'legal.privacy.introLeagueHub' | translate }}</p>
          } @else if (kind === 'clubhub') {
            <p>{{ 'legal.privacy.introClubHub' | translate }}</p>
          } @else if (kind === 'turnier') {
            <!-- UI-Sweep 2026-10-11 (r2-t-privacy-intro): die Turnierseite begann mit RookHubs Trainingsplattform. -->
            <p>{{ 'legal.privacy.introTurnier' | translate }}</p>
          } @else {
            <p>{{ 'legal.privacy.intro' | translate }}</p>
          }

          <h2 id="privacy-controller" tabindex="-1">{{ 'legal.privacy.controllerTitle' | translate }}</h2>
          <!-- Der Verantwortliche steht HIER mit der Kontaktadresse der Oberflaeche; Name/Anschrift nennt der Betreiber
               bewusst nicht (Entscheidung 2026-09-30). UI-Sweep 2026-10-11 (r2-rk-privacy-operator): auch mit Impressum —
               der fruehere Verweis „der im Impressum genannte Betreiber" fuehrte ins Leere, dort steht nur der Kontakt. -->
          <p>{{ 'legal.privacy.controllerNamed' | translate }}<br>
            <a [href]="'mailto:' + site.contactEmail">{{ site.contactEmail }}</a></p>

          @if (kind === 'turnier') {
            <!-- Auf der Turnierseite gleich nach dem Verantwortlichen: das ist, was sie verarbeitet (r2-t-privacy-intro). -->
            <h2 id="privacy-tournament" tabindex="-1">{{ 'legal.privacy.tournamentTitle' | translate }}</h2>
            <p>{{ 'legal.privacy.tournamentIntro' | translate }}</p>
            <ul>
              <li>{{ 'legal.privacy.tournamentData' | translate }}</li>
              <li>{{ 'legal.privacy.tournamentRetention' | translate }}</li>
            </ul>
          }

          @if (kind === 'clubhub') {
            <!-- ClubHub: die Kartei der Kinder und Jugendlichen (UI-Sweep 2026-10-10, x-privacy-intro) — Inhalt nach dem
                 Ist-Stand der Oberflaeche und des Servers (Haupt-CLAUDE.md „ClubHub“); Rechtstext, vom Betreiber freizugeben. -->
            <h2 id="privacy-club" tabindex="-1">{{ 'legal.privacy.clubTitle' | translate }}</h2>
            <p>{{ 'legal.privacy.clubIntro' | translate }}</p>
            <ul>
              <li>{{ 'legal.privacy.clubData' | translate }}</li>
              <li>{{ 'legal.privacy.clubContacts' | translate }}</li>
              <li>{{ 'legal.privacy.clubAttendance' | translate }}</li>
              <li>{{ 'legal.privacy.clubAccess' | translate }}</li>
              <li>{{ 'legal.privacy.clubPhotos' | translate }}</li>
              <li>{{ 'legal.privacy.clubLink' | translate }}</li>
              <li>{{ 'legal.privacy.clubRetention' | translate }}</li>
            </ul>
          }

          @if (kind === 'leaguehub') {
            <!-- LeagueHub verarbeitet Daten von Ligaspielern ohne Konto und gibt sie ueber Teilen-Links weiter:
                 Informationspflicht nach Art. 14 DSGVO (Codereview F7-006). -->
            <h2 id="privacy-league" tabindex="-1">{{ 'legal.privacy.leagueTitle' | translate }}</h2>
            <p>{{ 'legal.privacy.leagueIntro' | translate }}</p>
            @if (site.leagueClub?.(); as club) { <p>{{ 'legal.privacy.leagueClub' | translate: { club } }}</p> }
            <ul>
              <!-- UI-Sweep 2026-10-11 (d2-l-datenschutz-lang): die Quellen als Unterliste statt ~30 Zeilen am Stueck;
                   Wortlaut unveraendert, nur an den Strichpunkten geteilt. -->
              <li>{{ 'legal.privacy.leagueSourcesTitle' | translate }}
                <ul class="sub">
                  <li>{{ 'legal.privacy.leagueSourcesPairings' | translate }}</li>
                  <li>{{ 'legal.privacy.leagueSourcesGames' | translate }}</li>
                  <li>{{ 'legal.privacy.leagueSourcesClub' | translate }}</li>
                  <li>{{ 'legal.privacy.leagueSourcesOnline' | translate }}</li>
                </ul>
              </li>
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
          @if (kind !== 'turnier') {
            <h2 id="privacy-tournament" tabindex="-1">{{ 'legal.privacy.tournamentTitle' | translate }}</h2>
            <p>{{ 'legal.privacy.tournamentIntro' | translate }}</p>
            <ul>
              <li>{{ 'legal.privacy.tournamentData' | translate }}</li>
              <li>{{ 'legal.privacy.tournamentRetention' | translate }}</li>
            </ul>
          }

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
          @if (kind !== 'kidhub') {
            <!-- Ruecksprung zum Inhaltsverzeichnis (d2-l-datenschutz-lang): EIN klebender Knopf unten rechts, sobald das
                 Verzeichnis oben aus dem Bild ist. KidHub hat kein Verzeichnis (die Abschnitte sind dort selbst
                 Aufklapper). Runde z1 (z1-rk-privacy-inhalt-knopf): die Leiste steht HINTER dem letzten Abschnitt und
                 haelt dort ihre eigene Hoehe frei — am Ende liegt der Knopf unter dem Text statt auf der Kontaktzeile;
                 der Knopf ist ein rundes 44-px-„↑" am rechten Rand (Beschriftung fuer Vorleser), ab 1200 px steht er
                 neben der Karte statt ueber dem Text. -->
            <div class="to-toc-bar">
              @if (tocAway()) {
                <a class="to-toc" [href]="tocHref('privacy-toc-title')" (click)="toToc($event)"
                   [attr.aria-label]="'legal.privacy.tocTitle' | translate" [title]="'legal.privacy.tocTitle' | translate">
                  <span aria-hidden="true">↑</span>
                </a>
              }
            </div>
          }
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [LEGAL_PAGE_STYLES, `
    /* Theme-Token statt festem Hellblau/-grau (F7-015, wie die Anmeldemaske): #90caf9 hatte im hellen Modus — KidHub
       immer, RookHub/LeagueHub auf Wunsch — 1,75:1 auf Weiss, #bdbdbd 1,9:1. */
    /* Abschnitte als h2 unter dem h1-Titel (UX-057: vorher kein h1 und alles h4) — Aussehen wie vorher. */
    h2 { margin: 1.25rem 0 0.25rem; font-size: 1em; font-weight: bold; color: var(--mat-sys-primary); scroll-margin-top: 1rem; }
    h2:focus:not(:focus-visible) { outline: none; }
    .toc { margin: 0.25rem 0 0.5rem; }
    .toc-title { margin: 0; font-weight: bold; cursor: pointer; min-height: 44px; display: list-item; line-height: 44px; }
    .toc ul { margin: 0 0 0.25rem; padding-left: 1.25rem; }
    /* UI-Sweep 2026-10-11 (r2-c-privacy-toc-bullet): block statt inline-block — sonst sitzt der Listenpunkt bei einem
       umbrechenden Eintrag vor der LETZTEN Zeile (Grundlinie des Inline-Blocks). */
    .toc a { display: block; padding: 4px 0; }
    @media (pointer: coarse) { .toc a { padding: 10px 0; } }
    /* Aufgeklappt zweispaltig — halb so hoch (d2-l-datenschutz-lang; seit z1-rk-privacy-toc-offen in jeder Breite). */
    .toc ul { columns: 2; column-gap: 1.5rem; }
    .toc li { break-inside: avoid; }
    ul.sub { margin: 0.25rem 0 0; }
    /* Klebt unten; am Seitenende ruht die Leiste in ihrer eigenen Hoehe unter dem letzten Absatz (DESIGN 4.6 Regel 10). */
    .to-toc-bar { position: sticky; bottom: 0; height: 60px; margin-right: -8px; display: flex; justify-content: flex-end;
                  align-items: center; pointer-events: none; z-index: 1; }
    .to-toc { pointer-events: auto; display: inline-flex; align-items: center; justify-content: center;
              width: 44px; height: 44px; box-sizing: border-box; border-radius: 50%; text-decoration: none;
              font-size: 1.25rem; font-weight: bold; line-height: 1;
              background: var(--mat-sys-surface-container-high, #2b2f36); color: var(--mat-sys-primary);
              box-shadow: 0 2px 6px rgba(0, 0, 0, 0.3); }
    /* Grosser Bildschirm: neben die Karte in den freien Rand (16 px Kartenpolster + 16 px Abstand), nicht ueber den
       Text; die Leiste braucht dann keine eigene Hoehe. */
    @media (min-width: 1200px) {
      .to-toc-bar { height: 0; bottom: 16px; margin-right: 0; align-items: flex-end; }
      .to-toc { transform: translateX(calc(100% + 32px)); }
    }
    a { color: var(--mat-sys-primary); }
    .muted { color: var(--mat-sys-on-surface-variant); font-size: 0.85rem; }
    .back { margin-top: 1.5rem; }
    .back a::before { content: '← ' / ''; }
  `]
})
export class PrivacyComponent implements AfterViewInit {
  /** Kontakt und Impressum je Oberflaeche (KidHub: eigene Adresse, kein Impressum). */
  readonly site = inject(LEGAL_SITE);
  readonly kind = this.site.kind ?? 'rookhub';
  /** „Zurueck" dorthin, wo man herkam; direkt aufgerufen zur Anmeldung (KidHub: Startseite). */
  readonly back = legalBackLink('legal.privacy.back');
  private readonly location = inject(Location);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tocNav = viewChild<ElementRef<HTMLElement>>('tocNav');

  /** Inhaltsverzeichnis offen? Nur auf grossen Bildschirmen (ab 769 px breit UND 900 px hoch) — am Handy
   *  (d2-t-privacy-toc-mobile) und am Laptop (z1-rk-privacy-toc-offen) zugeklappt. */
  readonly tocOpen = signal(PrivacyComponent.wideScreen());
  /** Ist das Verzeichnis nach oben aus dem Bild gescrollt? Dann steht unten rechts „↑ Inhalt". */
  readonly tocAway = signal(false);

  static readonly TOC_OPEN_QUERY = '(min-width: 769px) and (min-height: 900px)';

  private static wideScreen(): boolean {
    return typeof matchMedia !== 'function' || matchMedia(PrivacyComponent.TOC_OPEN_QUERY).matches;
  }

  ngAfterViewInit(): void {
    const nav = this.tocNav()?.nativeElement;
    if (!nav || typeof IntersectionObserver !== 'function') return;
    const io = new IntersectionObserver(([e]) => this.tocAway.set(!e.isIntersecting && e.boundingClientRect.top < 0));
    io.observe(nav);
    this.destroyRef.onDestroy(() => io.disconnect());
  }

  /** „↑ Inhalt": zum Verzeichnis, aufgeklappt, Fokus auf seinen Kopf. */
  toToc(e: MouseEvent): void {
    if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
    const head = document.getElementById('privacy-toc-title');
    if (!head) return;
    e.preventDefault();
    this.tocOpen.set(true);
    head.scrollIntoView({ block: 'start' });
    head.focus({ preventScroll: true });
  }

  /** Inhaltsverzeichnis — dieselben Abschnitte (Kennung = `id` der h2) in derselben Reihenfolge wie im Template. */
  readonly toc: readonly { id: string; title: string }[] = [
    ...(this.kind === 'kidhub' ? [{ id: 'privacy-kid', title: 'legal.privacy.kidTitle' }] : []),
    { id: 'privacy-controller', title: 'legal.privacy.controllerTitle' },
    ...(this.kind === 'turnier' ? [{ id: 'privacy-tournament', title: 'legal.privacy.tournamentTitle' }] : []),
    ...(this.kind === 'clubhub' ? [{ id: 'privacy-club', title: 'legal.privacy.clubTitle' }] : []),
    ...(this.kind === 'leaguehub' ? [{ id: 'privacy-league', title: 'legal.privacy.leagueTitle' }] : []),
    { id: 'privacy-data', title: 'legal.privacy.dataTitle' },
    { id: 'privacy-purposes', title: 'legal.privacy.purposesTitle' },
    { id: 'privacy-third', title: 'legal.privacy.thirdTitle' },
    { id: 'privacy-ai', title: 'legal.privacy.aiTitle' },
    { id: 'privacy-bot', title: 'legal.privacy.botTitle' },
    ...(this.kind !== 'turnier' ? [{ id: 'privacy-tournament', title: 'legal.privacy.tournamentTitle' }] : []),
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
