import { DestroyRef, Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService, AuthResponse } from './auth.service';
import { accountHomeUrl, leagueHubUrl, partnerSiteUrl, rookHubUrlForLeagueHub } from './partner-site';

/**
 * Der Sprung zwischen RookHub und der Turnierseite.
 *
 * <p>Beide liegen auf verschiedenen Origins und teilen den `localStorage` NICHT — wer hier
 * angemeldet ist, ist es drueben nicht, obwohl dasselbe Konto dahintersteht. Der Sprung holt
 * deshalb einen Einmal-Code (60 s, einmal einloesbar) und haengt ihn an die Ziel-URL; die
 * Gegenseite tauscht ihn beim Start gegen ihre eigene Anmeldung. Wo es die geteilte Anmeldung (unten)
 * gibt, tauscht der Server ihn nur gegen das Cookie DESSELBEN Kontos — das Holen des Codes legt es an;
 * ein fremder Code, den jemand einem anderen Browser unterschiebt, meldet dort niemanden an (F1-008).</p>
 *
 * <p>Ohne Anmeldung — und waehrend einer Impersonation — wird schlicht ohne Code gesprungen: dann
 * landet man drueben auf der oeffentlichen Seite bzw. der Anmeldemaske.</p>
 *
 * <p>Der Code deckt aber nur den KLICK im Menue ab. Wer die Turnierseite direkt aufruft, nachdem
 * er sich vorhin in RookHub angemeldet hat, bringt keinen mit — dafuer gibt es die GETEILTE
 * Anmeldung: der Server legt beim Anmelden ein Cookie auf der gemeinsamen Elterndomaene ab, und
 * <c>consumeIncoming</c> tauscht es beim Start gegen eine eigene Anmeldung (siehe
 * <c>SharedSessionService</c> auf der Serverseite).</p>
 *
 * <p>Eine so UEBERNOMMENE Anmeldung endet mit der geteilten (siehe {@link verifyAdoptedSession}):
 * wer sich in RookHub abmeldet, loescht das Cookie — und KidHub, Turnierseite und LeagueHub, die
 * es beim Start gegen ein eigenes 30-Tage-Token getauscht haben, melden sich beim naechsten Abgleich
 * ebenfalls ab, statt den naechsten Nutzer am Geraet im fremden Konto weiterarbeiten zu lassen.</p>
 */
