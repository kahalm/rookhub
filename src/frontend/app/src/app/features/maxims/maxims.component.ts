import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { map } from 'rxjs';
import { ChessMaxim, ChessMaximSource, ChessMaximsService } from '../../shared/maxims/chess-maxims.service';

/**
 * Seite „Merksätze“ (0.917.0, Wunsch 2026-11-09: „mach irgendwo in RookHub einen Link, wo diese Merksätze aufgelistet sind —
 * jeweils aufklappbar mit Erklärungen + Quellen + Links, sofern der Spieler Zugriff auf die E-Books hat“). Je Kategorie die
 * Sätze als Aufklapp-Tafeln: wann er passt, die Buchstellen mit Zitat; verlinkt nur, was der Server als zugänglich meldet.
 * `?m=<kennung>` klappt einen Satz auf und scrollt hin (Sprung aus dem (i) einer Erklärung).
 */
@Component({
  selector: 'app-maxims',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatCardModule, MatExpansionModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <div class="maxims-page">
      <mat-card class="head-card">
        <h1>{{ 'maxims.title' | translate }}</h1>
        <p class="intro">{{ 'maxims.intro' | translate }}</p>
      </mat-card>
      @if (list(); as all) {
        <p class="count">{{ 'maxims.count' | translate: { count: all.length } }}</p>
        @for (g of groups(); track g.category) {
          <h2>{{ g.category }}</h2>
          <mat-accordion multi>
            @for (m of g.items; track m.id) {
              <mat-expansion-panel [id]="'m-' + m.id" [expanded]="m.id === openId()">
                <mat-expansion-panel-header><mat-panel-title class="text">{{ m.text }}</mat-panel-title></mat-expansion-panel-header>
                <p class="label">{{ 'maxims.when' | translate }}</p>
                <p class="when">{{ m.when }}</p>
                <p class="label">{{ 'maxims.books' | translate }}</p>
                <ul class="sources">
                  @for (s of m.sources; track $index) {
                    <li>
                      @if (s.accessible) {
                        <a [routerLink]="['/ebooks', s.ebookId]" [queryParams]="s.page ? { page: s.page } : {}">{{ s.author }} — <em>{{ s.title }}</em></a>
                      } @else { <span>{{ s.author }} — <em>{{ s.title }}</em></span> }
                      @if (s.year) { <span class="where"> ({{ s.year }})</span> }
                      @if (s.page) { <span class="where"> · {{ 'maxims.page' | translate: { page: s.page } }}</span> }
                      @else if (where(s); as w) { <span class="where"> · {{ w }}</span> }
                      @if (!s.accessible) { <span class="where"> · {{ 'maxims.noAccess' | translate }}</span> }
                      @if (s.quote) { <q>{{ s.quote }}</q> }
                    </li>
                  }
                </ul>
              </mat-expansion-panel>
            }
          </mat-accordion>
        }
      } @else {
        <div class="center"><mat-spinner diameter="40" /></div>
      }
    </div>
  `,
  styles: [`
    .maxims-page { max-width: 860px; margin: 0 auto; padding: 16px; box-sizing: border-box; }
    .head-card { padding: 16px; margin-bottom: 8px; }
    h1 { margin: 0 0 4px; font-size: 1.5rem; }
    .intro { margin: 0; color: var(--mat-sys-on-surface-variant); }
    .count { margin: 8px 0 0; font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    h2 { font-size: 1.05rem; margin: 20px 0 8px; }
    .text { font-weight: 500; white-space: normal; line-height: 1.35; padding: 6px 0; }
    .label { margin: 8px 0 2px; font-size: 0.75rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.04em;
      color: var(--mat-sys-on-surface-variant); }
    .when { margin: 0; line-height: 1.45; }
    .sources { margin: 0; padding-left: 18px; line-height: 1.45; }
    .sources li { margin: 6px 0; overflow-wrap: anywhere; }
    .sources a { color: inherit; }
    .where { color: var(--mat-sys-on-surface-variant); font-size: 0.85rem; }
    q { display: block; font-size: 0.85rem; color: var(--mat-sys-on-surface-variant); }
    .center { display: flex; justify-content: center; padding: 32px; }
  `],
})
export class MaximsComponent {
  private readonly route = inject(ActivatedRoute);
  readonly list = toSignal(inject(ChessMaximsService).all$);
  readonly openId = toSignal(this.route.queryParamMap.pipe(map(q => q.get('m'))), { initialValue: null });
  private readonly scrolled = signal(false);

  /** Kategorien in der Reihenfolge der Liste. */
  readonly groups = computed(() => {
    const out: { category: string; items: ChessMaxim[] }[] = [];
    for (const m of this.list() ?? []) {
      const g = out.find(x => x.category === m.category);
      if (g) g.items.push(m); else out.push({ category: m.category, items: [m] });
    }
    // Sprung aus einer Erklärung: nach dem Rendern zum aufgeklappten Satz scrollen (einmal).
    const id = this.openId();
    if (id && out.length && !this.scrolled()) {
      setTimeout(() => { document.getElementById('m-' + id)?.scrollIntoView({ block: 'center' }); this.scrolled.set(true); });
    }
    return out;
  });

  where(s: ChessMaximSource): string {
    return (s.location ?? '').replace(/;?\s*E-Book ohne Seitenzahlen, bei ca\.\s*(\d+)\s*%/, (_m, p) => ` ~${p} %`).trim();
  }
}
