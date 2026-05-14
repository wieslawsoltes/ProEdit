# ProEdit Collaboration (Web)

TypeScript client for the ProEdit collaboration protocol.

## Usage
```ts
import { CollabWebSocketClient } from "./dist/index.js";

const client = new CollabWebSocketClient({
  url: "ws://localhost:5000/collab",
  documentId: "00000000-0000-0000-0000-000000000001",
  sessionId: "00000000-0000-0000-0000-000000000002",
  senderId: "00000000-0000-0000-0000-000000000003",
  clientName: "Browser",
});

await client.connect();
client.onOps = (batch) => console.log("ops", batch);
```
