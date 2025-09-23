# Grafana Dashboards (Story 7)

This directory contains JSON dashboards and Prometheus alert rules for Auth observability.

Contents

- `dashboards/auth-overview.json` – high‑level auth health (success %, failures by reason, rate limiter outcomes, security events, latency, key rotation).
- `dashboards/auth-security.json` – security‑focused panels (invalid/expired/reuse, failure ratios, validation failures).
- `../alerts/auth-rules.yml` – Prometheus alerting rules (import into Alertmanager / Prometheus configuration).

Metric Naming

OpenTelemetry .NET Prometheus exporter replaces `.` with `_`. Queries therefore reference names like `auth_login_success` even though the instrument was defined as `auth.login.success`.

Dashboards assume a `job` label (default `api`). Use the variable selector to target other instances.

Apply Script (Optional)

Create a Grafana API key with `Editor` permissions and export environment variables:

```bash
export GRAFANA_URL=https://grafana.example.com
export GRAFANA_API_KEY=xxxxxxxxxxxxxxxx
```

Then run:

```bash
scripts/apply-grafana-auth.sh
```

This script (to be added if needed) will POST/PUT dashboards via the HTTP API. For now manual import is acceptable in Development; IaC pipeline integration can gate on `AUTH__DASHBOARDS__APPLY_ENABLED` later.

Alerts

Prometheus rule thresholds are intentionally conservative initial baselines. Tune after observing real traffic:

- `HighLoginFailureRatio` (>30% over 5m) – watch for credential stuffing / outages.
- `RefreshReuseSpike` (>5 reuses per 5m) – token theft or automation.
- `RefreshRateLimitBlocksSpike` (>1 enforced block per 5m) – indicates limiter moved out of dry-run; validate user impact.
- `KeyRotationValidationFailure` – any failure is critical.
- `ExcessiveRefreshInvalidEvents` (>50 per 15m) – potential brute force of refresh tokens.

Next Steps

- Integrate dashboards provisioning into CI with flag `AUTH__DASHBOARDS__APPLY_ENABLED`.
- Consider adding SLO panels (login success ratio, refresh success latency budgets) and exemplar links to tracing for failed reasons.
