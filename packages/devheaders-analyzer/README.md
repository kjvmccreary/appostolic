# Appostolic.DevHeadersAnalyzer

Roslyn analyzer preventing reintroduction of deprecated development header authentication pathway.

## Rule RDH0001

**Forbidden legacy dev header artifact**
Triggers when any of these tokens appear in C# source (string literal or identifier):

- `x-dev-user`
- `x-tenant`
- `DevHeaderAuthHandler`
- `BearerOrDev`
- `AUTH__ALLOW_DEV_HEADERS`

### Allowlist

- `DevHeadersRemovedTests.cs` (negative-path regression test)
- `storyLog` paths (historical documentation entries)

### Rationale

Ensures all environments continue using only the unified JWT authentication flow; prevents accidental resurrection of insecure developer shortcuts.
