import { Component, AfterViewInit, ChangeDetectionStrategy, ElementRef, OnDestroy, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { REPCHECK_CHROME_URL, REPCHECK_FIREFOX_URL } from '../../core/community';

/**
 * Ausführliche Hilfe-/Anleitungsseite. Route: /help (offen, kein Login nötig).
 *
 * Die Seitenstruktur (Reihenfolge + Icon je Abschnitt) lebt hier als `SECTIONS`,
 * die Texte ausschließlich in i18n unter `help.s.<id>.t` (Titel) und
 * `help.s.<id>.p` (Array von Absätzen). So bleibt die Seite voll lokalisierbar;
 * fehlende Sprachen fallen wie im Rest der App automatisch auf `en` zurück.
 */
interface HelpSection { id: string; icon: string; }

/**
 * Welcher Hilfe-Abschnitt erklärt welchen Menüeintrag — je Schlüssel aus `MenuRegistry.Items`
 * (Backend, `Services/Accounts/MenuRegistry.cs`). Ohne diese Zuordnung erschienen neue Menüpunkte
 * (Punktepartie, Partien, Rekonstruieren, Aufgabenblätter …) ohne ein Wort auf /help
 * (Codereview UX-011). `MenuRegistryTests.EveryMenuItem_HasHelpSection` verlangt für JEDEN
 * Registry-Schlüssel einen Eintrag hier; das Spec prüft, dass jedes Ziel ein Abschnitt ist.
 */
export const MENU_HELP: Readonly<Record<string, string>> = {
  'dashboard': 'welcome',
  'repertoires': 'repertoires',
  'tournaments': 'tournaments',
  'tournament-calendar': 'tournaments',
  'friends': 'friends',
  'puzzles': 'puzzles',
  'favorites': 'puzzles',
  'worksheets': 'worksheets',
  'training-goals': 'trainingGoals',
  'analysis': 'analysis',
  'guess': 'guess',
  'games': 'games',
  'reconstruct': 'reconstruct',
  'scoresheet': 'games',
  'remembered': 'chessable',
  'weekly': 'weekly',
  'courses': 'courses',
  'catalog': 'courses',
  'leaderboards': 'leaderboards',
  'stats': 'stats',
  'chessable': 'chessable',
  'install': 'offline',
  'help': 'welcome',
};

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-help',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, RouterModule, TranslatePipe],
  template: `
    <div class="help-container">
      <header class="help-header">
        <h1>{{ 'help.title' | translate }}</h1>
        <p class="subtitle">{{ 'help.subtitle' | translate }}</p>
      </header>

      <!-- UI-Sweep 2026-10-11 (d2-rk-help-inhalt-vorlauf): bis 1279 px zugeklappt („Inhalt · 27 Themen ▾“) — offen schob
           es den ersten Abschnitt am Handy auf 181 % der Hoehe; ab 1280 px klebende Spalte links neben den Abschnitten. -->
      <!-- Runde z1 (z1-rk-help-toc-verborgen): als klebende Spalte scrollt das Verzeichnis in sich; reicht es unter den
           Rand, blendet es unten aus (.more) — vorher endete es am Laptop glatt nach 13 von 27 Themen. -->
      <nav class="help-toc" [class.more]="tocMore()" [attr.aria-label]="'help.tocTitle' | translate" #tocNav
           (scroll)="updateTocMore()">
        <details [open]="tocOpen()" (toggle)="tocOpen.set(tocFold.open)" #tocFold>
          <summary id="help-toc">
            <h2>{{ 'help.tocTitle' | translate }}</h2>
            <span class="toc-count">{{ 'help.tocCount' | translate: { count: sections.length } }}</span>
            <mat-icon class="chev" aria-hidden="true">expand_more</mat-icon>
          </summary>
          <ul>
            @for (s of sections; track s.id) {
              <li>
                <!-- Echter Link (href = /help#id): per Tab erreichbar, als Link angesagt, kopierbar (UX-012). -->
                <a [routerLink]="[]" [fragment]="s.id" (click)="onTocClick($event, s.id)">
                  <span class="toc-icon">{{ s.icon }}</span>
                  <span>{{ 'help.s.' + s.id + '.t' | translate }}</span>
                </a>
              </li>
            }
          </ul>
        </details>
      </nav>

      <!-- UI-Sweep 2026-10-11 (d2-rk-help-seitenlaenge): Abschnitte zugeklappt mit Titel und erster Zeile — offen war die
           Seite am Handy 16 Bildschirme lang. Der Rest steht mit hidden="until-found" im DOM: die Suche im Browser
           findet ihn und klappt den Abschnitt auf (beforematch). -->
      <div class="help-sections">
        @for (s of sections; track s.id) {
          @let paras = asParagraphs('help.s.' + s.id + '.p' | translate: textParams);
          @let open = isOpen(s.id);
          <mat-card [id]="s.id" class="help-section" [class.open]="open" tabindex="-1">
            <mat-card-header>
              <h2 mat-card-title class="sec-title">
                <button type="button" class="sec-toggle" [attr.aria-expanded]="open" [attr.aria-controls]="s.id + '-more'"
                        (click)="toggle(s.id)">
                  <span class="sec-icon">{{ s.icon }}</span>
                  <span class="sec-name">{{ 'help.s.' + s.id + '.t' | translate }}</span>
                  <mat-icon class="chev" aria-hidden="true">expand_more</mat-icon>
                </button>
              </h2>
            </mat-card-header>
            <mat-card-content>
              @if (paras.length) {
                <p class="lead" [class.teaser]="!open" [innerHTML]="linkify(paras[0])" (click)="expand(s.id)"></p>
              }
              <div class="more" [id]="s.id + '-more'" [attr.hidden]="open ? null : 'until-found'" (beforematch)="expand(s.id)">
                @for (p of paras.slice(1); track $index) {
                  <p [innerHTML]="linkify(p)"></p>
                }
                <button type="button" class="back-top" (click)="scrollTop()">
                  <mat-icon>arrow_upward</mat-icon>{{ 'help.backToTop' | translate }}
                </button>
              </div>
            </mat-card-content>
          </mat-card>
        }
      </div>
    </div>
  `,
  styles: [`
    .help-container { max-width: 860px; margin: 0 auto; padding: 1.5rem 1rem 3rem; }
    .help-header { text-align: center; margin-bottom: 1rem; }
    .help-header h1 { margin: 0 0 0.25rem; }
    .subtitle { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0; }

    .help-toc { margin-bottom: 1rem; }
    .help-toc summary { display: flex; align-items: center; gap: 8px; min-height: 44px; padding: 0 8px;
                        border-radius: 6px; cursor: pointer; list-style: none;
                        background: color-mix(in srgb, currentColor 6%, transparent); }
    .help-toc summary::-webkit-details-marker { display: none; }
    .help-toc summary:hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    .help-toc h2 { font-size: 1rem; text-transform: uppercase; letter-spacing: 0.05em; margin: 0;
                   color: color-mix(in srgb, currentColor 70%, transparent); }
    .toc-count { flex: 1; color: color-mix(in srgb, currentColor 60%, transparent); font-size: 0.9rem; }
    .toc-count::before { content: '· ' / ''; }
    .chev { transition: transform .15s; }
    details[open] > summary .chev, .help-section.open .sec-toggle .chev { transform: rotate(180deg); }
    .help-toc ul { list-style: none; padding: 0; margin: 0.5rem 0 0;
                   display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 4px; }
    .help-toc a { display: flex; align-items: center; gap: 8px; padding: 6px 8px; min-height: 44px;
                  box-sizing: border-box; border-radius: 6px; cursor: pointer; color: inherit;
                  text-decoration: none; }
    .help-toc a:hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    .toc-icon { width: 1.4em; text-align: center; }

    .help-section { margin-bottom: 0.75rem; scroll-margin-top: 80px; }
    /* Kopf = Aufklapper ueber die ganze Breite (≥ 44 px); 12 px Luft bis zum ersten Absatz (d2-rk-help-head-gap). */
    .help-section mat-card-header { padding-bottom: 12px; }
    .sec-title { margin: 0; width: 100%; }
    .sec-toggle { display: flex; align-items: center; gap: 0.5rem; width: 100%; min-height: 44px; padding: 0;
                  border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .sec-name { flex: 1; }
    .sec-toggle .chev { color: color-mix(in srgb, currentColor 55%, transparent); }
    .help-section:not(.open) mat-card-content { padding-bottom: 4px; }
    /* Lange Adressen (Linktext) duerfen umbrechen — sonst machte EINE URL die ganze Seite am Handy
       breiter als den Bildschirm (Codereview UX-013: 222 px seitlicher Ueberlauf). Absatz hoechstens 68ch ≈ 75 Zeichen
       je Zeile (d2-rk-help-line; vorher 105–115). */
    mat-card-content p { line-height: 1.55; margin: 0 0 0.75rem; overflow-wrap: anywhere; max-width: 68ch; }
    .teaser { display: -webkit-box; -webkit-line-clamp: 1; -webkit-box-orient: vertical; overflow: hidden;
              cursor: pointer; color: color-mix(in srgb, currentColor 65%, transparent); }

    /* Trefferflaeche ≥ 44 px (UX-012); der Text bleibt buendig mit den Absaetzen (negativer Rand). */
    .back-top { display: inline-flex; align-items: center; gap: 4px; cursor: pointer;
                min-height: 44px; padding: 0 12px 0 8px; margin: 0 0 0 -8px;
                border: 0; border-radius: 6px; background: none; font: inherit;
                font-size: 0.8rem; color: color-mix(in srgb, currentColor 55%, transparent); }
    .back-top:hover { color: inherit; background: color-mix(in srgb, currentColor 10%, transparent); }
    .back-top mat-icon { font-size: 1rem; width: 1rem; height: 1rem; }

    /* Grosser Bildschirm (ab 1280 px): Verzeichnis als klebende Spalte links, Abschnitte rechts — nutzt die Breite,
       statt das Verzeichnis ueber den Inhalt zu stellen (d2-rk-help-inhalt-vorlauf). */
    @media (min-width: 1280px) {
      .help-container { max-width: 1160px; display: grid; grid-template-columns: 260px minmax(0, 1fr);
                        gap: 0 24px; align-items: start; }
      .help-header { grid-column: 1 / -1; }
      .help-toc { position: sticky; top: 72px; max-height: calc(100vh - 88px); overflow-y: auto; margin: 0; }
      .help-toc ul { grid-template-columns: 1fr; gap: 0; }
      /* Mehr darunter: unterer Rand blendet aus (z1-rk-help-toc-verborgen). */
      .help-toc.more { -webkit-mask-image: linear-gradient(to bottom, #000 calc(100% - 28px), transparent);
                       mask-image: linear-gradient(to bottom, #000 calc(100% - 28px), transparent); }
    }
    /* Mit Maus dichtere Zeilen (30 statt 44 px): am Laptop passen fast alle 27 Themen, gross alle; Touch behaelt 44 px. */
    @media (min-width: 1280px) and (pointer: fine) {
      .help-toc a { min-height: 30px; padding: 4px 8px; }
    }
    @media (prefers-reduced-motion: reduce) { .chev { transition: none; } }
  `]
})
export class HelpComponent implements AfterViewInit, OnDestroy {
  constructor(private route: ActivatedRoute) {}

