#!/usr/bin/env bash
# CLI equivalent of the step 0.1 setup (resource group, Storage, Event Grid, monitoring, AI, SQL, roles).
# Reference only: the resources already exist. Names marked "must be globally unique" need your own suffix.
# Prerequisite: `az login`. Run with `bash infra/setup.sh`.
set -euo pipefail

RG=rg-three-way-match
LOC=swedencentral
AOAI_LOC=francecentral          # Azure OpenAI quota was 0 in swedencentral on this subscription
TAG="project=three-way-match-copilot"
STORAGE=stthreewaymatch         # must be globally unique, 3-24 lowercase letters/digits
SQL_SERVER=sql-three-way-match-3114   # must be globally unique
SQL_DB=sqldb-three-way-match

SUB=$(az account show --query id -o tsv)
ME_ID=$(az ad signed-in-user show --query id -o tsv)
ME_UPN=$(az ad signed-in-user show --query userPrincipalName -o tsv)

# --- Resource providers (new subscriptions have most of these unregistered) ---
for p in Microsoft.Storage Microsoft.EventGrid Microsoft.Sql Microsoft.Insights \
         Microsoft.OperationalInsights Microsoft.CognitiveServices; do
  az provider register -n "$p" --wait
done

# --- Resource group ---
az group create -n "$RG" -l "$LOC" --tags "$TAG"

# --- Budget: EUR 100, alerts at 50/80/100 % of actual spend ---
# Created in the Portal (Cost Management -> Budgets, scope = the resource group).
# `az consumption budget create` can do the same but is a preview command; use the Portal.

# --- Storage: account, containers, queue ---
az storage account create -n "$STORAGE" -g "$RG" -l "$LOC" --sku Standard_LRS --kind StorageV2 \
  --allow-blob-public-access false --min-tls-version TLS1_2 --tags "$TAG"
STORAGE_ID=$(az storage account show -n "$STORAGE" -g "$RG" --query id -o tsv)

az role assignment create --assignee-object-id "$ME_ID" --assignee-principal-type User \
  --role "Storage Blob Data Contributor" --scope "$STORAGE_ID"
az role assignment create --assignee-object-id "$ME_ID" --assignee-principal-type User \
  --role "Storage Queue Data Contributor" --scope "$STORAGE_ID"
sleep 60   # role assignments take a moment to propagate

for c in incoming training; do
  az storage container create --account-name "$STORAGE" --name "$c" --auth-mode login --public-access off
done
az storage queue create --account-name "$STORAGE" --name documents --auth-mode login
# `documents-poison` is created automatically by the Functions runtime after 5 failed attempts.

# --- Event Grid: blob created in incoming/ -> queue `documents` ---
az eventgrid system-topic create -g "$RG" -n evgt-three-way-match -l "$LOC" \
  --topic-type Microsoft.Storage.StorageAccounts --source "$STORAGE_ID"
az eventgrid system-topic event-subscription create -g "$RG" --system-topic-name evgt-three-way-match \
  -n blob-created-to-queue --endpoint-type storagequeue \
  --endpoint "$STORAGE_ID/queueServices/default/queues/documents" \
  --included-event-types Microsoft.Storage.BlobCreated \
  --subject-begins-with /blobServices/default/containers/incoming/

# --- Monitoring: Log Analytics + Application Insights ---
az monitor log-analytics workspace create -g "$RG" -n log-three-way-match -l "$LOC" --tags "$TAG"
WS_ID=$(az monitor log-analytics workspace show -g "$RG" -n log-three-way-match --query id -o tsv)
az monitor app-insights component create -g "$RG" -a appi-three-way-match -l "$LOC" \
  --workspace "$WS_ID" --kind web --application-type web --tags "$TAG"

# --- Document Intelligence (free tier). Custom domain is required for Entra ID auth. ---
az cognitiveservices account create -g "$RG" -n di-three-way-match -l "$LOC" \
  --kind FormRecognizer --sku F0 --custom-domain di-three-way-match --tags "$TAG" --yes
DI_ID=$(az cognitiveservices account show -g "$RG" -n di-three-way-match --query id -o tsv)
az role assignment create --assignee-object-id "$ME_ID" --assignee-principal-type User \
  --role "Cognitive Services User" --scope "$DI_ID"

# --- Azure OpenAI + one small model (GlobalStandard: regional Standard quota was 0) ---
az cognitiveservices account create -g "$RG" -n aoai-three-way-match -l "$AOAI_LOC" \
  --kind OpenAI --sku S0 --custom-domain aoai-three-way-match --tags "$TAG" --yes
az cognitiveservices account deployment create -g "$RG" -n aoai-three-way-match \
  --deployment-name gpt-4.1-mini --model-name gpt-4.1-mini --model-version 2025-04-14 \
  --model-format OpenAI --sku-name GlobalStandard --sku-capacity 30
AOAI_ID=$(az cognitiveservices account show -g "$RG" -n aoai-three-way-match --query id -o tsv)
az role assignment create --assignee-object-id "$ME_ID" --assignee-principal-type User \
  --role "Cognitive Services OpenAI User" --scope "$AOAI_ID"

# --- Azure SQL: Entra-only auth (no SQL password), free serverless database ---
az sql server create -g "$RG" -n "$SQL_SERVER" -l "$LOC" --enable-ad-only-auth \
  --external-admin-principal-type User --external-admin-name "$ME_UPN" --external-admin-sid "$ME_ID" \
  --minimal-tls-version 1.2 --tags "$TAG"
az sql db create -g "$RG" -s "$SQL_SERVER" -n "$SQL_DB" \
  --edition GeneralPurpose --family Gen5 --compute-model Serverless --capacity 2 --min-capacity 0.5 \
  --auto-pause-delay 60 --use-free-limit --free-limit-exhaustion-behavior AutoPause \
  --backup-storage-redundancy Local --tags "$TAG"

# Firewall: your current IP (update it when your IP changes) + Azure services (for Functions / Container Apps).
MY_IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create -g "$RG" -s "$SQL_SERVER" -n chris-dev \
  --start-ip-address "$MY_IP" --end-ip-address "$MY_IP"
az sql server firewall-rule create -g "$RG" -s "$SQL_SERVER" -n AllowAzureServices \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

echo "Done. Subscription: $SUB"
