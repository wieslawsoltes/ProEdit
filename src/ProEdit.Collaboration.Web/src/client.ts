import {
  AckMessage,
  CollabEnvelope,
  CollabMessageType,
  CollabOpBatch,
  ErrorMessage,
  HelloMessage,
  JoinMessage,
  OpsMessage,
  PresenceMessage,
  PresenceState,
  SnapshotMessage,
  deserializeEnvelope,
  serializeEnvelope,
} from "./protocol.js";
import { PresenceRegistry, PresenceThrottler } from "./presence.js";

export interface CollabWebSocketClientOptions {
  url: string;
  documentId: string;
  sessionId: string;
  senderId: string;
  clientName: string;
  capabilities?: string[];
  compression?: string | null;
  presenceThrottleMs?: number;
  defaultPresenceTtlMs?: number;
  webSocketFactory?: () => WebSocket;
}

export class CollabWebSocketClient {
  private socket?: WebSocket;
  private readonly options: CollabWebSocketClientOptions;
  private readonly presenceRegistry: PresenceRegistry;
  private readonly presenceThrottler: PresenceThrottler;
  private sequence = 0;
  private lamport = 0;

  onEnvelope?: (envelope: CollabEnvelope<unknown>) => void;
  onOps?: (batch: CollabOpBatch) => void;
  onSnapshot?: (snapshot: SnapshotMessage) => void;
  onPresence?: (presence: PresenceState, ttlMs: number) => void;
  onError?: (error: ErrorMessage) => void;
  onStateChanged?: (state: "connected" | "disconnected" | "error", message?: string) => void;

  constructor(options: CollabWebSocketClientOptions) {
    this.options = options;
    this.presenceRegistry = new PresenceRegistry(options.defaultPresenceTtlMs ?? 10000);
    this.presenceThrottler = new PresenceThrottler(options.presenceThrottleMs ?? 80);
  }

  async connect(): Promise<void> {
    if (this.socket) {
      return;
    }

    const factory = this.options.webSocketFactory ?? (() => new WebSocket(this.options.url));
    const socket = factory();
    socket.binaryType = "arraybuffer";
    this.socket = socket;

    await new Promise<void>((resolve, reject) => {
      socket.onopen = () => resolve();
      socket.onerror = () => reject(new Error("WebSocket connection failed."));
    });

    this.onStateChanged?.("connected");
    this.sendHello();
    this.sendJoin();

    socket.onmessage = (event) => {
      const data = typeof event.data === "string" ? event.data : new TextDecoder().decode(event.data as ArrayBuffer);
      const envelope = deserializeEnvelope<unknown>(data);
      this.onEnvelope?.(envelope);
      this.handleEnvelope(envelope);
    };

    socket.onclose = () => {
      this.socket = undefined;
      this.onStateChanged?.("disconnected");
    };
  }

  disconnect(): void {
    if (!this.socket) {
      return;
    }

    this.socket.close();
    this.socket = undefined;
    this.onStateChanged?.("disconnected");
  }

  sendOps(batch: CollabOpBatch): void {
    const payload: OpsMessage = { batch };
    this.sendEnvelope("ops", payload);
  }

  sendSnapshot(snapshot: SnapshotMessage): void {
    this.sendEnvelope("snapshot", snapshot);
  }

  sendPresence(presence: PresenceState, ttlMs?: number): void {
    if (!this.presenceThrottler.shouldSend()) {
      return;
    }

    const timeToLiveMs = ttlMs ?? this.options.defaultPresenceTtlMs ?? 10000;
    const payload: PresenceMessage = {
      presence,
      timeToLive: formatTimeSpan(timeToLiveMs),
    };
    this.sendEnvelope("presence", payload);
  }

  getActivePresence(): PresenceState[] {
    return this.presenceRegistry.getActive();
  }

  private handleEnvelope(envelope: CollabEnvelope<unknown>): void {
    switch (envelope.messageType) {
      case "ops": {
        const payload = envelope.payload as OpsMessage;
        this.onOps?.(payload.batch);
        break;
      }
      case "snapshot": {
        const payload = envelope.payload as SnapshotMessage;
        this.onSnapshot?.(payload);
        break;
      }
      case "presence": {
        const payload = envelope.payload as PresenceMessage;
        const ttlMs = parseTimeSpan(payload.timeToLive);
        this.presenceRegistry.update(payload.presence, ttlMs);
        this.onPresence?.(payload.presence, ttlMs);
        break;
      }
      case "error": {
        this.onError?.(envelope.payload as ErrorMessage);
        break;
      }
      default:
        break;
    }
  }

  private sendHello(): void {
    const payload: HelloMessage = {
      clientName: this.options.clientName,
      capabilities: this.options.capabilities ?? [],
      compression: this.options.compression ?? null,
    };

    this.sendEnvelope("hello", payload);
  }

  private sendJoin(): void {
    const payload: JoinMessage = {
      documentId: this.options.documentId,
      knownVersion: 0,
      snapshotId: null,
    };

    this.sendEnvelope("join", payload);
  }

  private sendEnvelope<TPayload>(messageType: CollabMessageType, payload: TPayload): void {
    if (!this.socket) {
      return;
    }

    const envelope: CollabEnvelope<TPayload> = {
      protocolVersion: 1,
      documentId: this.options.documentId,
      sessionId: this.options.sessionId,
      senderId: this.options.senderId,
      sequence: ++this.sequence,
      lamport: ++this.lamport,
      timestampUtc: new Date().toISOString(),
      messageType,
      payload,
    };

    this.socket.send(serializeEnvelope(envelope));
  }
}

function formatTimeSpan(milliseconds: number): string {
  const totalSeconds = Math.floor(milliseconds / 1000);
  const seconds = totalSeconds % 60;
  const totalMinutes = Math.floor(totalSeconds / 60);
  const minutes = totalMinutes % 60;
  const totalHours = Math.floor(totalMinutes / 60);
  const hours = totalHours % 24;
  const days = Math.floor(totalHours / 24);
  const ms = milliseconds % 1000;
  const time = `${pad2(hours)}:${pad2(minutes)}:${pad2(seconds)}.${pad3(ms)}`;
  return days > 0 ? `${days}.${time}` : time;
}

function parseTimeSpan(value: string): number {
  if (!value) {
    return 0;
  }

  const daySplit = value.split(".");
  let timePart = value;
  let days = 0;
  if (daySplit.length === 2 && daySplit[0].indexOf(":") === -1) {
    days = Number(daySplit[0]);
    timePart = daySplit[1];
  }

  const timeParts = timePart.split(":");
  if (timeParts.length !== 3) {
    return 0;
  }

  const hours = Number(timeParts[0]);
  const minutes = Number(timeParts[1]);
  const seconds = Number(timeParts[2]);
  if (Number.isNaN(hours) || Number.isNaN(minutes) || Number.isNaN(seconds)) {
    return 0;
  }

  return (((days * 24 + hours) * 60 + minutes) * 60 + seconds) * 1000;
}

function pad2(value: number): string {
  return value.toString().padStart(2, "0");
}

function pad3(value: number): string {
  return value.toString().padStart(3, "0");
}