  /** Dekorator statt viewChild(): die Specs bauen die Komponente auch ohne Injektionskontext (`new HelpComponent`). */
  @ViewChild('tocNav') private tocNav?: ElementRef<HTMLElement>;
  private tocObserver?: ResizeObserver;
  /** Reicht das (in sich scrollende) Verzeichnis unter seinen sichtbaren Rand? Dann blendet es unten aus. */
  readonly tocMore = signal(false);

  /** Prueft, ob unter dem sichtbaren Teil des Verzeichnisses noch Eintraege liegen (Groesse, Aufklappen, Scrollen). */
  updateTocMore(): void {
    const nav = this.tocNav?.nativeElement;
    this.tocMore.set(!!nav && nav.scrollHeight - nav.scrollTop - nav.clientHeight > 1);
  }

  ngOnDestroy(): void {
    this.tocObserver?.disconnect();
  }

  /** Deep-Link wie /help#extension: nach dem Render zum Abschnitt scrollen. */
  ngAfterViewInit(): void {
    const nav = this.tocNav?.nativeElement;
    if (nav && typeof ResizeObserver === 'function') {
      this.tocObserver = new ResizeObserver(() => this.updateTocMore());
      this.tocObserver.observe(nav);
      const list = nav.querySelector('ul');
      if (list) this.tocObserver.observe(list);
    }
    const fragment = this.route.snapshot.fragment;
    if (fragment) {
      setTimeout(() => this.scrollTo(fragment), 0);
    }
  }

