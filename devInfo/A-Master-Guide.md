# MasterGuide — Delivery Phases and Story Index

This guide sequences the current work across Auth, Notifications, and Pre‑Migration. It lists each phase with the exact stories and references to their source sprint plans and designs.

Quick status (as of now)

- Current Sprint: Phase 0 complete (middleware + proxy/header bridge + tests). Moving to Phase 1.
- Notifications: Notif‑01..12 completed; DB outbox work starts at Notif‑13.
- Auth: Auth‑01..14 not started.
- Pre‑Migration: Plans only.

Source plans and designs

- Current sprint: devInfo/currentSprint.md
- Auth plan: devInfo/AuthInfo/authSprintPlan.md
- Auth design: devInfo/AuthInfo/authDesign.md
- Notifications plan: devInfo/Sendgrid/notifSprintPlan.md
- Notifications design: devInfo/Sendgrid/notifDesign.md
- Pre‑Migration plan: devInfo/PreMigrate/migSprintPlan.md
- Pre‑Migration design: devInfo/PreMigrate/migDesign.md
- Story log: devInfo/storyLog.md

Phases overview

- Phase 0 — Close security basics (route protection)
- Phase 1 — Auth core (DB‑backed, two‑stage login, switcher)
- Phase 2 — Members + Invites
- Phase 3 — Notifications outbox (persistence + PII)
- Phase 4 — E2E hardening and docs
- Phase 5 — Pre‑migration prep (still monolith)

Guidelines

- With each story completion, update /Users/kevinmccreary/appostolic/devInfo/storyLog.md with the exact summary that you display in the Github Copilot chat window. Updates are additive; no destruction. Include a header row for each addition showing the story ID and Name.
- At the end of each story, update SnapshotArchitecture.md. Updates should be additive. Only destructive items allowed are instances where the item is outdated or superseded.
- Commit w message and sync at conclusion of each story.
- Update the story references below each time a story is complete. Use ✅ (DONE).

---

## Phase 0 — Close security basics

| Story ID                    | Title                         | Source                             | Notes/Dependencies                 |
| --------------------------- | ----------------------------- | ---------------------------------- | ---------------------------------- |
| ✅ (DONE) Current Sprint #3 | Route protection middleware   | devInfo/currentSprint.md           | Finish before expanding flows      |
| ✅ (DONE) Current Sprint #4 | Session → API headers bridge  | devInfo/currentSprint.md           | Complete sub tasks AC1, AC2 & AC3  |
| ✅ (DONE) Current Sprint #5 | Tests (minimal)               | devInfo/currentSprint.md           | Complete sub tasks AC1, AC2        |
| ✅ (DONE) Auth‑10           | Proxy Header Mapping & Guards | devInfo/AuthInfo/authSprintPlan.md | Requires Auth‑04 (tenant selected) |
| ✅ (DONE) Auth‑11           | Route Protection (Role‑based) | devInfo/AuthInfo/authSprintPlan.md | Admin‑only gating                  |

---

## Phase 1 — Auth core (DB‑backed, two‑stage login, switcher)

| Story ID          | Title                                               | Source                             | Notes/Dependencies                  |
| ----------------- | --------------------------------------------------- | ---------------------------------- | ----------------------------------- |
| ✅ (DONE) Auth‑01 | Schema & Migrations (Users/Memberships/Invitations) | devInfo/AuthInfo/authSprintPlan.md | Bootstrap roles + invites schema    |
| ✅ (DONE) Auth‑02 | Password Hashing & Signup API                       | devInfo/AuthInfo/authSprintPlan.md | Self‑serve signup + personal tenant |
| ✅ (DONE) Auth‑03 | Credentials Auth via DB (NextAuth)                  | devInfo/AuthInfo/authSprintPlan.md | Replace env‑seed auth               |
| ✅ (DONE) Auth‑04 | Two‑Stage Login: /select‑tenant                     | devInfo/AuthInfo/authSprintPlan.md | Multi‑tenant selector               |
| ✅ (DONE) Auth‑05 | Header Tenant Switcher                              | devInfo/AuthInfo/authSprintPlan.md | Session/JWT refresh on switch       |

