#!/usr/bin/env bash
# Phase 9: everything that deploys the app. Run after infra/setup.sh (the group, storage, SQL, Document Intelligence and OpenAI exist).
# Prerequisites: `az login`, Docker running, the `gh` CLI logged in (optional, for the last step).
# Run it in parts if you like: every command is safe to repeat, and each block says what it makes.
# Usage: bash infra/setup-phase9.sh
set -euo pipefail

RG=rg-three-way-match
LOC=swedencentral
TAG="project=three-way-match-copilot"
STORAGE=stthreewaymatch
SQL_SERVER=sql-three-way-match-3114
SQL_DB=sqldb-three-way-match
DI=di-three-way-match
AOAI=aoai-three-way-match

ACR=acrthreewaymatch3114                 # must be globally unique, 5-50 lowercase letters/digits
IDENTITY=id-three-way-match
# The subscription allows ONE Container Apps environment in total, and Steel Defect Triage already has it. This project's app joins that environment.
CAE_RG=rg-steel-defect-triage
CAE=cae-steel-defect-triage
APP=ca-three-way-match
FUNC=func-three-way-match3114            # must be globally unique
GH_REPO=chris-loog/three-way-match-copilot

SUB=$(az account show --query id -o tsv)
TENANT=$(az account show --query tenantId -o tsv)
STORAGE_ID=$(az storage account show -n "$STORAGE" -g "$RG" --query id -o tsv)

# --- Resource providers ---
for p in Microsoft.App Microsoft.ContainerRegistry Microsoft.ManagedIdentity Microsoft.Web; do
  az provider register -n "$p" --wait
done

# --- One user-assigned managed identity, shared by the web app and the Function ---
az identity create -g "$RG" -n "$IDENTITY" -l "$LOC" --tags "$TAG"
ID_PRINCIPAL=$(az identity show -g "$RG" -n "$IDENTITY" --query principalId -o tsv)
ID_CLIENT=$(az identity show -g "$RG" -n "$IDENTITY" --query clientId -o tsv)
ID_RESOURCE=$(az identity show -g "$RG" -n "$IDENTITY" --query id -o tsv)
sleep 30   # the new identity takes a moment to appear in Entra ID

# --- Container registry (Basic, no admin user: images are pulled with the identity) ---
az acr create -g "$RG" -n "$ACR" -l "$LOC" --sku Basic --admin-enabled false --tags "$TAG"
ACR_ID=$(az acr show -g "$RG" -n "$ACR" --query id -o tsv)

# --- Roles for the identity: blobs, queues, Document Intelligence, OpenAI, registry pull ---
# Storage Blob Data Owner (not Contributor): the Functions host keeps its leases and locks in blobs and needs it.
DI_ID=$(az cognitiveservices account show -g "$RG" -n "$DI" --query id -o tsv)
AOAI_ID=$(az cognitiveservices account show -g "$RG" -n "$AOAI" --query id -o tsv)
for assignment in \
  "Storage Blob Data Owner|$STORAGE_ID" \
  "Storage Queue Data Contributor|$STORAGE_ID" \
  "Cognitive Services User|$DI_ID" \
  "Cognitive Services OpenAI User|$AOAI_ID" \
  "AcrPull|$ACR_ID"; do
  az role assignment create --assignee-object-id "$ID_PRINCIPAL" --assignee-principal-type ServicePrincipal \
    --role "${assignment%%|*}" --scope "${assignment#*|}"
done

# --- Storage: a container for the web app's data-protection keys, and one for the Function package ---
for c in dataprotection app-package-func; do
  az storage container create --account-name "$STORAGE" --name "$c" --auth-mode login --public-access off
done

# --- Database access: the identity gets read and write, nothing else ---
# This part cannot be done with az. Open the Portal query editor for $SQL_DB (as the Entra admin) and run db/grant-identity.sql.
if [[ -z "${SKIP_SQL_PROMPT:-}" ]]; then
  echo "TODO (Portal): run db/grant-identity.sql against $SQL_DB, then press Enter. (Already done? Run with SKIP_SQL_PROMPT=1.)"
  read -r _
fi

# --- Container Apps environment: reuse the existing one (see above). Nothing is created here. ---
CAE_ID=$(az containerapp env show -g "$CAE_RG" -n "$CAE" --query id -o tsv)

# --- First image: the app cannot be created without one, so build and push the first version by hand ---
az acr login --name "$ACR"
IMAGE="$ACR.azurecr.io/three-way-match-web:bootstrap"
docker build -t "$IMAGE" .
docker push "$IMAGE"

# --- The web app: external HTTPS, scale to zero, demo mode on, everything through the identity ---
SQL_CONNECTION="Server=tcp:$SQL_SERVER.database.windows.net,1433;Database=$SQL_DB;Authentication=Active Directory Default;User Id=$ID_CLIENT;Encrypt=True;Connection Timeout=90"  # 90 s: the free database auto-pauses and the first connection waits for it to resume
az containerapp create -g "$RG" -n "$APP" --environment "$CAE_ID" --image "$IMAGE" \
  --user-assigned "$ID_RESOURCE" --registry-server "$ACR.azurecr.io" --registry-identity "$ID_RESOURCE" \
  --ingress external --target-port 8080 \
  --min-replicas 0 --max-replicas 2 --cpu 0.5 --memory 1Gi \
  --env-vars \
    "AZURE_CLIENT_ID=$ID_CLIENT" \
    "THREEWAYMATCH_SQL=$SQL_CONNECTION" \
    "Storage__BlobServiceUri=https://$STORAGE.blob.core.windows.net" \
    "Storage__QueueServiceUri=https://$STORAGE.queue.core.windows.net" \
    "OpenAI__Endpoint=https://$AOAI.openai.azure.com/" \
    "Demo__Enabled=true" \
  --tags "$TAG"
