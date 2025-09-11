## Worker Execution (S1-09)

In Development, task processing uses an in-memory queue and a background worker:

- Queue: `InMemoryAgentTaskQueue` (Channel<Guid>)
- Worker: `AgentTaskWorker` (BackgroundService)

Flow: the create endpoint enqueues the task ID → the worker dequeues and runs the orchestrator → the task transitions:

Pending → Running → Succeeded/Failed

Try it:

```bash
# Create a task
TASK=$(curl -s http://localhost:5198/api/agent-tasks \
  -H "x-dev-user: dev@example.com" -H "x-tenant: acme" \
  -H "Content-Type: application/json" \
  -d '{ "agentId":"11111111-1111-1111-1111-111111111111", "input": { "topic": "Beatitudes" } }' | jq -r .id)

# Poll for completion
curl -s "http://localhost:5198/api/agent-tasks/$TASK?includeTraces=true" \
  -H "x-dev-user: dev@example.com" -H "x-tenant: acme" | jq .
```

Note: For production you can later swap `IAgentTaskQueue` for an external broker like RabbitMQ or Azure Storage Queues.