@Injectable({ providedIn: 'root' })
export class HandoffService {
  private http = inject(HttpClient);
  private auth = inject(AuthService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  /** Parametername in der Ziel-URL. */
  static readonly Param = 'h';

  /** Tausch des geteilten Cookies — derselbe Pfad, den das Cookie traegt (`Path=/api/auth/rh-session`, N6-001). */
  static readonly SharedSessionUrl = '/api/auth/rh-session';

  /** Einloesen eines Uebergabe-Codes — UNTER dem Pfad des Cookies, damit der Browser es mitschickt: der Server
   *  tauscht den Code nur, wenn das geteilte Cookie zum selben Konto gehoert (kein Login-CSRF, Codereview F1-008). */
  static readonly ExchangeUrl = '/api/auth/rh-session/handoff';

  /** Mindestabstand zwischen zwei Abgleichen beim Zurueckkehren in den Tab — hin- und herschalten
   *  fragt nicht jedes Mal (der Endpunkt teilt sich sein IP-Fenster mit dem Abmelden). */
  static readonly RecheckGapMs = 15_000;

  private watching = false;
  private lastCheck = 0;

  /** Adresse der Schwesterseite, oder `null` (dann keinen Sprung anbieten). */
  get partnerUrl(): string | null { return partnerSiteUrl(); }

  /** Springt zur Schwesterseite — angemeldet, wenn es geht. `path` ohne fuehrenden Schraegstrich. */
  async jump(path = ''): Promise<void> {
    const base = this.partnerUrl;
    if (base) await this.jumpTo(base, path);
  }

  /** Adresse von LeagueHub zu diesem RookHub, oder `null`. */
  get leagueHubUrl(): string | null { return leagueHubUrl(); }

  /** Springt nach LeagueHub (derselbe Weg wie zur Schwesterseite: Einmal-Code, drüben eingelöst). */
  async jumpToLeagueHub(path = ''): Promise<void> {
    const base = this.leagueHubUrl;
    if (base) await this.jumpTo(base, path);
  }

  /** Der Rückweg: RookHub zu diesem LeagueHub, oder `null`. */
  get rookHubUrl(): string | null { return rookHubUrlForLeagueHub(); }

  /** Springt von LeagueHub nach RookHub (Einmal-Code, drüben eingelöst — z. B. auf eine eben abgelegte Partie). */
  async jumpToRookHub(path = ''): Promise<void> {
    const base = this.rookHubUrl;
    if (base) await this.jumpTo(base, path);
  }

  /** RookHub zu dieser Oberflaeche (dort liegt das Konto), oder `null` — auf RookHub selbst und ohne bekannte Adresse. */
  get accountHomeUrl(): string | null { return accountHomeUrl(); }

  /** Springt von KidHub, LeagueHub, ClubHub oder der Turnierseite nach RookHub (Einmal-Code wie oben) — z. B. zur
   *  Karte „Konto loeschen", die es nur dort gibt (Codereview UX-023). */
  async jumpToAccountHome(path = ''): Promise<void> {
    const base = this.accountHomeUrl;
    if (base) await this.jumpTo(base, path);
  }

  private async jumpTo(base: string, path: string): Promise<void> {
    const target = `${base}/${path}`.replace(/([^:]\/)\/+/g, '$1');

    if (!this.auth.isLoggedIn) { this.go(target); return; }
    // Waehrend einer Impersonation gar nicht erst fragen: eingeloest wird der Code drueben zu einer
    // GEWOEHNLICHEN Anmeldung des Zielkontos (30 Tage, ohne imp-Claim, samt geteiltem Cookie) — alle
    // Impersonations-Sperren waeren weg. Der Server lehnt ohnehin mit 403 ab (A1-001); drueben steht
    // dann die Anmeldemaske. Geprueft wird das Token-Merkmal, wie es auch der Server tut (seit F1-005
    // liest `isImpersonating` dasselbe, ohne zusaetzlich das Admin-Backup zu verlangen).
    if (this.auth.currentUser?.impersonating) { this.go(target); return; }
    try {
      const res = await firstValueFrom(this.http.post<{ code: string }>('/api/auth/handoff', {}));
      const sep = target.includes('?') ? '&' : '?';
      this.go(`${target}${sep}${HandoffService.Param}=${encodeURIComponent(res.code)}`);
    } catch {
      // Kein Code zu bekommen ist kein Grund, den Sprung zu verweigern — drueben steht dann
      // die Anmeldemaske, und das ist immer noch besser als ein toter Knopf.
      this.go(target);
    }
  }

  /** Die eigentliche Navigation — eine eigene Methode, damit die Specs den Seitenwechsel abfangen koennen. */
  private go(url: string): void { location.href = url; }

  /**
   * Loest einen mitgebrachten Code ein (beim App-Start aufzurufen) und raeumt ihn aus der URL —
   * er ist verbraucht, und im Verlauf hat er nichts verloren. `true`, wenn dadurch eine Anmeldung
   * entstanden ist.
   */
  async consumeIncoming(): Promise<boolean> {
    this.watchAdoptedSession();
    const url = new URL(location.href);
    const code = url.searchParams.get(HandoffService.Param);

    if (code) {
      url.searchParams.delete(HandoffService.Param);
      history.replaceState({}, '', url.pathname + (url.search || '') + url.hash);
    }

    if (this.auth.isLoggedIn) {                  // schon angemeldet: Code einfach verfallen lassen
      void this.verifyAdoptedSession();
      return false;
    }

    if (code) {
      try {
        const res = await firstValueFrom(
          this.http.post<AuthResponse>(HandoffService.ExchangeUrl, { code }));
        // Der Tausch legt — wo eingerichtet — auch das geteilte Cookie an. Nur wenn es danach
        // wirklich steht, haengt diese Anmeldung an ihm: ohne Elterndomaene (localhost, Dev ueber
        // HTTP) antwortet der Abgleich immer 204, und das hiesse sonst „abmelden".
        const shared = await this.fetchSharedSession();
        this.auth.adoptSession(shared?.userId === res.userId ? { ...res, adopted: true } : res);
        this.leaveLoginMask();
        return true;
      } catch {
        return false;                            // abgelaufen/verbraucht → Anmeldemaske
      }
    }

    return this.adoptSharedSession();
  }

  /**
   * Ohne Code: besteht auf der Schwesterseite schon eine Anmeldung? Nachweis ist das Cookie auf
   * der gemeinsamen Elterndomaene — es ist <c>HttpOnly</c>, hier also nicht lesbar; nur der Server
   * kann sagen, ob es taugt. „Keine" ist der Normalfall (nicht angemeldet, oder es gibt gar keine
   * gemeinsame Domaene) und bleibt deshalb still: der Server antwortet dann 204 ohne Rumpf, eine
   * aeltere API noch mit 401 — beides endet hier in `false`.
   *
   * <p>Pfad `rh-session` statt `session` (Codereview N6-001): das Cookie traegt seitdem genau diesen
   * Pfad und geht nicht mehr an JEDEN `/api/auth/*`-Aufruf der uebrigen Hosts unter der Elterndomaene
   * (Cal.com, RCT, Lernkompass …). Eine API ohne den neuen Pfad antwortet 404 — endet hier ebenfalls
   * in `false`.</p>
   */
  async adoptSharedSession(): Promise<boolean> {
    if (this.auth.isLoggedIn) return false;
    // Ein Abmelden ohne Netz hat die geteilte Anmeldung noch nicht beendet: erst nachholen, NICHT
    // uebernehmen — sonst meldete dieser Start genau die Anmeldung wieder an, die eben beendet wurde,
    // womoeglich fuer den naechsten Nutzer am Geraet (Codereview F1-004).
    if (this.auth.sessionEndPending) {
      await this.auth.flushSessionEnd();
      return false;
    }
    try {
      const res = await firstValueFrom(this.http.post<AuthResponse | null>(HandoffService.SharedSessionUrl, {}));
      if (!res) return false;
      this.auth.adoptSession({ ...res, adopted: true });
      this.leaveLoginMask();
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Gleicht eine UEBERNOMMENE Anmeldung mit der geteilten ab — beim Start und beim Zurueckkehren in
   * den Tab. `true`, wenn sich dadurch die Anmeldung geaendert hat.
   *
   * <p>Ohne diesen Abgleich erreichte ein Abmelden die anderen Oberflaechen nie: sie fragen das Cookie
   * nur, solange sie NICHT angemeldet sind, und behielten ihr eigenes Token bis zu 30 Tage. Eine
   * Lehrerin meldete sich am Vereins-PC in RookHub ab, KidHub blieb als sie angemeldet — die naechsten
   * Kinder spielten in ihrem Konto.</p>
   *
   * <ul>
   *   <li>204 (Cookie weg oder entwertet): lokal abmelden.</li>
   *   <li>Cookie eines ANDEREN Kontos (inzwischen hat sich dort jemand anderes angemeldet): dessen
   *       Anmeldung uebernehmen, wie es ein frischer Start auch taete — vorher aber lokal aufraeumen
   *       wie beim Abmelden (`AuthService.switchToSharedSession`), sonst erbte das neue Konto
   *       Offline-Inhalte und Endless-Laeufe des vorigen. Nicht abmelden — das loeschte das Cookie
   *       des anderen gleich mit.</li>
   *   <li>Keine Antwort (offline, 429, Server weg): nichts tun, die Anmeldung gilt weiter.</li>
   * </ul>
   * <p>Eine selbst angemeldete Sitzung (ohne `adopted`) fragt gar nicht erst.</p>
   */
  async verifyAdoptedSession(): Promise<boolean> {
    const user = this.auth.currentUser;
    if (!user?.adopted || (typeof navigator !== 'undefined' && navigator.onLine === false)) return false;
    this.lastCheck = Date.now();
    const res = await this.fetchSharedSession();
    if (res === undefined || this.auth.currentUser?.token !== user.token) return false; // inzwischen umgemeldet
    if (res === null) {
      this.auth.logout();
      return true;
    }
    if (res.userId === user.userId) return false;
    this.auth.switchToSharedSession(res);
    void this.router.navigateByUrl('/');
    return true;
  }

  /** Die Anmeldung hinter dem geteilten Cookie: `null` bei 204 (keins, oder es taugt nicht mehr),
   *  `undefined`, wenn keine brauchbare Antwort kam (offline, 429, Server weg, aeltere API mit 401). */
  private async fetchSharedSession(): Promise<AuthResponse | null | undefined> {
    try {
      return await firstValueFrom(this.http.post<AuthResponse | null>(HandoffService.SharedSessionUrl, {}));
    } catch {
      return undefined;
    }
  }

  /** Abgleich beim Zurueckkehren in den Tab oder ins Fenster — einmal je App eingerichtet, beim Abbau
   *  wieder entfernt. `focus` zusaetzlich zu `visibilitychange`: zwei nebeneinander offene Fenster
   *  bleiben beide sichtbar, der Wechsel zwischen ihnen loest nur `focus` aus. Beide zusammen (Tab
   *  anklicken) fragen dank Mindestabstand nur einmal. */
  private watchAdoptedSession(): void {
    if (this.watching || typeof document === 'undefined') return;
    this.watching = true;
    const onVisible = () => {
      if (document.visibilityState !== 'visible') return;
      if (Date.now() - this.lastCheck < HandoffService.RecheckGapMs) return;
      void this.verifyAdoptedSession();
    };
    document.addEventListener('visibilitychange', onVisible);
    window.addEventListener('focus', onVisible);
    this.destroyRef.onDestroy(() => {
      document.removeEventListener('visibilitychange', onVisible);
      window.removeEventListener('focus', onVisible);
    });
  }

  /**
   * Holt den Nutzer aus der Anmeldemaske heraus, falls er dort inzwischen gelandet ist.
   *
   * <p>Der Aufruf hier laeuft aus dem `ngOnInit` der Wurzelkomponente und ist ASYNCHRON — die
   * erste Router-Navigation startet direkt danach, also BEVOR die Anmeldung steht. Der authGuard
   * sieht dann „nicht angemeldet" und schickt auf `/login?returnUrl=…`. Kurz darauf gelingt die
   * Uebernahme, aber niemand navigiert zurueck: der Nutzer sitzt angemeldet vor dem
   * Anmeldeformular. Betrifft beide Wege — den Einmal-Code UND die geteilte Anmeldung.</p>
   */
  private leaveLoginMask(): void {
    const url = new URL(location.href);
    if (!url.pathname.endsWith('/login')) return;
    const back = url.searchParams.get('returnUrl');
    void this.router.navigateByUrl(back && back.startsWith('/') ? back : '/');
  }
}
