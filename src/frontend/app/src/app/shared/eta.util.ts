import { TranslateService } from '@ngx-translate/core';

/**
 * Restdauer als „12 min", „3 h 20 min" oder — ab zwei Tagen — „6 Tage 12 h". Geteilt von der Seite
 * „Partie-Analysen" (Restdauer aller eigenen Analysen) und dem Partie-Rückblick (Restdauer DIESER
 * Partie) — beide Zahlen rechnet der Server, hier steht nur die Schreibweise.
 *
 * <p>Minuten in einer Angabe über hundert Stunden sind Schein-Genauigkeit: die Schätzung stammt aus
 * einem Mittel der letzten Stunde, sie ist auf Tage genau und nicht auf Minuten.</p>
 */
export function formatEta(minutes: number, translate: TranslateService): string {
  const total = Math.max(1, Math.round(minutes));
  const h = Math.floor(total / 60);
  if (h >= 48) {
    const days = Math.floor(h / 24);
    return `${days} ${translate.instant(days === 1 ? 'common.day' : 'common.days')} ${h % 24} h`;
  }
  return h > 0 ? `${h} h ${total % 60} min` : `${total} min`;
}
