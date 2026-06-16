import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { InAppNotificationService, AppNotification } from '../../core/in-app-notification.service';
import { notificationText, notificationIcon } from '../../core/notification-text';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

/** Vollständige, paginierte Benachrichtigungs-History (von der Glocke aus „Alle anzeigen"). */
@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatButtonModule, TranslateModule, LoadingSpinnerComponent],
  template: `
    <div class="notif-container">
      <h1>{{ 'notifications.historyTitle' | translate }}</h1>

      @if (loading && items.length === 0) {
        <app-loading-spinner />
      } @else if (items.length === 0) {
        <p class="empty">{{ 'notifications.empty' | translate }}</p>
      } @else {
        <mat-card>
          <mat-card-content class="list">
            @for (n of items; track n.id) {
              <button class="row" [class.unseen]="!n.seen" [class.clickable]="!!n.link" (click)="open(n)">
                <mat-icon class="row-icon">{{ icon(n) }}</mat-icon>
                <span class="row-text">{{ text(n) }}</span>
                <span class="row-date">{{ n.createdAt | date:'short' }}</span>
              </button>
            }
          </mat-card-content>
        </mat-card>

        @if (items.length < total) {
          <div class="more">
            <button mat-stroked-button [disabled]="loading" (click)="loadMore()">
              {{ (loading ? 'common.loading' : 'notifications.loadMore') | translate }}
            </button>
          </div>
        }
        <p class="count">{{ 'notifications.shownOf' | translate:{ shown: items.length, total: total } }}</p>
      }
    </div>
  `,
  styles: [`
    .notif-container { max-width: 760px; margin: 24px auto; padding: 0 16px; }
    .empty { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; padding: 16px 0; }
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
    .row-text { flex: 1; line-height: 1.3; }
    .row-date { flex: 0 0 auto; font-size: 0.78rem; color: color-mix(in srgb, currentColor 55%, transparent); white-space: nowrap; }
    .more { display: flex; justify-content: center; margin: 16px 0 4px; }
    .count { text-align: center; font-size: 0.8rem; color: color-mix(in srgb, currentColor 55%, transparent); }
  `]
})
export class NotificationsComponent implements OnInit {
  items: AppNotification[] = [];
  total = 0;
  loading = false;
  private page = 0;
  private readonly pageSize = 30;

  constructor(
    private notif: InAppNotificationService,
    private translate: TranslateService,
    private router: Router,
  ) {}

  ngOnInit(): void { this.loadMore(); }

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
