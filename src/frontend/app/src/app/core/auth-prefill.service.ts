import { Injectable } from '@angular/core';

/**
 * Hält die zuletzt eingegebenen Anmeldedaten (Benutzername/E-Mail/Passwort)
 * in-memory, damit sie beim Wechsel zwischen Login- und Registrierungs-Seite
 * erhalten bleiben (und beim Zurückwechseln wieder erscheinen).
 *
 * Bewusst NICHT über Query-Params gelöst — sonst stünde das Passwort in der
 * URL/Browser-History. Der State lebt nur im RAM und wird beim erfolgreichen
 * Login/Register bzw. beim Reload verworfen.
 */
@Injectable({ providedIn: 'root' })
export class AuthPrefillService {
  username = '';
  email = '';
  password = '';

  clear(): void {
    this.username = '';
    this.email = '';
    this.password = '';
  }
}