Design refs: devInfo/AuthInfo/authDesign.md

---

## Phase 2 — Members + Invites

| Story ID          | Title                            | Source                             | Notes/Dependencies                     |
| ----------------- | -------------------------------- | ---------------------------------- | -------------------------------------- |
| ✅ (DONE) Auth‑06 | Members List (Admin Read‑Only)   | devInfo/AuthInfo/authSprintPlan.md | Requires Auth‑04                       |
| ✅ (DONE) Auth‑07 | Invite API & Email (Dev)         | devInfo/AuthInfo/authSprintPlan.md | Uses notifications; token 7‑day expiry |
| ✅ (DONE) Auth‑08 | Invite Acceptance Flow           | devInfo/AuthInfo/authSprintPlan.md | Accept path for existing/new users     |
| ✅ (DONE) Auth‑09 | Members Management (Admin Write) | devInfo/AuthInfo/authSprintPlan.md | Role change/remove                     |

Notifications hooks already in place (Notif‑07/08); see devInfo/Sendgrid/notifSprintPlan.md

---

## Phase 3 — Notifications outbox (persistence + PII)

Baseline outbox and privacy-by-design before scaling email usage.

| Story ID           | Title                                                   | Source                              | Notes/Dependencies                        |
| ------------------ | ------------------------------------------------------- | ----------------------------------- | ----------------------------------------- |
| ✅ (DONE) Notif‑13 | Notifications table (outbox) migration                  | devInfo/Sendgrid/notifSprintPlan.md | DB schema + indexes                       |
| ✅ (DONE) Notif‑14 | Enqueue writes to DB outbox                             | devInfo/Sendgrid/notifSprintPlan.md | Insert + dedupe guard                     |
| ✅ (DONE) Notif‑15 | Dispatcher reads/updates DB records                     | devInfo/Sendgrid/notifSprintPlan.md | Transitions + retries                     |
| ✅ (DONE) Notif‑16 | Admin/dev endpoints: list + retry                       | devInfo/Sendgrid/notifSprintPlan.md | Dev‑only list/filter + retry; tests green |
| ✅ (DONE) Notif‑17 | Purge job (retention)                                   | devInfo/Sendgrid/notifSprintPlan.md | Hourly purger; 60–90d defs                |
| ✅ (DONE) Notif‑18 | Dedupe store and policy                                 | devInfo/Sendgrid/notifSprintPlan.md | TTL table + index adj.                    |
| ✅ (DONE) Notif‑19 | Delivery status webhook (optional)                      | devInfo/Sendgrid/notifSprintPlan.md | SendGrid webhook → provider_status stored |
| Notif‑20           | E2E verification (outbox path)                          | devInfo/Sendgrid/notifSprintPlan.md |                                           |
| Notif‑21           | PII minimization and token hashing                      | devInfo/Sendgrid/notifSprintPlan.md | Hash tokens; redact logs                  |
| Notif‑22           | Field-level encryption for sensitive columns (optional) | devInfo/Sendgrid/notifSprintPlan.md |                                           |
| Notif‑23           | Retention policy hardening (PII‑aware)                  | devInfo/Sendgrid/notifSprintPlan.md | Per‑status windows                        |
| Notif‑25           | Logging and telemetry privacy gates                     | devInfo/Sendgrid/notifSprintPlan.md | Redaction helpers/usage                   |
| Notif‑26           | Privacy Policy and vendor compliance docs               | devInfo/Sendgrid/notifSprintPlan.md | Policy + subprocessor list                |

Design refs: devInfo/Sendgrid/notifDesign.md (Outbox + PII sections)

