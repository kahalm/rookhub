import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface CatalogItem {
  ownerUserId: number;
  ownerName: string;
  itemType: 'course' | 'repertoire';
  itemId: number;
  name: string;
  status: 'none' | 'pending' | 'shared';
}

export interface CatalogGrants {
  userIds: number[];
  groupIds: number[];
}

export interface CatalogRequest {
  id: number;
  requesterUserId: number;
  requesterName: string;
  itemType: 'course' | 'repertoire';
  itemId: number;
  itemName: string;
  status: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private http = inject(HttpClient);

  access(): Observable<{ hasAccess: boolean }> {
    return this.http.get<{ hasAccess: boolean }>('/api/catalog/access');
  }
  list(): Observable<CatalogItem[]> {
    return this.http.get<CatalogItem[]>('/api/catalog');
  }
  request(itemType: 'course' | 'repertoire', itemId: number): Observable<{ status: string }> {
    return this.http.post<{ status: string }>('/api/catalog/request', { itemType, itemId });
  }

  // Besitzer (Admin)
  getGrants(): Observable<CatalogGrants> {
    return this.http.get<CatalogGrants>('/api/catalog/grants');
  }
  setGrants(grants: CatalogGrants): Observable<CatalogGrants> {
    return this.http.put<CatalogGrants>('/api/catalog/grants', grants);
  }
  getRequests(): Observable<CatalogRequest[]> {
    return this.http.get<CatalogRequest[]>('/api/catalog/requests');
  }
  approve(id: number): Observable<void> {
    return this.http.post<void>(`/api/catalog/requests/${id}/approve`, {});
  }
  decline(id: number): Observable<void> {
    return this.http.post<void>(`/api/catalog/requests/${id}/decline`, {});
  }
}
