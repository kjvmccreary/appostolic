# Role ↔ Permission Matrix

This table shows which hardcoded roles (`TenantAdmin`, `Approver`, `Creator`, `Learner`) grant access to specific actions and API endpoints.

| **Action / Endpoint**                                  | **TenantAdmin**                                                               | **Approver**          | **Creator**          | **Learner**                                                                      |
| ------------------------------------------------------ | ----------------------------------------------------------------------------- | --------------------- | -------------------- | -------------------------------------------------------------------------------- |
| **Tenant & Membership Management**                     | ✅ Full access (invite, list, edit, remove members; cannot remove last admin) | ❌                    | ❌                   | ❌                                                                               |
| **Invite Users (with initial role flags)**             | ✅                                                                            | ❌                    | ❌                   | ❌                                                                               |
| **Manage Tenant Settings**                             | ✅                                                                            | ❌                    | ❌                   | ❌                                                                               |
| **Lesson Creation / Draft (`POST /api/lessons`)**      | ❌                                                                            | ❌                    | ✅                   | ❌                                                                               |
| **Lesson Approve / Publish** _(future V1+)_            | ❌                                                                            | ✅                    | ❌                   | ❌                                                                               |
| **Access Admin Pages (e.g., `/studio/admin/members`)** | ✅                                                                            | ❌                    | ❌                   | ❌                                                                               |
| **View Role-Specific UI Elements**                     | ✅ Admin-only navigation                                                      | Approver-only buttons | Creator-only actions | Learner-only (reserved, currently no-op)                                         |
| **Session / JWT Flags**                                | `isAdmin=true`                                                                | `canApprove=true`     | `canCreate=true`     | `isLearner=true`                                                                 |
| **Learner Role (Reserved)**                            | ❌                                                                            | ❌                    | ❌                   | ✅ (no active permissions in v1; placeholder for future learner-facing features) |

---

## Notes

- **DB persistence**: Roles are stored in `TenantMembership.Roles` as a `[Flags] enum` (int column).
- **Code enforcement**: Permissions are hardcoded in policies/handlers (`RequireTenantAdmin`, `RequireCreator`, etc.).
- **Invariants**: At least one TenantAdmin must exist per tenant (system prevents demoting/removing the last one).
- **Future growth**: `Approver` endpoints (approve/publish) and `Learner` functionality are reserved for later sprints.
