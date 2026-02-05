export type CollabMessageType =
  | "hello"
  | "join"
  | "snapshot"
  | "ops"
  | "ack"
  | "presence"
  | "error"
  | "leave";

export interface CollabEnvelope<TPayload> {
  protocolVersion: number;
  documentId: string;
  sessionId: string;
  senderId: string;
  sequence: number;
  lamport: number;
  timestampUtc: string;
  messageType: CollabMessageType;
  payload: TPayload;
}

export interface HelloMessage {
  clientName: string;
  capabilities: string[];
  compression?: string | null;
}

export interface JoinMessage {
  documentId: string;
  knownVersion: number;
  snapshotId?: string | null;
}

export interface SnapshotMessage {
  snapshotId: string;
  version: number;
  payload: string;
}

export interface AckMessage {
  batchId: string;
  actorId: string;
  sequence: number;
}

export interface ErrorMessage {
  code: string;
  message: string;
}

export interface LeaveMessage {
  reason?: string | null;
}

export type AnchorBias = "before" | "after" | "Before" | "After";

export interface TextAnchor {
  nodeId: string;
  offset: number;
  bias: AnchorBias;
}

export interface AnchorRange {
  start: TextAnchor;
  end: TextAnchor;
}

export interface PresenceState {
  userId: string;
  displayName: string;
  caret?: TextAnchor | null;
  selection?: AnchorRange | null;
  updatedAtUtc: string;
  color?: string | null;
}

export interface PresenceMessage {
  presence: PresenceState;
  timeToLive: string;
}

export interface CollabOpBatch {
  batchId: string;
  actorId: string;
  baseVersion: number;
  sequence: number;
  lamport: number;
  timestampUtc: string;
  ops: CollabOp[];
}

export type CollabOp =
  | InsertTextOp
  | DeleteRangeOp
  | SetParagraphPropertiesOp
  | SetInlinePropertiesOp
  | InsertBlockOp
  | DeleteBlockOp
  | ReplaceBlockOp;

export interface InsertTextOp {
  kind: "InsertText";
  anchor: TextAnchor;
  text: string;
  authorId?: string | null;
}

export interface DeleteRangeOp {
  kind: "DeleteRange";
  start: TextAnchor;
  end: TextAnchor;
}

export interface SetParagraphPropertiesOp {
  kind: "SetParagraphProperties";
  paragraphNodeId: string;
  properties: Record<string, string>;
  lamport: number;
}

export interface SetInlinePropertiesOp {
  kind: "SetInlineProperties";
  inlineNodeId: string;
  properties: Record<string, string>;
  lamport: number;
}

export interface InsertBlockOp {
  kind: "InsertBlock";
  parentNodeId: string;
  position: string;
  blockType: string;
  payload?: string | null;
}

export interface DeleteBlockOp {
  kind: "DeleteBlock";
  parentNodeId: string;
  position: string;
  blockNodeId: string;
}

export interface ReplaceBlockOp {
  kind: "ReplaceBlock";
  blockNodeId: string;
  payload: string;
}

export interface OpsMessage {
  batch: CollabOpBatch;
}

export function serializeEnvelope<TPayload>(envelope: CollabEnvelope<TPayload>): string {
  return JSON.stringify(envelope);
}

export function deserializeEnvelope<TPayload>(payload: string): CollabEnvelope<TPayload> {
  const parsed = JSON.parse(payload) as CollabEnvelope<TPayload>;
  parsed.messageType = normalizeMessageType(parsed.messageType);
  return parsed;
}

export function normalizeMessageType(messageType: string): CollabMessageType {
  return messageType.toLowerCase() as CollabMessageType;
}
