# Design: Autonomous pipeline throughput & loop-guard fixes

**Date:** 2026-06-15
**Author:** Claude (Opus 4.8) + ayasinovskii-art
**Status:** Proposed

## Context

The role-based autonomous dev pipeline (`manager→analyst→techlead→coder→qa→security→reviewer`)
in `.github/workflows/pipeline.yml` + `scripts/pipeline/*` + `.github/pipeline/prompts/*` is
healthy (CI green) but **throughput has stalled**: no feature merged 2026-06-14/15, issue #15
sat blocked for ~2 days, and the queue (#16/#18) starved behind it.

These files live under `.github/**` and `scripts/**`, which every pipeline role is **forbidden**
to edit (prompt guardrails). They can only be changed from outside the conveyor — i.e. by us.

Root causes (diagnosed 2026-06-15):

1. **P0 — CI breaks 2026-06-16.** `actions/checkout@v4` + `actions/setup-dotnet@v4` run on Node20,
   force-migrated to Node24 on 06-16. Fix is ready as **PR #24** (`chore/bump-actions-v5`) but unmerged.
2. **P1 — throughput.** Pipeline is single-track *and* the `7 * * * *` cron is throttled by GitHub
   (observed cadence ~2-3 h, not hourly). One issue = 7-9 ticks → ~1 day/issue, queue serial behind it.
3. **P2 — trivial human escalations.** `qa.md` blocks on `qa_iterations > 2 OR total_coding_passes > 5`.
   A *coverage-only* miss (build+tests+acceptance all green) is cheap and deterministic but burns
   `qa_iterations` and escalates to `needs-human`. The global `total_coding_passes` also conflates
   security- and qa-driven passes, so a normal issue (plan + 2 sec + 2 qa) trips the cap by itself.
   **This is exactly how #15 blocked.**
4. **P3 — queue starvation.** Only #16/#18 carry `bot:queue`. #14/#17/#19 are ready but unlabeled.
5. **P4 — leaked credential.** The live `CLAUDE_CODE_OAUTH_TOKEN` was pasted in plaintext into chat on
   2026-06-10, so it must be treated as compromised. GitHub's secret-masking only covers Actions logs
   (exact-match, best-effort) and cannot scrub a value exposed outside GitHub.

Out of scope (deferred): parallel multi-issue fan-out (YAGNI for a 2-5 item queue). OAuth token
rotation (Change 5) is performed manually by the repo owner, not by the pipeline PR.

## Design

### Change 1 — merge PR #24 (P0)

Rebase `chore/bump-actions-v5` on master if behind, ensure `build-test-coverage` is green, squash-merge.
Pure CI-runner version bump; no app code. Unblocks the 06-16 Node24 deadline.

### Change 2 — self-chaining ticks (P1)

Add a terminal `continue` job to `pipeline.yml` that re-triggers the workflow when this tick did real,
successful work, so the conveyor advances at job speed (~minutes/stage) instead of waiting on the
throttled schedule.

```yaml
  continue:
    needs: [dispatch, manager, analyst, techlead, coder, qa, security, reviewer]
    if: ${{ !failure() && !cancelled() && needs.dispatch.outputs.stage != 'idle' }}
    runs-on: ubuntu-latest
    permissions: { actions: write }
    steps:
      - env: { GH_TOKEN: ${{ github.token }} }
        run: gh workflow run pipeline.yml -R ${{ github.repository }}
```

- **No PAT needed.** `GITHUB_TOKEN` may trigger `workflow_dispatch` in the same repo (GitHub
  changelog 2022-09-08; recursion-prevention applies only to push/PR). Only `actions: write` is required.
- **Gating.** Fires only when (a) no needed job failed/cancelled and (b) the tick was a real stage
  (`stage != idle`). On a role failure the chain stops → the (kept) cron resumes later. When the queue
  drains, the next dispatch returns `idle` → exactly one trailing no-op tick, then the chain stops.
- **No overlap.** Existing `concurrency: { group: bot-pipeline, cancel-in-progress: false }` serialises
  runs; a chained run queues until the current finishes. Strictly one-at-a-time, as today — just faster.
- **Backstop.** Keep the `7 * * * *` cron as a heartbeat so a broken chain self-heals on the next tick.

### Change 3 — loop-guard tuning (P2)

Edit `qa.md` to separate failure classes and raise the global cap:

- **Hard FAIL** (build fails / tests fail / a logic acceptance criterion unmet): increment `qa_iterations`;
  block if `qa_iterations > 2 OR total_coding_passes > 8`, else return to coding. (Unchanged except the
  global cap 5→8.)
- **Coverage-only FAIL** (build + tests + all acceptance green; only NEW/changed-code coverage missing):
  increment a new `coverage_iterations`; block only if `coverage_iterations > 4 OR total_coding_passes > 8`,
  else return to coding with a precise note naming the exact uncovered path/lines and the test to add.

Init the new counter in `manager.md`'s state block (`"coverage_iterations":0`). Bump the
`total_coding_passes > 5` cap to `> 8` in all three places it appears — `qa.md:16`, `security.md:12`,
`reviewer.md:23` — for consistency.

Net: a #15-class trivial coverage gap gets up to 4 cheap auto-retries instead of escalating, and normal
sec+qa rounds no longer self-trip the global cap. Logic failures still hard-stop at `qa_iterations > 2`.

### Change 4 — seed the queue (P3)

`gh issue edit 14 17 19 --add-label bot:queue`. These carry `Depends-on: #18`, so dispatch holds them
until #18 closes (no manual gating). Keeps the conveyor fed once #16/#18 land.

### Change 5 — OAuth token rotation & hardening (P4, manual)

The leaked `CLAUDE_CODE_OAUTH_TOKEN` (P4) is rotated by the repo owner out-of-band — it needs an
interactive Anthropic login and must never transit chat, a command line, or shell history. This is the
one fix the conveyor cannot make for itself and the only one requiring a human, hence kept separate from
the automated Changes 1-4.

**Why log-scrubbing is not enough.** GitHub Actions auto-masks registered `secrets.*` in run logs as
`***`, but masking is (a) exact-match/best-effort — a transformed value (base64, url-encode, split lines)
slips past it, (b) scoped to Actions logs only, and (c) no defence against a hostile workflow printing the
secret. The token here leaked into **chat**, outside GitHub entirely, so masking is irrelevant. Once a
secret's plaintext leaves the vault, treat it as compromised and rotate.

**Rotation (owner action):**
1. `claude setup-token` — generate a fresh token (printed once).
2. `gh secret set CLAUDE_CODE_OAUTH_TOKEN -R ayasinovskii-art/financial-bot` — store write-only via the
   hidden stdin prompt (or the web UI: Settings → Secrets and variables → Actions). Never pass it via
   `--body` on the command line or echo it.
3. Revoke the old token in the Anthropic account if a revoke control exists; otherwise it expires on its own.

**Hardening invariants (going forward):**
- Reference the secret only as `${{ secrets.CLAUDE_CODE_OAUTH_TOKEN }}`; never `echo`/`cat`/print it in a `run:` step.
- Keep it write-only (set via stdin/UI) so the value never lands in shell history, a command line, or chat.
- Do not add `pull_request_target` triggers that run untrusted fork code while secrets are in scope.
- Never paste the token into issues, PRs, commits, or chat.

## Testing / verification

- `scripts/pipeline/state_test.sh` still passes (dispatch/state semantics unchanged).
- The Changes 2-3 PR runs `build-test-coverage` (builds the .NET solution — unaffected by YAML/prompt
  edits) and must be green before merge.
- After merge: `gh workflow run pipeline.yml` once, then confirm the `continue` job fired a follow-up
  run and that an `idle` tick does **not** chain.
- Change 4 verified by `gh issue view 14/17/19` showing `bot:queue` and dispatch skipping them while #18 open.
- Change 5 verified after rotation: a `gh workflow run pipeline.yml` tick authenticates and a role job
  runs green on the new token; no run depends on the old value. `git log -p`/PR/issue search shows the
  token string nowhere in the repo.

## Rollout

- Changes 2 + 3 + this spec → branch `chore/pipeline-throughput` → PR → merge after CI green.
- Change 1 (PR #24) merged separately (its own branch).
- Change 4 is label-only via `gh` (no PR).
- Change 5 (token rotation) is a manual owner action, out-of-band from the PR.

## Risks & mitigations

| Risk | Mitigation |
|------|------------|
| Runaway self-chain burning tokens | Chain only on success + `stage != idle`; idle stops it; `concurrency` serialises; cron is just a backstop. |
| A "successful" Claude no-op re-runs the same stage forever | Pre-existing risk, now bounded by loop-guards + `--allowedTools` (fixed #22); residual, accepted. |
| Loop-guard too lax → long coverage loops | `coverage_iterations` capped at 4 and `total_coding_passes` at 8. |
| YAML/prompt edit breaks a run | Reversible config; cron backstop; PR CI gate. |
| Leaked OAuth token still valid (pasted in chat 06-10) | Rotate via `claude setup-token` + `gh secret set`, revoke old, keep write-only (Change 5); log-masking does not cover the chat exposure. |