APP_FQDN=$(az containerapp show -g "$RG" -n "$APP" --query properties.configuration.ingress.fqdn -o tsv)

# --- The Function app (Flex Consumption), identity-based connections, no connection strings ---
az functionapp create -g "$RG" -n "$FUNC" --storage-account "$STORAGE" \
  --flexconsumption-location "$LOC" --runtime dotnet-isolated --runtime-version 10.0 \
  --instance-memory 2048 --maximum-instance-count 10 \
  --deployment-storage-name "$STORAGE" --deployment-storage-container-name app-package-func \
  --deployment-storage-auth-type UserAssignedIdentity --deployment-storage-auth-value "$ID_RESOURCE" \
  --assign-identity "$ID_RESOURCE" --tags "$TAG"

APPI_CONNECTION=$(az monitor app-insights component show -g "$RG" -a appi-three-way-match --query connectionString -o tsv)
az functionapp config appsettings set -g "$RG" -n "$FUNC" --settings \
  "AZURE_CLIENT_ID=$ID_CLIENT" \
  "AzureWebJobsStorage__accountName=$STORAGE" \
  "AzureWebJobsStorage__credential=managedidentity" \
  "AzureWebJobsStorage__clientId=$ID_CLIENT" \
  "Storage__queueServiceUri=https://$STORAGE.queue.core.windows.net" \
  "Storage__credential=managedidentity" \
  "Storage__clientId=$ID_CLIENT" \
  "Storage__blobServiceUri=https://$STORAGE.blob.core.windows.net" \
  "THREEWAYMATCH_SQL=$SQL_CONNECTION" \
  "THREEWAYMATCH_DI_ENDPOINT=https://$DI.cognitiveservices.azure.com/" \
  "THREEWAYMATCH_DI_DELIVERY_NOTE_MODEL=prebuilt-layout" \
  "THREEWAYMATCH_DI_MIN_INTERVAL_SECONDS=3" \
  "APPLICATIONINSIGHTS_CONNECTION_STRING=$APPI_CONNECTION"

# --- GitHub Actions deploys with OIDC: an app registration with a federated credential, no secret ---
APP_REG=$(az ad app create --display-name gh-three-way-match-deploy --query appId -o tsv)
APP_REG_OBJECT=$(az ad app show --id "$APP_REG" --query id -o tsv)
az ad sp create --id "$APP_REG" >/dev/null
# GitHub now puts the numeric owner and repository IDs in the OIDC subject: repo:OWNER@OWNER_ID/REPO@REPO_ID:ref:...
# A subject without them is rejected by Entra (AADSTS700213). Older repositories without IDs would need the plain form.
OWNER_ID=$(gh api "repos/$GH_REPO" --jq '.owner.id')
REPO_ID=$(gh api "repos/$GH_REPO" --jq '.id')
SUBJECT="repo:${GH_REPO%%/*}@$OWNER_ID/${GH_REPO##*/}@$REPO_ID:ref:refs/heads/main"
az ad app federated-credential create --id "$APP_REG_OBJECT" --parameters "{
  \"name\": \"github-main\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"$SUBJECT\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"
sleep 30
RG_ID=$(az group show -n "$RG" --query id -o tsv)
az role assignment create --assignee "$APP_REG" --role Contributor --scope "$RG_ID"
az role assignment create --assignee "$APP_REG" --role AcrPush --scope "$ACR_ID"

# --- The repository variables the deploy workflow reads (IDs and names, not secrets) ---
if command -v gh >/dev/null; then
  gh variable set AZURE_CLIENT_ID --repo "$GH_REPO" --body "$APP_REG"
  gh variable set AZURE_TENANT_ID --repo "$GH_REPO" --body "$TENANT"
  gh variable set AZURE_SUBSCRIPTION_ID --repo "$GH_REPO" --body "$SUB"
  gh variable set ACR_NAME --repo "$GH_REPO" --body "$ACR"
  gh variable set CONTAINER_APP --repo "$GH_REPO" --body "$APP"
  gh variable set FUNCTION_APP --repo "$GH_REPO" --body "$FUNC"
else
  echo "Set these repository variables by hand (GitHub: Settings, Secrets and variables, Actions, Variables):"
  echo "  AZURE_CLIENT_ID=$APP_REG  AZURE_TENANT_ID=$TENANT  AZURE_SUBSCRIPTION_ID=$SUB"
  echo "  ACR_NAME=$ACR  CONTAINER_APP=$APP  FUNCTION_APP=$FUNC"
fi

echo "Done. The site will be at https://$APP_FQDN (first request waits for a cold start). Check https://$APP_FQDN/health"
