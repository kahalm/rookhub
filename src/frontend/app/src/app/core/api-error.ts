import { TranslateService } from '@ngx-translate/core';

/**
 * Fehlertext einer API-Antwort in der UI-Sprache (Codereview 2026-09-29, F5-019).
 *
 * Bisher zeigten die Seiten `err.error?.message` roh — in der Sprache des jeweiligen Dienstes, nicht der Oberfläche:
 * der deutsche Nutzer las „A friendship or request already exists.", der kroatische Admin „Eine Rolle mit diesem Key
 * existiert bereits.". Die API schickt jetzt neben `message` einen stabilen `code` (`ApiErrorCodes` im Backend); hier
 * wird er über `apiErrors.<code>` übersetzt. Unbekannter Code oder keiner → wie bisher `message`, sonst der Ersatztext.
 */
export function apiErrorCodeText(err: unknown, translate: TranslateService): string | null {
  const code = (err as { error?: { code?: unknown } } | null)?.error?.code;
  if (typeof code !== 'string' || !/^[a-z][a-z0-9_]*$/.test(code)) return null;
  const key = `apiErrors.${code}`;
  const text = translate.instant(key);
  return typeof text === 'string' && text && text !== key ? text : null;
}

/** {@link apiErrorCodeText}, sonst `message` der Antwort, sonst der übersetzte `fallbackKey`. */
export function apiErrorText(err: unknown, translate: TranslateService, fallbackKey: string): string {
  const message = (err as { error?: { message?: unknown } } | null)?.error?.message;
  return apiErrorCodeText(err, translate)
    || (typeof message === 'string' && message.trim() ? message : '')
    || translate.instant(fallbackKey);
}
