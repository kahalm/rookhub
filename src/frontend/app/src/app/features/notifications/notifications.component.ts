import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { InAppNotificationService, AppNotification } from '../../core/in-app-notification.service';
import {
  notificationText, notificationIcon, notificationCategory,
  NotificationCategory, NOTIFICATION_CATEGORIES,
} from '../../core/notification-text';
import { PushService } from '../../core/push.service';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

const HIDDEN_STORAGE_KEY = 'rookhub_notifications_hidden_categories';

/** Vollständige, paginierte Benachrichtigungs-History (von der Glocke aus „Alle anzeigen"). */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatIconModule, MatButtonModule, MatChipsModule, MatSlideToggleModule, TranslatePipe, LoadingSpinnerComponent],
  template: `
    <div class="notif-container">
      <h1>{{ 'notifications.historyTitle' | translate }}</h1>

      <mat-card class="push-card">
        <mat-card-content>
          <div class="push-head">
            <mat-icon>notifications_active</mat-icon>
            <span class="push-title">{{ 'notifications.push.title' | translate }}</span>
          </div>
          @if (!pushSupported) {
            <p class="push-hint">{{ 'notifications.push.unsupported' | translate }}</p>
          } @else if (!pushPublicKey) {
            <p class="push-hint">{{ 'notifications.push.notConfigured' | translate }}</p>
          } @else {
            <p class="push-hint">{{ 'notifications.push.hint' | translate }}</p>
            @if (pushDenied) {
              <p class="push-hint push-denied">{{ 'notifications.push.denied' | translate }}</p>
            }
            <div class="push-cats">
              @for (cat of pushCategories; track cat) {
                <mat-slide-toggle color="primary"
                    [checked]="isPushOn(cat)" [disabled]="pushBusy || pushDenied"
                    (change)="togglePush(cat, $event.checked)">
                  {{ ('notifications.category.' + cat) | translate }}
                </mat-slide-toggle>
              }
            </div>
          }
        </mat-card-content>
      </mat-card>

      @if (loading && items.length === 0) {
        <app-loading-spinner />
      } @else if (items.length === 0) {
        <p class="empty">{{ 'notifications.empty' | translate }}</p>
      } @else {
        @if (availableCategories.length > 0) {
          <div class="filter-bar">
            <span class="filter-label">{{ 'notifications.filter.label' | translate }}</span>
            <mat-chip-set aria-label="notifications.filter.label">
              @for (cat of availableCategories; track cat) {
                <mat-chip class="filter-chip" [class.chip-off]="isHidden(cat)"
                          (click)="toggleCategory(cat)"
                          [attr.aria-pressed]="!isHidden(cat)"
                          [attr.title]="(isHidden(cat) ? 'notifications.filter.showTooltip' : 'notifications.filter.hideTooltip') | translate">
                  <mat-icon matChipAvatar>{{ isHidden(cat) ? 'visibility_off' : 'visibility' }}</mat-icon>
                  {{ ('notifications.category.' + cat) | translate }} ({{ counts[cat] || 0 }})
                </mat-chip>
              }
            </mat-chip-set>
            @if (hidden.size > 0) {
              <button mat-button class="filter-reset" (click)="showAll()">
                {{ 'notifications.filter.showAll' | translate }}
              </button>
            }
          </div>
        }

        @if (visibleItems.length === 0) {
          <p class="empty">{{ 'notifications.filter.allHidden' | translate }}</p>
        } @else {
          <mat-card>
            <mat-card-content class="list">
              @for (n of visibleItems; track n.id) {
                <button class="row" [class.unseen]="!n.seen" [class.clickable]="!!n.link" (click)="open(n)">
                  <mat-icon class="row-icon">{{ icon(n) }}</mat-icon>
                  <span class="row-text">{{ text(n) }}</span>
                  <span class="row-date">{{ n.createdAt | date:'short' }}</span>
                </button>
              }
            </mat-card-content>
          </mat-card>
        }

        @if (items.length < total) {
          <div class="more">
            <button mat-stroked-button [disabled]="loading" (click)="loadMore()">
              {{ (loading ? 'common.loading' : 'notifications.loadMore') | translate }}
            </button>
          </div>
        }
        <p class="count">{{ 'notifications.shownOf' | translate:{ shown: visibleItems.length, total: total } }}</p>
      }
    </div>
  `,
  styles: [`
    .notif-container { max-width: 760px; margin: 24px auto; padding: 0 16px; }
    .empty { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; padding: 16px 0; }
    .push-card { margin-bottom: 16px; }
    .push-head { display: flex; align-items: center; gap: 8px; font-weight: 600; margin-bottom: 6px; }
    .push-hint { font-size: 0.85rem; color: color-mix(in srgb, currentColor 65%, transparent); margin: 0 0 10px; }
    .push-hint.push-denied { color: #c62828; }
    .push-cats { display: flex; flex-wrap: wrap; gap: 8px 20px; }
    .filter-bar { display: flex; align-items: center; flex-wrap: wrap; gap: 8px 12px; margin: 0 0 12px; }
    .filter-label { font-size: 0.85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .filter-chip { cursor: pointer; user-select: none; }
    .filter-chip.chip-off { opacity: 0.45; text-decoration: line-through; }
    .filter-reset { min-width: 0; padding: 0 8px; font-size: 0.8rem; }
    .list { display: flex; flex-direction: column; padding: 4px 0; }
    .row { display: flex; align-items: center; gap: 12px; width: 100%; text-align: left;
           background: none; border: none; color: inherit; font: inherit; padding: 10px 12px;
           border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent); }
    .row:last-child { border-bottom: none; }
    .row.clickable { cursor: pointer; }
    .row.clickable:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .row.unseen { font-weight: 600; }
    .row.unseen .row-icon { color: var(--mat-sys-primary, #3f51b5); }
    .row-icon { color: color-mix(in srgb, currentColor 45%, transparent); flex: 0 0 auto; }
    .row-text { flex: 1; min-width: 0; line-height: 1.3; overflow-wrap: anywhere; }
    .row-date { flex: 0 0 auto; font-size: 0.78rem; color: color-mix(in srgb, currentColor 55%, transparent); white-space: nowrap; }
    .more { display: flex; justify-content: center; margin: 16px 0 4px; }
    .count { text-align: center; font-size: 0.8rem; color: color-mix(in srgb, currentColor 55%, transparent); }
  `]
})
export class NotificationsComponent implements OnInit {
  items: AppNotification[] = [];
  total = 0;
  loading = false;
  /** Ausgeblendete Kategorien (persistiert in localStorage). */
  hidden = new Set<NotificationCategory>();
  private page = 0;
  private readonly pageSize = 30;

