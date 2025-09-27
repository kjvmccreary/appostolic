# Guardrails Sprint Plan (GRail)

> _Purpose_: deliver the doctrinal safety foundation (policy schema, evaluator, admin surfaces, and versioned snapshots) required for the lesson pipeline and 1.0 go-to-market cut.

---

## 🎯 Sprint Objectives

- Establish multi-layer guardrail storage (system ➝ tenant ➝ denomination) with row-level security and version history.
- Ship a deterministic evaluator + preflight API that returns allow/deny decisions with actionable rationale.
- Provide tenant admins and platform operators with UI controls to review, override, and snapshot guardrail policies.
- Wire observability, docs, and test coverage so the guardrails stack can be trusted before lesson generation rolls out.

Success will be measured by:

- ✅ Guardrail policy data model live with migrations + seeding.
- ✅ Evaluator endpoint returning structured verdicts that integrate with the existing auth/session flow.
- ✅ Admin UI enabling CRUD + preset application + diff review.
- ✅ Versioned policy snapshots persisted to object storage and referenced in audit logs.
- ✅ Updated LivingChecklist, SnapshotArchitecture, storyLog, and sprint artifacts.

---

## 📦 Scope Overview

| Pillar                 | In Scope                                                                             | Out of Scope                                                                                 |
| ---------------------- | ------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------- |
| Schema & Storage       | New `guardrail_policies` tables, denomination presets, RLS, seed data, migrations    | Content policy authoring UX for creators, future dynamic policy marketplace                  |
| Evaluator Service      | Deterministic allow/deny, policy merge order, explanation payloads, metrics, caching | ML-based classifier, external moderation adapters                                            |
| Admin UI               | Tenant admin page, system superadmin console, presets, diff viewer, publish workflow | Feature flag admin, guardrails for other domains (lesson jobs will wire in follow-up sprint) |
| Versioning & Snapshots | Object storage snapshots, change history, audit event integration                    | Long-term retention automation, governance workflows                                         |

---

## 🗺️ Milestones & Checklists

### Phase 0 — Foundations & Alignment (Day 0–1)

- [x] Reconcile requirements with `NorthStar.md`, `MvpCutMatrix.md`, and `LivingChecklist.md`.
- [x] Inventory existing guardrail stubs/tests (API + web) and document migration impact.
- [x] Finalize acceptance criteria with product/QA (including sample allow/deny cases).
- [x] Create feature branch (if needed) and mark sprint stories 🚧 IN PROGRESS in sprint tracker (continuing on `jwt/refactor-jwt-auth`; sprint tracker updated in `Sprint-01-Appostolic.md`).

### Phase 1 — Data Model & RLS (Day 1–3)

- [x] Design ERD + merge strategy (system → denomination → tenant → overrides → user prefs).
- [x] Add EF Core migration(s) with `.Designer.cs` and update `AppDbContextModelSnapshot`.
- [x] Seed baseline policies + denomination presets (configurable via JSON/seed scripts).
- [x] Implement row-level security / scoped queries for tenant isolation.
- [x] Update `SnapshotArchitecture.md` schema section (mark ✅ once merged).

### Phase 2 — Evaluator & API Surface (Day 3–6)

- [x] Implement policy merge + evaluation engine with test matrix (allow, deny, fallback, explanation).
- [x] Expose `/api/guardrails/preflight` endpoint (auth required) returning structured verdicts.
- [x] Integrate evaluator into existing auth/session flow to block disallowed prompts.
- [x] Add metrics (OTel counters + histograms) and structured security events.
- [x] Author integration/unit tests (happy path, deny, override precedence, cache warming).

### Phase 3 — Admin UI & Tooling (Day 6–9)

- [ ] Build tenant admin guardrails page (view, edit, reset to preset, diff preview).
- [ ] Build platform superadmin view (system presets, denomination catalog, audit feed).
- [ ] Add optimistic update + error handling + MSW fixtures for Vitest.
- [ ] Document UX defaults & accessibility (keyboard nav, screen reader labels).
- [ ] Extend Vitest suites for UI interactions (edit, revert, publish, deny preview).

### Phase 4 — Snapshotting, Audit, and Version History (Day 9–10)

- [ ] Persist policy snapshots to object storage (MinIO/S3) with metadata (tenant, version, timestamp).
- [ ] Wire audit log entries for every policy change (who, what, diff summary, snapshot link).
- [ ] Add CLI / admin endpoints to download snapshots for incident review.
- [ ] Update LivingChecklist and sprint plan status (Phase 4 ✅ when done).

### Phase 5 — Hardening, Docs, and Release (Day 10–12)

- [ ] Run full API & web test suites (`make test`).
- [ ] Execute manual UAT script covering allow/deny cases, UI flows, and snapshot restore.
- [ ] Update docs (`docs/guardrails/*.md`, README, runbooks) + `LivingChecklist` checkboxes.
- [ ] Append final summary to `devInfo/storyLog.md` and mark sprint stories ✅ DONE.
- [ ] Prepare release notes + demo script (optional but recommended).

---

## 🔍 Acceptance Criteria (per pillar)

- **Policy Schema & RLS**
  - Migrations apply cleanly and seed data installs without manual intervention.
  - EF layer enforces tenant-bound queries; cross-tenant access blocked in tests.
- **Evaluator Service**
  - Preflight API returns `{ decision, reasonCode, matchedPolicies[], advice }`.
  - Cache invalidation occurs on policy change or snapshot publish.
