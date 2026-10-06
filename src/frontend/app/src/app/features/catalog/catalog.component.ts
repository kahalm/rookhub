import { Component, OnInit, inject, ChangeDetectionStrategy, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, of } from 'rxjs';
import { catchError, debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatChipsModule } from '@angular/material/chips';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CatalogService, CatalogItem, CatalogRequest, CatalogGrants, CatalogGrantUser } from './catalog.service';
import { AuthService } from '../../core/auth.service';
import { AdminService, Group } from '../../core/admin.service';
import { FriendsService } from '../../core/friends.service';
import { UserSearchResult } from '../../core/models';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-catalog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule,
    MatSelectModule, MatInputModule, MatTooltipModule, MatChipsModule, TranslatePipe, LoadErrorComponent,
  ],
  template: `
  <div class="catalog">
    <h1>{{ 'catalog.title' | translate }}</h1>

    <!-- Besitzer/Admin: Freigaben + offene Anforderungen -->
    @if (isAdmin) {
      <mat-card class="admin-card">
        <h2>{{ 'catalog.admin.grantsTitle' | translate }}</h2>
        <p class="hint">{{ 'catalog.admin.grantsHint' | translate }}</p>

        <!-- User wie bei „Freunde" suchen (Name, Anzeigename, chess.com, lichess, FIDE, CR) statt einer langen Auswahlliste. -->
        <h3>{{ 'catalog.admin.grantedUsers' | translate }}</h3>
        @if (grantUsers.length === 0) {
          <p class="empty">{{ 'catalog.admin.noGrantedUsers' | translate }}</p>
        } @else {
          <mat-chip-set class="granted-users">
            @for (u of grantUsers; track u.userId) {
              <mat-chip (removed)="removeUser(u.userId)">
                {{ u.username }}@if (u.displayName) { <span class="chip-sub">· {{ u.displayName }}</span> }
                <button matChipRemove [attr.aria-label]="'common.remove' | translate"><mat-icon>cancel</mat-icon></button>
              </mat-chip>
            }
          </mat-chip-set>
        }
        <div class="user-search">
          <mat-form-field appearance="outline" class="search-field">
            <mat-label>{{ 'catalog.admin.searchUsers' | translate }}</mat-label>
            <mat-icon matPrefix>search</mat-icon>
            <input matInput [(ngModel)]="searchQuery" (ngModelChange)="searchInput.next($event)" (keyup.enter)="search()">
          </mat-form-field>
          <button mat-stroked-button (click)="search()" [disabled]="searchQuery.trim().length < 2">{{ 'common.search' | translate }}</button>
        </div>
        @if (searched) {
          @if (searchResults.length === 0) {
            <p class="empty">{{ 'catalog.admin.noResults' | translate }}</p>
          } @else {
            <div class="search-results">
              @for (u of searchResults; track u.userId) {
                <div class="search-row">
                  <span class="search-text">
                    <strong>{{ u.username }}</strong>
                    @if (u.displayName) { <span class="sub">{{ u.displayName }}</span> }
                    @if (chessIdentities(u)) { <span class="sub ids">{{ chessIdentities(u) }}</span> }
                  </span>
                  @if (isGranted(u.userId)) {
                    <mat-icon class="granted-mark">check_circle</mat-icon>
                  } @else {
                    <button mat-icon-button (click)="addUser(u)"
                            [attr.aria-label]="'catalog.admin.add' | translate" [matTooltip]="'catalog.admin.add' | translate">
                      <mat-icon>person_add</mat-icon>
                    </button>
                  }
                </div>
              }
            </div>
          }
        }

        <div class="grant-selects">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'catalog.admin.groups' | translate }}</mat-label>
            <mat-select multiple [(ngModel)]="grantGroupIds">
              @for (g of groups; track g.id) { <mat-option [value]="g.id">{{ g.name }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <button mat-flat-button color="primary" (click)="saveGrants()" [disabled]="savingGrants || !grantsLoaded">
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
      } @else if (loadError) {
        <app-load-error (retry)="loadList()" />
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
    h3 { margin: 12px 0 6px; font-size: 1rem; }
    .granted-users { display: block; margin-bottom: 8px; }
    .chip-sub { opacity: .65; margin-left: 4px; }
    .user-search { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .user-search .search-field { flex: 1; min-width: 220px; margin-bottom: -1.25em; }
    .search-results { display: flex; flex-direction: column; gap: 4px; margin: 12px 0; }
    .search-row { display: flex; align-items: center; gap: 12px; padding: 4px 10px;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 8px; }
    .search-text { flex: 1; min-width: 0; display: flex; flex-wrap: wrap; column-gap: 8px; align-items: baseline; }
    .search-text .sub { color: color-mix(in srgb, currentColor 60%, transparent); font-size: .85rem; }
    .search-text .ids { flex-basis: 100%; }
    .granted-mark { color: var(--mat-sys-primary, #2e7d32); margin: 0 8px; }
    .grant-selects { margin-top: 16px; display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
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
  private friends = inject(FriendsService);
  private destroyRef = inject(DestroyRef);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  isAdmin = false;
  loading = true;
  /** Laden der Liste gescheitert — sonst saehe sie aus wie „nichts freigegeben". */
  loadError = false;
  items: CatalogItem[] = [];
  requests: CatalogRequest[] = [];
  groups: Group[] = [];
  /** Freigegebene User mit Namen — Quelle der Wahrheit fürs Speichern (userIds werden daraus gebildet). */
  grantUsers: CatalogGrantUser[] = [];
  grantGroupIds: number[] = [];
  /** Erst nach erfolgreichem Laden true — setGrants ERSETZT alle Freigaben; ein Speichern auf dem leeren
   *  Stand nach einem Ladefehler entzöge sonst allen Nutzern und Gruppen den Katalog. */
  grantsLoaded = false;
  savingGrants = false;
  busyId: number | null = null;
  busyItem: string | null = null;
  searchQuery = '';
  searchResults: UserSearchResult[] = [];
  /** Erst nach einer Suche true — sonst stünde „Keine Treffer" schon vor der ersten Eingabe da. */
  searched = false;
  readonly searchInput = new Subject<string>();
  private searchNow = new Subject<string>();

  ngOnInit(): void {
    // Besitzer ist, wer catalog.manage hat (Admins immer) — wie der Server seit F5-005.
    this.isAdmin = this.auth.has('catalog.manage');
    this.loadList();
    if (this.isAdmin) {
      this.svc.getGrants().subscribe({
        next: g => { this.applyGrants(g); this.grantsLoaded = true; },
        error: () => this.snackbar.info(this.translate.instant('common.error')),
      });
      this.svc.getRequests().subscribe(r => this.requests = r);
      this.adminService.getGroups().subscribe(g => this.groups = g);
      // Tippen sucht nach kurzer Pause, Enter/Knopf sofort; switchMap verwirft überholte Antworten.
      this.searchInput.pipe(debounceTime(350), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
        .subscribe(q => this.searchNow.next(q));
      this.searchNow.pipe(
        switchMap(q => {
          const t = q.trim();
          if (t.length < 2) return of(null);
          return this.friends.search(t).pipe(catchError(() => {
            this.snackbar.info(this.translate.instant('common.error')); return of(null);
          }));
        }),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe(r => {
        if (r) { this.searchResults = r; this.searched = true; }
        else { this.searchResults = []; this.searched = false; }
      });
    }
  }

  search(): void {
    if (this.searchQuery.trim().length < 2) return;
    this.searchNow.next(this.searchQuery);
  }

  isGranted(userId: number): boolean {
    return this.grantUsers.some(u => u.userId === userId);
  }

  addUser(u: UserSearchResult): void {
    if (this.isGranted(u.userId)) return;
    this.grantUsers = [...this.grantUsers, { userId: u.userId, username: u.username, displayName: u.displayName }]
      .sort((a, b) => a.username.localeCompare(b.username));
  }

  removeUser(userId: number): void {
    this.grantUsers = this.grantUsers.filter(u => u.userId !== userId);
  }

  chessIdentities(u: UserSearchResult): string {
    const parts: string[] = [];
    if (u.chessComUsername) parts.push(`chess.com: ${u.chessComUsername}`);
    if (u.lichessUsername) parts.push(`lichess: ${u.lichessUsername}`);
    if (u.fideId) parts.push(`FIDE: ${u.fideId}`);
    if (u.chessResultsId) parts.push(`CR: ${u.chessResultsId}`);
    return parts.join(' | ');
  }

  private applyGrants(g: CatalogGrants): void {
    // Ältere Server liefern noch keine Namen — dann wenigstens die Id zeigen statt die Freigabe zu verlieren.
    const named = new Map((g.users ?? []).map(u => [u.userId, u]));
    this.grantUsers = g.userIds.map(id => named.get(id) ?? { userId: id, username: `#${id}`, displayName: null });
    this.grantGroupIds = g.groupIds;
  }

  loadList(): void {
    this.loading = true;
    this.loadError = false;
    this.svc.list().subscribe({
      next: items => { this.items = items; this.loading = false; },
      error: () => { this.items = []; this.loading = false; this.loadError = true; },
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
    if (!this.grantsLoaded || this.savingGrants) return;
    this.savingGrants = true;
    this.svc.setGrants({ userIds: this.grantUsers.map(u => u.userId), groupIds: this.grantGroupIds }).subscribe({
      next: g => { this.applyGrants(g); this.savingGrants = false;
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
