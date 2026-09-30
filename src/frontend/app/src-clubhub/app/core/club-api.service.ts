import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Group, GroupInput, GroupRow, LinkCode, LinkState, Member, MemberInput, MemberRow, Progress, SessionDetail, SessionInput } from './club.models';

/**
 * Der Text, den die API für einen abgelehnten Aufruf mitschickt (`{ message }` — für Nutzer geschrieben, z. B. „‚abc‘ ist
 * keine Telefonnummer."), sonst der übergebene Rückfall. Ohne Netz gibt es keinen Rumpf.
 */
export function apiErrorText(err: unknown, fallback: string): string {
  const message = err instanceof HttpErrorResponse && typeof err.error?.message === 'string' ? err.error.message.trim() : '';
  return message || fallback;
}

/** ClubHub-API: Kartei, Gruppen, Anwesenheit, Verknüpfung mit einem Konto. */
@Injectable({ providedIn: 'root' })
export class ClubApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/club';

  /** Die Kartei (soweit der Aufrufer sie sieht). Gesucht wird im Browser — ein Name gehört nicht in eine Adresse. */
  members(archived: boolean): Promise<MemberRow[]> {
    let params = new HttpParams();
    if (archived) params = params.set('archived', true);
    return firstValueFrom(this.http.get<MemberRow[]>(`${this.base}/members`, { params }));
  }

  member(id: number): Promise<Member> {
    return firstValueFrom(this.http.get<Member>(`${this.base}/members/${id}`));
  }

  createMember(input: MemberInput): Promise<Member> {
    return firstValueFrom(this.http.post<Member>(`${this.base}/members`, input));
  }

  updateMember(id: number, input: MemberInput): Promise<Member> {
    return firstValueFrom(this.http.put<Member>(`${this.base}/members/${id}`, input));
  }

  deleteMember(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/members/${id}`));
  }

  addNote(id: number, text: string): Promise<Member> {
    return firstValueFrom(this.http.post<Member>(`${this.base}/members/${id}/notes`, { text }));
  }

  deleteNote(id: number, noteId: number): Promise<Member> {
    return firstValueFrom(this.http.delete<Member>(`${this.base}/members/${id}/notes/${noteId}`));
  }

  createLinkCode(id: number): Promise<LinkCode> {
    return firstValueFrom(this.http.post<LinkCode>(`${this.base}/members/${id}/link-code`, {}));
  }

  unlink(id: number): Promise<Member> {
    return firstValueFrom(this.http.delete<Member>(`${this.base}/members/${id}/link`));
  }

  /** Lernstand aus dem verknüpften Konto; `null` ohne Verknüpfung (204). */
  progress(id: number): Promise<Progress | null> {
    return firstValueFrom(this.http.get<Progress | null>(`${this.base}/members/${id}/progress`));
  }

  groups(): Promise<GroupRow[]> {
    return firstValueFrom(this.http.get<GroupRow[]>(`${this.base}/groups`));
  }

  group(id: number): Promise<Group> {
    return firstValueFrom(this.http.get<Group>(`${this.base}/groups/${id}`));
  }

  createGroup(input: GroupInput): Promise<Group> {
    return firstValueFrom(this.http.post<Group>(`${this.base}/groups`, input));
  }

  updateGroup(id: number, input: GroupInput): Promise<Group> {
    return firstValueFrom(this.http.put<Group>(`${this.base}/groups/${id}`, input));
  }

  deleteGroup(id: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/groups/${id}`));
  }

  addTrainer(id: number, username: string): Promise<Group> {
    return firstValueFrom(this.http.post<Group>(`${this.base}/groups/${id}/trainers`, { username }));
  }

  removeTrainer(id: number, userId: number): Promise<Group> {
    return firstValueFrom(this.http.delete<Group>(`${this.base}/groups/${id}/trainers/${userId}`));
  }

  addGroupMember(id: number, memberId: number): Promise<Group> {
    return firstValueFrom(this.http.post<Group>(`${this.base}/groups/${id}/members/${memberId}`, {}));
  }

  removeGroupMember(id: number, memberId: number): Promise<Group> {
    return firstValueFrom(this.http.delete<Group>(`${this.base}/groups/${id}/members/${memberId}`));
  }

  /** Die Einheit dieses Tages anlegen oder ersetzen — je Gruppe und Tag gibt es eine. */
  saveSession(groupId: number, input: SessionInput): Promise<SessionDetail> {
    return firstValueFrom(this.http.post<SessionDetail>(`${this.base}/groups/${groupId}/sessions`, input));
  }

  /** Die Einheit eines Tages; `null`, wenn es an dem Tag keine gibt (204). */
  sessionByDate(groupId: number, date: string): Promise<SessionDetail | null> {
    return firstValueFrom(this.http.get<SessionDetail | null>(`${this.base}/groups/${groupId}/sessions/by-date/${encodeURIComponent(date)}`));
  }

  session(sessionId: number): Promise<SessionDetail> {
    return firstValueFrom(this.http.get<SessionDetail>(`${this.base}/sessions/${sessionId}`));
  }

  deleteSession(sessionId: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/sessions/${sessionId}`));
  }

  linkState(): Promise<LinkState> {
    return firstValueFrom(this.http.get<LinkState>(`${this.base}/link`));
  }

  redeem(code: string): Promise<LinkState> {
    return firstValueFrom(this.http.post<LinkState>(`${this.base}/link`, { code }));
  }

  selfUnlink(): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/link`));
  }
}
