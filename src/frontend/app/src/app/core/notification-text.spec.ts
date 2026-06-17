import { TranslateService } from '@ngx-translate/core';
import { AppNotification } from './in-app-notification.service';
import { notificationText, notificationIcon } from './notification-text';

/** Fake TranslateService: gibt den (aufgelösten) Key zurück, damit Tests die Key-Wahl prüfen können. */
function fakeTranslate(): TranslateService {
  return { instant: (key: string, _params?: object) => key } as unknown as TranslateService;
}

function notif(type: string, data: Record<string, string> | null = null): AppNotification {
  return { id: 1, type, data, link: null, createdAt: '2026-06-17T00:00:00Z', seen: false };
}

describe('notificationText', () => {
  const t = fakeTranslate();

  it('uses notifications.type.<type> for a generic notification', () => {
    expect(notificationText(t, notif('friend_request_received'))).toBe('notifications.type.friend_request_received');
  });

  it('appends _solved / _failed for challenge_resolved based on data.solved', () => {
    expect(notificationText(t, notif('challenge_resolved', { solved: 'true' })))
      .toBe('notifications.type.challenge_resolved_solved');
    expect(notificationText(t, notif('challenge_resolved', { solved: 'false' })))
      .toBe('notifications.type.challenge_resolved_failed');
  });

  it('appends _solved / _failed for revenge_performed; missing solved → _failed', () => {
    expect(notificationText(t, notif('revenge_performed', { solved: 'true' })))
      .toBe('notifications.type.revenge_performed_solved');
    expect(notificationText(t, notif('revenge_performed')))   // alte Benachrichtigung ohne solved
      .toBe('notifications.type.revenge_performed_failed');
  });

  it('adds the chessable duration suffix only when fetchTime is present', () => {
    expect(notificationText(t, notif('chessable_import_completed', { fetchTime: '12' })))
      .toBe('notifications.type.chessable_import_completed · notifications.chessableDuration');
    expect(notificationText(t, notif('chessable_import_completed')))   // kein fetchTime → kein Suffix
      .toBe('notifications.type.chessable_import_completed');
  });
});

describe('notificationIcon', () => {
  it('maps known types to their Material icon', () => {
    expect(notificationIcon(notif('chessable_import_completed'))).toBe('menu_book');
    expect(notificationIcon(notif('chessable_import_failed'))).toBe('error_outline');
    expect(notificationIcon(notif('friend_request_received'))).toBe('person_add');
    expect(notificationIcon(notif('challenge_resolved'))).toBe('emoji_events');
    expect(notificationIcon(notif('admin_message_received'))).toBe('mail');
    expect(notificationIcon(notif('user_message_received'))).toBe('mark_email_unread');
  });

  it('falls back to the generic bell icon for unknown types', () => {
    expect(notificationIcon(notif('something_new'))).toBe('notifications');
  });
});
