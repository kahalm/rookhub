import { NotificationService } from './notification.service';

describe('NotificationService', () => {
  let svc: NotificationService;

  beforeEach(() => { svc = new NotificationService(); });

  it('isSupported spiegelt das Vorhandensein der Notification-API', () => {
    expect(svc.isSupported).toBe('Notification' in window);
  });

  it('permission gibt denied zurück, wenn die API fehlt (sonst den echten Wert)', () => {
    if (!('Notification' in window)) {
      expect(svc.permission).toBe('denied');
    } else {
      expect(svc.permission).toBe(Notification.permission);
    }
  });

  it('notify wirft nicht und konstruiert keine Notification ohne granted-Permission', () => {
    if (!('Notification' in window)) {
      expect(() => svc.notify('Titel')).not.toThrow();
      return;
    }
    const ctor = spyOn(window as any, 'Notification').and.callThrough();
    svc.notify('Titel');
    // In der Headless-Testumgebung ist permission nie 'granted' → kein Konstruktoraufruf.
    if (Notification.permission !== 'granted') {
      expect(ctor).not.toHaveBeenCalled();
    }
  });

  it('requestPermission liefert denied, wenn die API fehlt', async () => {
    if ('Notification' in window) {
      pending('Notification-API vorhanden — Negativfall nicht prüfbar');
      return;
    }
    await expectAsync(svc.requestPermission()).toBeResolvedTo('denied' as NotificationPermission);
  });
});
