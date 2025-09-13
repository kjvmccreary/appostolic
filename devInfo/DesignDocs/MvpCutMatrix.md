# Appostolic — Capabilities → MVP Cut Matrix

> Purpose: Take the Core Pillars (long‑term) and map which parts are **In MVP**, **Post‑MVP (v1)**, and **Future**. This guides coding focus without cluttering the high‑level architecture doc.

---

## Legend

- ✅ In MVP (must be present for initial launch)
- ⏩ Post‑MVP (v1; important soon after launch)
- 🔮 Future (valuable later, not required for initial adoption)

---

## Core Pillars vs. MVP Cut

| Pillar                                    | MVP Cut                 | Notes                                                                                                                                        |
| ----------------------------------------- | ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| **IAM (Users, Tenants, Roles, Invites)**  | ✅ In MVP               | Core auth flows (username/password, magic link), hardcoded roles (Owner/Admin/Creator/Approver/Learner). Google + Microsoft login can be ⏩. |
| **Tenant & Org Mgmt**                     | ✅ In MVP               | Tenant creation, membership, role assignment, basic settings. Feature flags ⏩.                                                              |
| **Guardrails & Doctrine Profiles**        | ✅ In MVP               | System + tenant overrides; denomination profiles. User‑level prefs can be ⏩.                                                                |
| **Lesson Pipeline**                       | ✅ In MVP               | Job submission, preflight guardrails, async worker, outputs to storage. Iterative refinement loops included.                                 |
| **DocGen, Deliverables & Media**          | ✅ In MVP (Docs/Slides) | Handouts + slides required. Media (images/audio/video) ⏩.                                                                                   |
| **Storage & Versioning**                  | ✅ In MVP               | Object storage, versioning, signed URLs.                                                                                                     |
| **Search & Retrieval**                    | ⏩ Post‑MVP             | Per‑tenant index for artifacts and summaries.                                                                                                |
| **Notifications & Messaging**             | ✅ In MVP (Email only)  | Outbox + email templates (invites, verification, job ready). In‑app/messaging ⏩.                                                            |
| **Usage Metering, Plans & Entitlements**  | ✅ In MVP               | Track lesson/job usage, enforce quotas.                                                                                                      |
| **Billing & Payments**                    | ✅ In MVP               | Stripe integration, plans, entitlements, seats.                                                                                              |
| **Commerce Admin**                        | ⏩ Post‑MVP             | Admin UI for subscription ops (refunds, adjustments, credits).                                                                               |
| **Observability & Audit**                 | ✅ In MVP (basic)       | Logs + metrics + audit ledger for IAM/lesson events. Admin console ⏩.                                                                       |
| **Admin & Config**                        | ⏩ Post‑MVP             | Feature flags, maintenance toggles.                                                                                                          |
| **Agents Runtime**                        | 🔮 Future               | Useful for research/automation, not needed for core MVP.                                                                                     |
| **Roles & Collaboration (Hardcoded)**     | ✅ In MVP               | Creator, Approver, Admin, Learner. No custom roles.                                                                                          |
| **Lesson Sharing & Marketplace**          | 🔮 Future               | Community/paid marketplace later.                                                                                                            |
| **Platform Ops & Cross‑Tenant Analytics** | ⏩ Post‑MVP             | SuperAdmin dashboards. Not launch blocker.                                                                                                   |
| **Web Responsiveness**                    | ✅ In MVP               | Mobile‑friendly web app required.                                                                                                            |
| **Mobile Experience & Sync**              | ⏩ Post‑MVP             | Native mobile app comes later.                                                                                                               |
| **Integration & API Management**          | ⏩ Post‑MVP             | API keys, webhooks, connectors.                                                                                                              |
| **Content Library & Taxonomy**            | ⏩ Post‑MVP             | Canonical scripture index + resource packs.                                                                                                  |
| **Localization & Internationalization**   | 🔮 Future               | Locale packs + scripture translations.                                                                                                       |
| **Compliance & Policy**                   | ⏩ Post‑MVP             | Data export/erasure requests; open records compliance.                                                                                       |
| **Tenant Analytics**                      | ⏩ Post‑MVP             | Tenant dashboards (lesson counts, adoption).                                                                                                 |
| **UI Builder / Dynamic Forms**            | 🔮 Future               | Drag/drop form builder.                                                                                                                      |
| **Security Services**                     | ⏩ Post‑MVP             | MFA, API key rotation, secret mgmt.                                                                                                          |

---

## MVP Essentials (short list)

- IAM (password + magic link, invites, roles)
- Tenant & Org Mgmt
- Guardrails & Denomination Profiles
- Lesson Pipeline (with refinement)
- DocGen (handouts + slides)
- Storage & Versioning
- Notifications (email)
- Usage Metering + Billing
- Roles (hardcoded)
- Web Responsiveness (mobile‑friendly web)
- Observability (logs/metrics)

These form the “thin but whole” platform to take to market.
