#!/usr/bin/env bash
# Регистрация и запуск GitHub Actions runner.
# Первый запуск: нужны REPO_URL + RUNNER_TOKEN (короткоживущий registration token,
#   получить: gh api -X POST repos/<owner>/<repo>/actions/runners/registration-token --jq .token).
# Конфигурация (.runner/.credentials) сохраняется в volume — рестарты токена не требуют.
set -euo pipefail
cd /home/runner

if [[ ! -f .runner ]]; then
  : "${REPO_URL:?REPO_URL is required for first-time registration}"
  : "${RUNNER_TOKEN:?RUNNER_TOKEN is required for first-time registration (short-lived registration token)}"
  ./config.sh \
    --url "$REPO_URL" \
    --token "$RUNNER_TOKEN" \
    --name "${RUNNER_NAME:-financebot-runner}" \
    --labels "${RUNNER_LABELS:-financebot-deploy}" \
    --unattended \
    --replace
fi

exec ./run.sh