  // ----- Push -----
  /** Bereiche im Push-Panel (Admin-Bereich nur für Admins). */
  pushCategories: NotificationCategory[] = [];
  /** VAPID-Key (null = serverseitig nicht konfiguriert). */
  pushPublicKey: string | null = null;
  /** Aktuell für Push aktivierte Bereiche. */
  private pushEnabled = new Set<NotificationCategory>();
  pushBusy = false;

  constructor(
    private notif: InAppNotificationService,
    private translate: TranslateService,
    private router: Router,
    private push: PushService,
    private auth: AuthService,
    private snackbar: SnackbarService,
  ) {
    this.hidden = readHiddenCategories();
    // „admin"-Bereich nur für Admins anbieten.
    this.pushCategories = NOTIFICATION_CATEGORIES.filter(c => c !== 'admin' || this.auth.isAdmin);
  }

  ngOnInit(): void {
    this.loadMore();
    if (this.pushSupported) this.push.getConfig().subscribe({
      next: cfg => {
        this.pushPublicKey = cfg.publicKey;
        this.pushEnabled = new Set(cfg.enabledCategories as NotificationCategory[]);
      },
      error: () => {},
    });
  }

  get pushSupported(): boolean { return this.push.supported; }
  get pushDenied(): boolean { return this.push.permissionDenied; }
  isPushOn(cat: NotificationCategory): boolean { return this.pushEnabled.has(cat); }

