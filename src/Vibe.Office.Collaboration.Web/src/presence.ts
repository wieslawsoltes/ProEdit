import { PresenceState } from "./protocol.js";

export interface PresenceEntry {
  presence: PresenceState;
  expiresAtUtc: number;
}

export class PresenceRegistry {
  private readonly entries = new Map<string, PresenceEntry>();

  constructor(private readonly defaultTimeToLiveMs = 10000) {}

  update(presence: PresenceState, ttlMs?: number, nowUtc = Date.now()): void {
    const ttl = ttlMs ?? this.defaultTimeToLiveMs;
    if (ttl <= 0) {
      return;
    }

    const updatedAt = presence.updatedAtUtc ? Date.parse(presence.updatedAtUtc) : nowUtc;
    const normalized: PresenceState = { ...presence, updatedAtUtc: new Date(updatedAt).toISOString() };
    this.entries.set(presence.userId, { presence: normalized, expiresAtUtc: updatedAt + ttl });
  }

  getActive(nowUtc = Date.now()): PresenceState[] {
    this.prune(nowUtc);
    return Array.from(this.entries.values()).map((entry) => entry.presence);
  }

  prune(nowUtc = Date.now()): string[] {
    const removed: string[] = [];
    for (const [userId, entry] of this.entries.entries()) {
      if (entry.expiresAtUtc <= nowUtc) {
        this.entries.delete(userId);
        removed.push(userId);
      }
    }
    return removed;
  }
}

export class PresenceThrottler {
  private lastSentAt = 0;

  constructor(private readonly intervalMs: number) {}

  shouldSend(nowUtc = Date.now()): boolean {
    if (nowUtc - this.lastSentAt < this.intervalMs) {
      return false;
    }

    this.lastSentAt = nowUtc;
    return true;
  }
}
