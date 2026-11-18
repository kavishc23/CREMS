#!/usr/bin/env bash
set -euo pipefail
CREMS_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$CREMS_ROOT"
dotnet test tests/CREMS.Api.Tests \
  --settings tests/CREMS.Api.Tests/coverage.runsettings \
  --collect 'Code Coverage' \
  --results-directory output/coverage/backend \
  --logger 'trx;LogFileName=backend-tests.trx'
npm --prefix src/crems-web run test:coverage -- \
  --reporter=default --reporter=json \
  --outputFile=../../output/coverage/frontend-tests.json
python3 scripts/build-coverage-report.py
