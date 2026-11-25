# SignalR messaging, feed posts, and materials

Connect to `/hubs/messages` with a teacher or student JWT. Supply the current token through the SignalR client's access-token factory so a reconnect can use a refreshed token. Use HTTPS and keep query strings out of reverse-proxy access logs. Browser WebSocket connections can carry tokens in the query string.

## Live events

- Call `JoinConversation(conversationId)` for the active conversation. Call `LeaveConversation(conversationId)` when leaving it. `MessageCreated` carries the same message object returned by REST.
- `FeedPostCreated`, `FeedPostDeleted`, `MaterialCreated`, `MaterialCoverUpdated`, and `MaterialDeleted` carry an object with an `id` field. Fetch the affected data through REST or remove the deleted item locally.
- `TeacherAssignmentChanged` has no payload. Refetch the student profile, conversations, feed, and materials. Join the active conversation again if it is still accessible.

Continue to send messages, create feed posts, and upload materials through the existing REST endpoints. Notifications reflect successful saves and can be missed during disconnection.

## Reconnect and recover

Register event handlers before starting the connection. Enable automatic reconnect. After reconnecting, join the active conversation, then reload the feed and materials through REST. For messages, fetch the latest page and follow `X-Next-Before-Id` until you reach the latest message ID you already have or exhaust the history. Merge REST results and live events by message ID so a message received during catch-up appears once. If rejoining is denied, refresh the conversation list.