Resend capability (after baseline outbox)
| Story ID | Title | Source | Notes |
|---|---|---|---|
| Notif‑27 | Outbox schema extension for Resend | devInfo/Sendgrid/notifSprintPlan.md | Adds resend fields, throttle |
| Notif‑28 | Manual resend endpoint (single) | devInfo/Sendgrid/notifSprintPlan.md | 201/429; links to original |
| Notif‑29 | Bulk resend endpoint | devInfo/Sendgrid/notifSprintPlan.md | Caps + batching |
| Notif‑30 | Resend policy, throttling, and metrics | devInfo/Sendgrid/notifSprintPlan.md | email.resend.total |
| Notif‑31 | Resend history and admin UI hooks (API) | devInfo/Sendgrid/notifSprintPlan.md | History per original |
| Notif‑32 | Automated resend (no‑action detector) [optional] | devInfo/Sendgrid/notifSprintPlan.md | Scheduled job rules |

---

## Phase 4 — E2E hardening and docs

| Story ID          | Title                                            | Source                              | Notes/Dependencies      |
| ----------------- | ------------------------------------------------ | ----------------------------------- | ----------------------- |
| ✅ (DONE) Auth‑10 | Proxy Header Mapping & Guards                    | devInfo/AuthInfo/authSprintPlan.md  | If not done in Phase 0  |
| ✅ (DONE) Auth‑11 | Route Protection (Role‑based)                    | devInfo/AuthInfo/authSprintPlan.md  | If not done in Phase 0  |
| Auth‑12           | API Integration Tests (Security & Flows)         | devInfo/AuthInfo/authSprintPlan.md  | Dev env tests           |
| Auth‑13           | Web Tests (Sign‑up, Invite, Two‑Stage, Switcher) | devInfo/AuthInfo/authSprintPlan.md  | Vitest + RTL + MSW      |
| Auth‑14           | Docs & Runbook Updates                           | devInfo/AuthInfo/authSprintPlan.md  | Flows + env vars        |
| Notif‑20          | E2E verification (outbox path)                   | devInfo/Sendgrid/notifSprintPlan.md | DB row → SMTP → Mailhog |

---

## Phase 5 — Pre‑migration prep (keep monolith; extraction later)

| Story ID | Title                           | Source                              | Notes                        |
| -------- | ------------------------------- | ----------------------------------- | ---------------------------- |
| Mig01    | Transport abstraction           | devInfo/PreMigrate/migSprintPlan.md | Interface seam (Channel now) |
| Mig02    | Outbox publisher integration    | devInfo/PreMigrate/migSprintPlan.md | Publish outbox IDs           |
| Mig03    | External worker executable      | devInfo/PreMigrate/migSprintPlan.md | Same repo, optional          |
| Mig04    | Broker adapter behind flag      | devInfo/PreMigrate/migSprintPlan.md | Rabbit/SQS later             |
| Mig05    | DLQ and replay tooling          | devInfo/PreMigrate/migSprintPlan.md | Future                       |
| Mig06    | Broker observability            | devInfo/PreMigrate/migSprintPlan.md | Future                       |
| Mig07    | PII hardening in transport path | devInfo/PreMigrate/migSprintPlan.md | Align with Notif‑21          |
| Mig08    | Rollout plan and fallback       | devInfo/PreMigrate/migSprintPlan.md | Feature flags/rollback       |

Design refs: devInfo/PreMigrate/migDesign.md

---

How to use

- Work phase by phase. After each story:
  - Update devInfo/storyLog.md with a brief completion entry.
  - Strike through the story in its sprint plan file.
  - Commit and push.
- Keep emails dev‑only (Mailhog) until Notif‑21/26 are done and policies are in place.
- Use VS Code background tasks for long‑running servers; run one‑offs in separate terminals.

Next up (recommended)

- Phase 0 complete. Start Phase 1 Auth core (Auth‑01 → Auth‑05).
- Then Phase 2 Members + Invites.
- Proceed to Phase 3 outbox and PII hardening before enabling production email.