  /** Einen Bereich für Push ein-/ausschalten: beim ersten Aktivieren Browser-Berechtigung anfordern +
   *  Subscription anlegen; beim Deaktivieren des letzten Bereichs die Subscription wieder abmelden. */
  async togglePush(cat: NotificationCategory, checked: boolean): Promise<void> {
    if (this.pushBusy || !this.pushPublicKey) return;
    const next = new Set(this.pushEnabled);
    if (checked) next.add(cat); else next.delete(cat);
    this.pushBusy = true;
    try {
      if (checked && this.pushEnabled.size === 0) {
        // Erstes Aktivieren → Berechtigung + Subscription (kann bei Ablehnung werfen).
        await this.push.ensureSubscribed(this.pushPublicKey);
      }
      const eff = await this.push.setPreferences([...next]).toPromise();
      this.pushEnabled = new Set((eff?.categories ?? [...next]) as NotificationCategory[]);
      if (this.pushEnabled.size === 0) await this.push.removeSubscription();
    } catch {
      this.snackbar.warn(this.translate.instant('notifications.push.enableError'));
    } finally {
      this.pushBusy = false;
    }
  }

  /** Nur Kategorien anzeigen, für die wir tatsächlich Einträge geladen haben. */
  get availableCategories(): NotificationCategory[] {
    const seen = new Set<NotificationCategory>();
    for (const n of this.items) seen.add(notificationCategory(n.type));
    return NOTIFICATION_CATEGORIES.filter(c => seen.has(c));
  }

  /** Aktuell sichtbare Einträge (nach Kategoriefilter). */
  get visibleItems(): AppNotification[] {
    if (this.hidden.size === 0) return this.items;
    return this.items.filter(n => !this.hidden.has(notificationCategory(n.type)));
  }

  /** Zähler je Kategorie über die geladenen Einträge (für die Chip-Klammer). */
  get counts(): Record<NotificationCategory, number> {
    const c: Record<NotificationCategory, number> = {
      courses: 0, friends: 0, puzzles: 0, messages: 0, tournaments: 0, admin: 0, other: 0,
    };
    for (const n of this.items) c[notificationCategory(n.type)]++;
    return c;
  }

  isHidden(cat: NotificationCategory): boolean { return this.hidden.has(cat); }

  toggleCategory(cat: NotificationCategory): void {
    if (this.hidden.has(cat)) this.hidden.delete(cat); else this.hidden.add(cat);
    persistHiddenCategories(this.hidden);
  }

  showAll(): void {
    this.hidden.clear();
    persistHiddenCategories(this.hidden);
  }

  loadMore(): void {
    this.loading = true;
    this.notif.history(this.page + 1, this.pageSize).subscribe({
      next: res => {
        this.items = [...this.items, ...res.items];
        this.total = res.total;
        this.page++;
        this.loading = false;
      },
      error: () => { this.loading = false; },
    });
  }

  open(n: AppNotification): void {
    if (!n.seen) { this.notif.markSeen(n.id).subscribe({ error: () => {} }); n.seen = true; }
    if (n.link) this.router.navigateByUrl(n.link);
  }

  text(n: AppNotification): string { return notificationText(this.translate, n); }
  icon(n: AppNotification): string { return notificationIcon(n); }
}

/** Liest die zuletzt ausgeblendeten Kategorien aus localStorage (still, wenn kaputt/nicht vorhanden). */
export function readHiddenCategories(): Set<NotificationCategory> {
  try {
    const raw = localStorage.getItem(HIDDEN_STORAGE_KEY);
    if (!raw) return new Set();
    const arr = JSON.parse(raw) as unknown;
    if (!Array.isArray(arr)) return new Set();
    const valid = new Set<NotificationCategory>(NOTIFICATION_CATEGORIES);
    return new Set(arr.filter((x): x is NotificationCategory => typeof x === 'string' && valid.has(x as NotificationCategory)));
  } catch { return new Set(); }
}

/** Schreibt den ausgeblendeten Zustand zurück; Fehler still schlucken (Storage voll / Safari-Private-Mode). */
export function persistHiddenCategories(hidden: Set<NotificationCategory>): void {
  try { localStorage.setItem(HIDDEN_STORAGE_KEY, JSON.stringify([...hidden])); } catch { /* ignore */ }
}
