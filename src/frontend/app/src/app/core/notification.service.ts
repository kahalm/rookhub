import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  requestPermission(): Promise<NotificationPermission> {
    if (!('Notification' in window)) {
      return Promise.resolve('denied' as NotificationPermission);
    }
    return Notification.requestPermission();
  }

  notify(title: string, options?: NotificationOptions): void {
    if (!('Notification' in window)) return;
    if (Notification.permission === 'granted') {
      new Notification(title, options);
    }
  }

  get isSupported(): boolean {
    return 'Notification' in window;
  }

  get permission(): NotificationPermission {
    if (!('Notification' in window)) return 'denied';
    return Notification.permission;
  }
}
