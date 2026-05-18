import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { Router } from '@angular/router';

export interface AuthResponse {
  token: string;
  username: string;
  userId: number;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly apiUrl = '/api/auth';
  private currentUserSubject = new BehaviorSubject<AuthResponse | null>(this.getStoredUser());
  currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient, private router: Router) {}

  get isLoggedIn(): boolean {
    return !!this.currentUserSubject.value;
  }

  get token(): string | null {
    return this.currentUserSubject.value?.token ?? null;
  }

  get currentUser(): AuthResponse | null {
    return this.currentUserSubject.value;
  }

  register(username: string, email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/register`, { username, email, password })
      .pipe(tap(res => this.storeUser(res)));
  }

  login(username: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/login`, { username, password })
      .pipe(tap(res => this.storeUser(res)));
  }

  logout(): void {
    localStorage.removeItem('rookhub_user');
    this.currentUserSubject.next(null);
    this.router.navigate(['/login']);
  }

  private storeUser(user: AuthResponse): void {
    localStorage.setItem('rookhub_user', JSON.stringify(user));
    this.currentUserSubject.next(user);
  }

  private getStoredUser(): AuthResponse | null {
    const stored = localStorage.getItem('rookhub_user');
    return stored ? JSON.parse(stored) : null;
  }
}
