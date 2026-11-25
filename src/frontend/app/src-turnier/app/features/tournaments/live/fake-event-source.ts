/** EventSource-Attrappe für die Specs der Live-Bretter: der Test schickt Ereignisse und Fehler von Hand. */
export class FakeEventSource {
  static all: FakeEventSource[] = [];
  readyState = 0;
  closed = false;
  onerror: ((e: Event) => void) | null = null;
  private readonly listeners = new Map<string, ((e: Event) => void)[]>();

  constructor(public readonly url: string) { FakeEventSource.all.push(this); }

  addEventListener(type: string, fn: (e: Event) => void): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), fn]);
  }

  /** Ein benanntes SSE-Ereignis mit JSON-Rumpf. */
  emit(type: string, data: unknown): void {
    this.readyState = 1;
    const e = new MessageEvent(type, { data: JSON.stringify(data) });
    for (const fn of this.listeners.get(type) ?? []) fn(e);
  }

  fail(closed: boolean): void {
    this.readyState = closed ? 2 : 0;
    this.onerror?.(new Event('error'));
  }

  close(): void { this.closed = true; this.readyState = 2; }

  static factory = (url: string) => new FakeEventSource(url) as unknown as EventSource;
  static reset(): void { FakeEventSource.all = []; }
  static last(): FakeEventSource { return FakeEventSource.all[FakeEventSource.all.length - 1]; }
}