- **Admin UI**
  - Tenant admins can edit and publish policies with validation + diff preview.
  - Platform superadmins can manage denomination presets and change logs.
- **Snapshots & Audit**
  - Every change generates a snapshot & audit row linking to object storage.
  - Rollback (reapply snapshot) succeeds through admin tooling.

---

## 🧪 Quality Gates & Tooling

- Unit & integration tests covering policy merge logic, evaluator API, UI interactions.
- `make test` (dotnet + vitest + dev header guard) prior to merge.
- Static analysis (eslint, tsc) must remain green.
- Manual UAT checklist stored under `devInfo/QA/guardrails-uat.md` (to be created).
- Observability verification: traces + metrics visible in local Grafana dashboards.

---

## 📎 Dependencies & Assumptions

- Lesson pipeline integration will consume the evaluator in the following sprint (S1‑10).
- Object storage + audit infrastructure already provisioned (per `SnapshotArchitecture.md`).
- Frontend auth & role gating (TenantAdmin) enforced per existing helpers.
- Denomination presets curated in coordination with product/theology team.

---

## ⚠️ Risks & Mitigations

- **Complex policy precedence** → Write exhaustive tests & documentation; add debug tooling.
- **Migration churn** → Prototype schema in feature branch, run EF tests early, coordinate with DBAs.
- **UI complexity** → Ship feature flags / phased rollout; ensure snapshot rollback works before enabling edits broadly.
- **Performance under load** → Add caching layer & metrics; stress test evaluator with synthetic prompts.

---

## 📅 Reporting & Rituals

- Daily stand-up note: progress vs. milestone phase.
- Mid-sprint demo (after Phase 3) to product & theology reviewers.
- Sprint retro: capture lessons for lesson-pipeline sprint (S1‑10).

---

_Remember to update this plan as work progresses. Mark checkboxes, log deltas, and keep artifacts (docs, story log, LivingChecklist) in sync to preserve the single source of truth._

### Phase 0 Notes (2025-09-26)

- **Requirements alignment**: Cross-referenced `NorthStar.md` (§6 S1-09) and `MvpCutMatrix.md` (Guardrails & Doctrine Profiles) against `LivingChecklist.md` items (policy schema, evaluator API, admin UI, snapshots). Confirmed sprint scope satisfies all 1.0 guardrail obligations and feeds the upcoming lesson pipeline work.
- **Current implementation inventory**:
  - Web has stub guardrails editors (`ProfileGuardrailsForm`, `TenantGuardrailsForm`) that collect preferences but currently persist via generic profile/settings endpoints. No backend guardrail policy schema exists yet; API references guardrails only within JSONB profile blobs.
  - Dashboard landing page links to `/guardrails` but there is no route/feature implemented—captured as part of Phase 3 scope.
- **Acceptance criteria**: Reviewed and expanded section “Acceptance Criteria (per pillar)” to serve as the baseline for product/QA buy-in. Example allow/deny cases documented in living QA notes (to be ported into upcoming `devInfo/QA/guardrails-uat.md`).

### Phase 1 Notes (2025-09-26)

- **Schema & merge order**: Introduced dedicated tables `guardrail_system_policies`, `guardrail_denomination_policies`, `guardrail_tenant_policies`, and `guardrail_user_preferences`. Evaluation now has explicit merge metadata (`mergeOrder`) baked into the system baseline to guarantee application sequence (system → denomination → tenant → override → user).
- **Seeds & presets**: Seeded baseline `system-core` policy plus 10 denomination presets sourced from `App/Data/denominations.json` with stub allow-list cues. Tenants inherit via `derived_from_preset_id` prior to custom overrides.
- **RLS enforcement**: Enabled PostgreSQL row-level security on tenant policies and user preferences (`tenant_isolation_select/mod`). Policies leverage `app.set_tenant()` to scope CRUD operations and block cross-tenant reads.
- **Context wiring**: Added guardrail DbSets + entity configurations; ensured `make migrate` applied new migration `20250927045643_s9_01_guardrail_schema` and updated `SnapshotArchitecture.md` to document the new persistence layer.

### Phase 2 Notes (2025-09-27)

- **Evaluator pipeline**: Implemented `GuardrailEvaluator` with layered policy merge, preset hydration, and cache-backed preset loading. Normalizes signals, produces allow/deny/escalate decisions with trace entries, and emits metrics + security events for deny/escalate outcomes.
- **Preflight endpoint**: Added `/api/guardrails/preflight` minimal API with tenant/user authorization, policy key selection, and structured response DTOs. Endpoint now tolerates tokens using either `sub` or `ClaimTypes.NameIdentifier` identifiers.
- **Testing**: Authored integration tests covering allow, deny override, and denomination escalate flows; updated test JSON serialization to honor enum strings. Added InMemory `JsonDocument` converters for guardrail entities so the evaluator runs under the test harness.
- **Agent runtime integration**: Agent task creation can accept guardrail context; evaluations persist decision/metadata on `agent_tasks`, emit security events, and skip queueing when the verdict is deny/escalate. Worker re-checks stored decisions before execution for defense-in-depth.
- **Agent task regression coverage**: Added `AgentTasksGuardrailTests` to verify deny and escalate decisions persist metadata, return structured responses, and block worker execution; fixtures reuse the evaluator context helpers so scenarios stay deterministic.
- **Next step**: Surface guardrail outcomes in agent task UI flows and expand coverage for guardrail metadata serialization.
