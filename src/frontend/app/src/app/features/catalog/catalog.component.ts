import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatChipsModule } from '@angular/material/chips';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CatalogService, CatalogItem, CatalogRequest } from './catalog.service';
import { AuthService } from '../../core/auth.service';
import { AdminService, AdminUser, Group } from '../../core/admin.service';
import { SnackbarService } from '../../core/snackbar.service';

@Component({
  selector: 'app-catalog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule,
    MatSelectModule, MatChipsModule, TranslatePipe,
  ],
  template: `
  <div class="catalog">
    <h1>{{ 'catalog.title' | translate }}</h1>

    <!-- Besitzer/Admin: Freigaben + offene Anforderungen -->
    @if (isAdmin) {
      <mat-card class="admin-card">
        <h2>{{ 'catalog.admin.grantsTitle' | translate }}</h2>
        <p class="hint">{{ 'catalog.admin.grantsHint' | translate }}</p>
        <div class="grant-selects">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'catalog.admin.users' | translate }}</mat-label>
            <mat-select multiple [(ngModel)]="grantUserIds">
              @for (u of users; track u.id) { <mat-option [value]="u.id">{{ u.username }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ 'catalog.admin.groups' | translate }}</mat-label>
            <mat-select multiple [(ngModel)]="grantGroupIds">
              @for (g of groups; track g.id) { <mat-option [value]="g.id">{{ g.name }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <button mat-flat-button color="primary" (click)="saveGrants()" [disabled]="savingGrants">
            <mat-icon>save</mat-icon> {{ 'common.save' | translate }}
          </button>
        </div>

        <h2>{{ 'catalog.admin.requestsTitle' | translate }}</h2>
        @if (requests.length === 0) {
          <p class="empty">{{ 'catalog.admin.noRequests' | translate }}</p>
        } @else {
          <div class="req-list">
            @for (r of requests; track r.id) {
              <div class="req-row">
                <span class="req-text">
                  <strong>{{ r.requesterName }}</strong>
                  {{ (r.itemType === 'course' ? 'catalog.type.course' : 'catalog.type.repertoire') | translate }}:
                  {{ r.itemName }}
                </span>
                <span class="req-actions">
                  <button mat-flat-button color="primary" (click)="approve(r)" [disabled]="busyId === r.id">
                    <mat-icon>check</mat-icon> {{ 'catalog.admin.approve' | translate }}
                  </button>
                  <button mat-stroked-button (click)="decline(r)" [disabled]="busyId === r.id">
                    <mat-icon>close</mat-icon> {{ 'catalog.admin.decline' | translate }}
                  </button>
                </span>
              </div>
            }
          </div>
        }
      </mat-card>
    }

    <!-- Viewer: freigegebene Liste -->
    <mat-card>
      <h2>{{ 'catalog.listTitle' | translate }}</h2>
      @if (loading) {
        <p class="empty">…</p>
      } @else if (items.length === 0) {
        <p class="empty">{{ 'catalog.empty' | translate }}</p>
      } @else {
        <div class="item-list">
          @for (i of items; track i.itemType + i.itemId) {
            <div class="item-row">
              <mat-icon class="type-icon">{{ i.itemType === 'course' ? 'menu_book' : 'account_tree' }}</mat-icon>
              <span class="item-name">{{ i.name }}</span>
              <span class="item-owner">{{ i.ownerName }}</span>
              @switch (i.status) {
                @case ('shared') {
                  @if (i.itemType === 'course') {
                    <a mat-stroked-button [routerLink]="['/courses', i.itemId, 'sequential']">
                      <mat-icon>play_arrow</mat-icon> {{ 'catalog.open' | translate }}
                    </a>
                  } @else {
                    <a mat-stroked-button [routerLink]="['/repertoires', i.itemId]">
                      <mat-icon>play_arrow</mat-icon> {{ 'catalog.open' | translate }}
                    </a>
                  }
                }
                @case ('pending') {
                  <mat-chip disabled><mat-icon>hourglass_top</mat-icon> {{ 'catalog.pending' | translate }}</mat-chip>
                }
                @default {
                  <button mat-flat-button color="primary" (click)="requestItem(i)" [disabled]="busyItem === (i.itemType + i.itemId)">
                    <mat-icon>send</mat-icon> {{ 'catalog.request' | translate }}
                  </button>
                }
              }
            </div>
          }
        </div>
      }
    </mat-card>
  </div>
  `,
  styles: [`
    .catalog { max-width: 900px; margin: 24px auto; padding: 0 16px; }
    h2 { margin: 16px 0 8px; }
    .hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0 0 8px; }
    .empty { color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; }
    .grant-selects { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
    .grant-selects mat-form-field { min-width: 220px; }
    .admin-card { margin-bottom: 20px; }
    .req-list, .item-list { display: flex; flex-direction: column; gap: 6px; }
    .req-row, .item-row { display: flex; align-items: center; gap: 12px; padding: 8px 10px;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 8px; }
    .req-text { flex: 1; min-width: 0; }
    .item-name { flex: 1; min-width: 0; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .item-owner { color: color-mix(in srgb, currentColor 55%, transparent); font-size: 0.85rem; }
    .type-icon { color: color-mix(in srgb, currentColor 55%, transparent); }
    mat-chip mat-icon { font-size: 16px; width: 16px; height: 16px; }
  `],
})
export class CatalogComponent implements OnInit {
  private svc = inject(CatalogService);
  private auth = inject(AuthService);
  private adminService = inject(AdminService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  isAdmin = false;
  loading = true;
  items: CatalogItem[] = [];
  requests: CatalogRequest[] = [];
  users: AdminUser[] = [];
  groups: Group[] = [];
  grantUserIds: number[] = [];
  grantGroupIds: number[] = [];
  savingGrants = false;
  busyId: number | null = null;
  busyItem: string | null = null;

  ngOnInit(): void {
    this.isAdmin = this.auth.isAdmin;
    this.loadList();
    if (this.isAdmin) {
      this.svc.getGrants().subscribe(g => { this.grantUserIds = g.userIds; this.grantGroupIds = g.groupIds; });
      this.svc.getRequests().subscribe(r => this.requests = r);
      this.adminService.getUsers('', 1, 500).subscribe(res => this.users = res.items);
      this.adminService.getGroups().subscribe(g => this.groups = g);
    }
  }

  private loadList(): void {
    this.loading = true;
    this.svc.list().subscribe({
      next: items => { this.items = items; this.loading = false; },
      error: () => { this.items = []; this.loading = false; },
    });
  }

  requestItem(i: CatalogItem): void {
    this.busyItem = i.itemType + i.itemId;
    this.svc.request(i.itemType, i.itemId).subscribe({
      next: res => { i.status = (res.status as CatalogItem['status']) || 'pending'; this.busyItem = null;
        this.snackbar.info(this.translate.instant('catalog.requested')); },
      error: () => { this.busyItem = null; this.snackbar.info(this.translate.instant('catalog.requestFailed')); },
    });
  }

  saveGrants(): void {
    this.savingGrants = true;
    this.svc.setGrants({ userIds: this.grantUserIds, groupIds: this.grantGroupIds }).subscribe({
      next: g => { this.grantUserIds = g.userIds; this.grantGroupIds = g.groupIds; this.savingGrants = false;
        this.snackbar.info(this.translate.instant('catalog.admin.grantsSaved')); },
      error: () => { this.savingGrants = false; this.snackbar.info(this.translate.instant('common.error')); },
    });
  }

  approve(r: CatalogRequest): void {
    this.busyId = r.id;
    this.svc.approve(r.id).subscribe({
      next: () => { this.requests = this.requests.filter(x => x.id !== r.id); this.busyId = null;
        this.snackbar.info(this.translate.instant('catalog.admin.approved')); this.loadList(); },
      error: () => { this.busyId = null; this.snackbar.info(this.translate.instant('common.error')); },
    });
  }

  decline(r: CatalogRequest): void {
    this.busyId = r.id;
    this.svc.decline(r.id).subscribe({
      next: () => { this.requests = this.requests.filter(x => x.id !== r.id); this.busyId = null; },
      error: () => { this.busyId = null; this.snackbar.info(this.translate.instant('common.error')); },
    });
  }
}
