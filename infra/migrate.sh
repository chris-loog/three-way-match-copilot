#!/usr/bin/env bash
# Applies the EF Core migrations to the Azure SQL database as YOU (the Entra admin), with a migration bundle.
# The deployed app has no right to change the schema, so this is the only way the schema moves.
# Run it before a deploy whose commit adds a migration. The /health check after a deploy tells you if you forgot.
# Prerequisites: `az login`, your current IP allowed on the SQL server firewall (see the end of infra/setup.sh).
# Usage: bash infra/migrate.sh            (builds the bundle from your working copy, then applies it)
#        bash infra/migrate.sh ./efbundle (applies a bundle you downloaded from the deploy workflow's artifacts)
set -euo pipefail

CONNECTION="${THREEWAYMATCH_SQL:-Server=tcp:sql-three-way-match-3114.database.windows.net,1433;Database=sqldb-three-way-match;Authentication=Active Directory Default;Encrypt=True}"
BUNDLE="${1:-}"

if [[ -z "$BUNDLE" ]]; then
  dotnet tool restore
  dotnet dotnet-ef migrations bundle \
    --project src/ThreeWayMatch.Infrastructure --startup-project src/ThreeWayMatch.Infrastructure \
    --self-contained -r linux-x64 --output ./efbundle --force
  BUNDLE=./efbundle
fi

chmod +x "$BUNDLE"
"$BUNDLE" --connection "$CONNECTION"