  /** Reihenfolge = die Gruppen des ☰-Menüs (Training, Analyse & Sammlung, Community, Konto). */
  readonly sections: HelpSection[] = [
    { id: 'welcome', icon: '\u{1F44B}' },
    { id: 'account', icon: '\u{1F511}' },
    // Training
    { id: 'puzzles', icon: '\u{265F}' },
    { id: 'endless', icon: '\u{267E}' },
    { id: 'daily', icon: '\u{1F4C5}' },
    { id: 'courses', icon: '\u{1F4DA}' },
    { id: 'weekly', icon: '\u{1F4F0}' },
    { id: 'trainingGoals', icon: '\u{1F3AF}' },
    { id: 'repertoires', icon: '\u{1F5C2}' },
    { id: 'guess', icon: '\u{1F914}' },
    { id: 'worksheets', icon: '\u{1F5A8}' },
    // Analyse & Sammlung
    { id: 'analysis', icon: '\u{1F52C}' },
    { id: 'games', icon: '\u{1F4DD}' },
    { id: 'reconstruct', icon: '\u{1F9E0}' },
    // Community
    { id: 'friends', icon: '\u{1F91D}' },
    { id: 'tournaments', icon: '\u{1F3C6}' },
    { id: 'leaderboards', icon: '\u{1F947}' },
    { id: 'discord', icon: '\u{1F4AC}' },
    // Konto
    { id: 'profile', icon: '\u{1F464}' },
    { id: 'stats', icon: '\u{1F4C8}' },
    { id: 'chessable', icon: '\u{1F4D6}' },
    { id: 'offline', icon: '\u{1F4F2}' },
    { id: 'settings', icon: '\u{1F3A8}' },
    { id: 'tokens', icon: '\u{1F50C}' },
    { id: 'extension', icon: '\u{1F9E9}' },
    { id: 'privacy', icon: '\u{1F512}' },
    { id: 'feedback', icon: '\u{1F41E}' },
  ];

