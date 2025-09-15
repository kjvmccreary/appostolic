# Appostolic — Release 1.0 (Go-to-Market) Cut

> Purpose: Define the exact feature set that constitutes the 1.0 launch of Appostolic. This reframes MVP as **v1.0 Go-to-Market** — a thin but complete product that supports lesson creation, delivery, learner engagement, and subscription management.

---

## Legend

- 🎯 1.0 Launch — must be included for market-ready release
- ➕ Post-1.0 — targeted for near-term enhancements
- 🔮 Future — longer-term differentiators

---

## Release 1.0 Cut by Pillar

| Pillar                                      | 1.0 Status           | Notes                                                                                                                                                     |
| ------------------------------------------- | -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **IAM (Users, Tenants, Roles, Invites)**    | 🎯 1.0               | Username/password, Magic Link, invites, email verification. Hardcoded roles: Owner, Admin, Creator, Approver, Learner. Google & Microsoft login are ➕.   |
| **Tenant & Org Mgmt**                       | 🎯 1.0               | Tenant creation, membership, role assignment, basic settings. Feature flags ➕.                                                                           |
| **Guardrails & Doctrine Profiles**          | 🎯 1.0               | System-level, tenant-level, denomination-level guardrails. User-level prefs ➕.                                                                           |
| **Lesson Pipeline**                         | 🎯 1.0               | Prompt entry, preflight guardrails, AI job submission, async worker, iterative refinement. Supports Lesson Plan content mode. Devotional/Sermon modes ➕. |
| **DocGen, Deliverables & Media**            | 🎯 1.0 (Docs/Slides) | Export lesson handouts and slides. Media (images/audio/video) ➕. Devotional-specific outputs ➕.                                                         |
| **Storage & Versioning**                    | 🎯 1.0               | Object storage, version history, signed URLs.                                                                                                             |
| **Search & Retrieval**                      | ➕ Post-1.0          | Tenant-scoped search/indexing of lesson/devotional content.                                                                                               |
| **Notifications & Messaging**               | 🎯 1.0 (Email only)  | Invites, verification, job-ready notifications. In-app/messaging ➕.                                                                                      |
| **Usage Metering, Plans & Entitlements**    | 🎯 1.0               | Track lesson/job usage, enforce quotas, align with plan tiers.                                                                                            |
| **Billing & Payments**                      | 🎯 1.0               | Stripe integration, subscription plans (Free/Pro/Org), entitlements, seat management.                                                                     |
| **Commerce Admin (Subscription Ops)**       | ➕ Post-1.0          | Refunds, credits, dunning, advanced subscription operations.                                                                                              |
| **Observability & Audit**                   | 🎯 1.0 (basic)       | Structured logs, OTel traces/metrics, tenant audit ledger. Expanded dashboards & retention policies ➕.                                                   |
| **Admin & Config**                          | ➕ Post-1.0          | Central feature flag management, maintenance toggles.                                                                                                     |
| **Roles & Collaboration**                   | 🎯 1.0               | Hardcoded creation→review→approval flow with Creator, Approver, Admin, Learner roles.                                                                     |
| **Lesson Sharing & Marketplace**            | 🔮 Future            | Self-published lessons/devotionals/curricula; monetization flows.                                                                                         |
| **Platform Ops & Cross-Tenant Analytics**   | ➕ Post-1.0          | SuperAdmin dashboards, aggregate metrics.                                                                                                                 |
| **Web Responsiveness**                      | 🎯 1.0               | Mobile-friendly responsive web app.                                                                                                                       |
| **Mobile Experience & Sync**                | ➕ Post-1.0          | Native mobile app with sync.                                                                                                                              |
| **Integration & API Management**            | ➕ Post-1.0          | API keys, webhooks, external connectors.                                                                                                                  |
| **Content Library & Taxonomy**              | ➕ Post-1.0          | Canonical scripture index, resource packs, original-language resources.                                                                                   |
| **Localization & Internationalization**     | 🔮 Future            | Multi-language UI and scripture translations.                                                                                                             |
| **Compliance & Policy**                     | ➕ Post-1.0          | Data export/erasure requests, open records compliance.                                                                                                    |
| **Tenant Analytics**                        | ➕ Post-1.0          | Org-level dashboards (usage, adoption, quiz completion).                                                                                                  |
| **UI Builder / Dynamic Forms**              | 🔮 Future            | Tenant-driven form/prompt customization.                                                                                                                  |
| **Security Services**                       | ➕ Post-1.0          | MFA enforcement, API key rotation, secret management.                                                                                                     |
| **Interactive Learning (Quizzes & Trivia)** | 🎯 1.0 (basic)       | AI-generated quizzes/trivia as deliverables, completion tracking, teacher recognition (gold stars). Gamification (badges, streaks, leaderboards) ➕.      |

---

## Release 1.0 Essentials

- Identity & Access (password + magic link, invites, roles)
- Tenant creation + membership
- Guardrails & denomination alignment
- Lesson Pipeline (lesson plan generation, refinement)
- DocGen (handouts + slides)
- Storage & versioning
- Notifications (email)
- Usage metering + subscription tiers (Free/Pro/Org)
- Basic audit/logging (structured logs, OTel, tenant ledger)
- Responsive web client for teachers + learners
- Basic quizzes/trivia with completion tracking + teacher recognition

---

## Post-1.0 Roadmap Highlights

- Google/Microsoft login
- Devotional & Sermon modes
- Media generation (images, audio, video)
- Search & Retrieval
- Commerce Admin console
- Tenant Analytics dashboards
- Gamification for Interactive Learning (badges, streaks, leaderboards)

## Future Differentiators

- Marketplace for self-published curricula
- Original Language Drilldown
- UI Builder / Dynamic Forms
- Advanced Compliance & Policy
- Full mobile apps
- Advanced Integrations
- Full Security Services
