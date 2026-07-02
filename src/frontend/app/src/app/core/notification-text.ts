import { TranslateService } from '@ngx-translate/core';
import { AppNotification } from './in-app-notification.service';

/**
 * Rendert den lokalisierten Text einer In-App-Benachrichtigung. Geteilt von Navbar-Glocke und
 * History-Seite, damit beide identisch formatieren. solved-Varianten (Challenge/Revenge) und der
 * Hol-/Wartezeit-Suffix beim fertigen Chessable-Import werden hier zentral behandelt; fehlende
 * Parameter (alte Benachrichtigungen) lassen den Suffix einfach weg.
 */
export function notificationText(translate: TranslateService, n: AppNotification): string {
  const data = n.data ?? {};
  let key = 'notifications.type.' + n.type;
  if (n.type === 'challenge_resolved' || n.type === 'revenge_performed')
    key += data['solved'] === 'true' ? '_solved' : '_failed';
  let text = translate.instant(key, data);
  if (n.type === 'chessable_import_completed' && data['fetchTime']) {
    text += ' · ' + translate.instant('notifications.chessableDuration', data);
  }
  return text;
}

/**
 * Grobe Kategorie einer Benachrichtigung — steuert den Filter auf der History-Seite.
 * Neue Typen dazu-mappen; `other` fängt alles ab, wofür (noch) keine Kategorie definiert ist.
 */
export type NotificationCategory = 'courses' | 'friends' | 'puzzles' | 'messages' | 'tournaments' | 'admin' | 'other';

/** Alle Kategorien in fester Anzeigereihenfolge (auch für stabile Chip-Reihenfolge in der UI). */
export const NOTIFICATION_CATEGORIES: NotificationCategory[] = [
  'courses', 'friends', 'puzzles', 'messages', 'tournaments', 'admin', 'other',
];

export function notificationCategory(type: string): NotificationCategory {
  switch (type) {
    case 'chessable_import_completed':
    case 'chessable_import_failed':
    case 'chessable_new_course':
    case 'chessable_token_added':
    case 'course_shared':
    case 'repertoire_shared':
      return 'courses';
    case 'friend_request_received':
    case 'friend_request_accepted':
      return 'friends';
    case 'challenge_received':
    case 'challenge_resolved':
    case 'revenge_performed':
      return 'puzzles';
    case 'admin_message_received':
    case 'user_message_received':
      return 'messages';
    case 'tournament_new_round':
      return 'tournaments';
    case 'new_user_registered':
      return 'admin';
    default:
      return 'other';
  }
}

/** Material-Icon je Benachrichtigungstyp (geteilt von Glocke + History-Seite). */
export function notificationIcon(n: AppNotification): string {
  switch (n.type) {
    case 'chessable_import_completed': return 'menu_book';
    case 'chessable_import_failed': return 'error_outline';
    case 'friend_request_received': return 'person_add';
    case 'friend_request_accepted': return 'how_to_reg';
    case 'revenge_performed': return 'sports_kabaddi';
    case 'challenge_received': return 'sports_esports';
    case 'challenge_resolved': return 'emoji_events';
    case 'admin_message_received': return 'mail';
    case 'user_message_received': return 'mark_email_unread';
    case 'tournament_new_round': return 'emoji_events';
    case 'new_user_registered': return 'group_add';
    case 'chessable_token_added': return 'vpn_key';
    case 'chessable_new_course': return 'library_add';
    case 'course_shared': return 'group_add';
    case 'repertoire_shared': return 'group_add';
    default: return 'notifications';
  }
}