  /**
   * Platzhalter in den Absätzen: die Store-Adressen der Erweiterung stehen EINMAL im Code statt als
   * Literal in vier Sprachdateien (dort stand Firefox mit festem Sprachkürzel, Codereview F5-015).
   */
  readonly textParams = { chromeUrl: REPCHECK_CHROME_URL, firefoxUrl: REPCHECK_FIREFOX_URL };

  /** Verzeichnis offen? Erst ab 1280 px (dort als Spalte daneben), schmaler zugeklappt (d2-rk-help-inhalt-vorlauf). */
  readonly tocOpen = signal(typeof matchMedia !== 'function' || matchMedia('(min-width: 1280px)').matches);
  /** Aufgeklappte Abschnitte — anfangs keiner (d2-rk-help-seitenlaenge); Sprung oder #anker klappt das Ziel auf. */
  private readonly openIds = signal<ReadonlySet<string>>(new Set());

  isOpen(id: string): boolean { return this.openIds().has(id); }

  expand(id: string): void {
    if (!this.isOpen(id)) this.openIds.update(s => new Set(s).add(id));
  }

  toggle(id: string): void {
    this.openIds.update(s => {
      const next = new Set(s);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  /** Der `translate`-Pipe liefert für ein JSON-Array das Array zurück; defensiv normalisieren. */
  asParagraphs(value: unknown): string[] {
    if (Array.isArray(value)) return value as string[];
    return value ? [String(value)] : [];
  }

  /** `[Linktext](https://…)` ODER eine nackte http(s)-Adresse (abschließende Satzzeichen bleiben draußen). */
  private static readonly LINK_RE = /\[([^\]\n]+)\]\((https?:\/\/[^\s<)]+)\)|(https?:\/\/[^\s<]+[^\s<.,;:!?)\]])/g;

  /**
   * Wandelt Links in einem Absatz in klickbare Anker: `[Linktext](https://…)` mit sprechendem Text
   * (lange Store-Adressen sind als Linktext unlesbar, UX-013) und nackte http(s)-URLs wie bisher.
   * Der Text wird zuerst HTML-escaped (kein Markup aus i18n durchlassen), dann werden die Links
   * durch Anker ersetzt; das Ergebnis bindet das Template per [innerHTML] (Angular sanitisiert,
   * behält aber a[href][target]). Daher kein DomSanitizer/bypass nötig.
   */
  linkify(text: string): string {
    const escaped = (text ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
    return escaped.replace(
      HelpComponent.LINK_RE,
      (_match, label: string | undefined, labelUrl: string | undefined, bareUrl: string | undefined) => {
        const url = labelUrl ?? bareUrl ?? '';
        return `<a href="${url}" target="_blank" rel="noopener noreferrer">${label ?? url}</a>`;
      },
    );
  }

  /** Klick im Inhaltsverzeichnis: der RouterLink setzt die Adresse (/help#id), gescrollt wird hier. Mit
   *  Strg/Umschalt/Mittelklick öffnet der Browser den Link in einem neuen Tab — dann bleibt diese Seite stehen. */
  onTocClick(event: MouseEvent, id: string): void {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    this.scrollTo(id);
  }

  /** Zum Abschnitt scrollen (aufgeklappt) und den Fokus mitnehmen — sonst ginge Tab nach dem Sprung im
   *  Inhaltsverzeichnis weiter statt im Abschnitt (die Karte trägt dafür tabindex="-1"). */
  scrollTo(id: string): void {
    if (this.sections.some(s => s.id === id)) this.expand(id);
    const el = document.getElementById(id);
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    el?.focus({ preventScroll: true });
  }

  /** Nach oben — und der Fokus zurück vors Inhaltsverzeichnis, damit Tab dort weitergeht. */
  scrollTop(): void {
    window.scrollTo({ top: 0, behavior: 'smooth' });
    document.getElementById('help-toc')?.focus({ preventScroll: true });
  }
}
