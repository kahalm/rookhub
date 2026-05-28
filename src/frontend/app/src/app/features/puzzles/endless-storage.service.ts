import { Injectable } from '@angular/core';

interface EndlessConfig {
  startElo: number;
  step: number;
  themes: string;
  fasttrack: boolean;
  fasttrackThreshold1?: number;
  fasttrackThreshold2?: number;
  stockfishDepth: number;
}

interface EndlessSession {
  timestamp: number;
  config: EndlessConfig;
  totalSolved: number;
  maxRating: number;
  durationSeconds: number;
  mistakeAtRatings: number[];
}

const CONFIG_KEY = 'rookhub_endless_config';
const HIGHSCORE_KEY = 'rookhub_endless_highscore';
const HISTORY_KEY = 'rookhub_endless_history';
const MAX_HISTORY_SESSIONS = 50;

@Injectable({ providedIn: 'root' })
export class EndlessStorageService {

  loadConfig(defaults: EndlessConfig): EndlessConfig {
    let config = { ...defaults };
    try {
      const raw = localStorage.getItem(CONFIG_KEY);
      if (raw) {
        const saved = JSON.parse(raw);
        delete saved.rangeWidth;
        config = { ...config, ...saved };
      }
    } catch {}
    if (config.step < 10) config.step = 10;
    if (config.step > 200) config.step = 200;
    if (!config.stockfishDepth || config.stockfishDepth < 1) config.stockfishDepth = 16;
    if (config.stockfishDepth > 24) config.stockfishDepth = 24;
    if (config.fasttrackThreshold1 != null && config.fasttrackThreshold1 <= config.startElo) {
      config.fasttrackThreshold1 = undefined;
    }
    if (config.fasttrackThreshold2 != null && config.fasttrackThreshold2 <= config.startElo) {
      config.fasttrackThreshold2 = undefined;
    }
    return config;
  }

  saveConfig(config: EndlessConfig): void {
    try { localStorage.setItem(CONFIG_KEY, JSON.stringify(config)); } catch {}
  }

  loadHighscore(): number {
    try {
      const raw = localStorage.getItem(HIGHSCORE_KEY);
      if (raw) return parseInt(raw, 10) || 0;
    } catch {}
    return 0;
  }

  checkHighscore(maxRatingReached: number, currentHighscore: number): { highscore: number; isNew: boolean } {
    if (maxRatingReached > currentHighscore) {
      try { localStorage.setItem(HIGHSCORE_KEY, String(maxRatingReached)); } catch {}
      return { highscore: maxRatingReached, isNew: true };
    }
    return { highscore: currentHighscore, isNew: false };
  }

  loadSessionHistory(): EndlessSession[] {
    try {
      const raw = localStorage.getItem(HISTORY_KEY);
      if (raw) return JSON.parse(raw) || [];
    } catch {}
    return [];
  }

  saveSessionHistory(history: EndlessSession[]): void {
    try { localStorage.setItem(HISTORY_KEY, JSON.stringify(history)); } catch {}
  }

  recordSession(history: EndlessSession[], session: EndlessSession): EndlessSession[] {
    const updated = [...history, session];
    const trimmed = updated.length > MAX_HISTORY_SESSIONS
      ? updated.slice(-MAX_HISTORY_SESSIONS)
      : updated;
    this.saveSessionHistory(trimmed);
    return trimmed;
  }
}
