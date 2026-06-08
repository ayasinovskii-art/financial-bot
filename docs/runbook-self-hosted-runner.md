# Self-hosted deploy runner runbook

The deploy workflow (`.github/workflows/deploy.yml`) runs ONLY on this runner,
ONLY on push to `master` (i.e. after a human merges a PR). It never executes
code from bot PRs.

## Install (on the bot host)

1. Settings → Actions → Runners → New self-hosted runner. Follow the shown
   `./config.sh` command; when prompted for labels add: `financebot-deploy`.
2. Run as a service so it survives reboots:
   - Linux: `sudo ./svc.sh install && sudo ./svc.sh start`
3. Run the runner as a low-privilege user that is a member of the `docker` group
   (NOT root). It needs: docker, curl, and read access to the repo checkout.
4. Place the production `.env` in the runner's repo checkout root (`_work/...`)
   OR have the deploy step symlink it from a secured location. The `.env` is
   NEVER committed (see CLAUDE.md).

## Hardening

- This runner accepts jobs only from `push: master`. Confirm `deploy.yml` has no
  `pull_request*` trigger.
- Keep the runner host patched; restrict inbound network (deploy is outbound-only:
  it pulls jobs from GitHub).
- No GitHub deploy secrets exist — the runner uses the local `.env`. Rotate the
  `.env` Telegram/Claude tokens on the normal schedule.

## Verify

After registering, `gh api repos/ayasinovskii-art/financial-bot/actions/runners`
should list a runner with label `financebot-deploy` and status `online`.
