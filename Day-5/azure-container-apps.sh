#!/usr/bin/env bash
# Day 5 - Task 3: Azure Container Apps Fundamentals
#
# This script is a REFERENCE ONLY. It does not run automatically and must
# be executed manually, command-by-command.
#
# STATUS: executed for real against the "Azure for Students" subscription
# on 2026-08-15. Actual resource names/values from that run are filled in
# below. Re-running this end-to-end will fail on the ACR/resource-group
# name collisions unless you change the names - it is left as a record of
# what was done, not a script to blindly re-run.
#
# Resources created:
#   Resource group:            thinkschool-rg (centralindia)
#   Container Registry:        thinkschoolquotesacr.azurecr.io
#   Container Apps environment: thinkschool-env (static IP 4.213.208.142)
#   Container app:              quotes-api
#   FQDN:                       quotes-api.bravebay-90a32791.centralindia.azurecontainerapps.io
#
# Lesson learned during this run: the default connection string
# ("Data Source=quotes.db") pointed at a relative path under /app, which is
# owned by root in the mcr.microsoft.com/dotnet/aspnet:*-alpine image while
# the process runs as the non-root "app" user (uid 1654). SQLite could not
# create the file there ("SQLite Error 14: unable to open database file"),
# so the container crash-looped. Fixed by overriding
# ConnectionStrings__DefaultConnection to "Data Source=/tmp/quotes.db"
# (world-writable tmpfs) as a container app env var - see step 5 below.
#
# Also note: registry auth used the container app's system-assigned
# managed identity + an AcrPull role assignment (no ACR admin user, no
# stored credentials), configured via `az containerapp registry set`.

set -euo pipefail

# -----------------------------------------------------------------------
# 1. PREREQUISITE / LOGIN
# -----------------------------------------------------------------------

# az login
# az account show

# -----------------------------------------------------------------------
# 2. RESOURCE GROUP CREATION
# -----------------------------------------------------------------------

az group create \
  --name thinkschool-rg \
  --location centralindia

# -----------------------------------------------------------------------
# 3. CONTAINER APPS ENVIRONMENT CREATION
# -----------------------------------------------------------------------

az containerapp env create \
  --name thinkschool-env \
  --resource-group thinkschool-rg \
  --location centralindia

# -----------------------------------------------------------------------
# 4. CONTAINER IMAGE: BUILD & PUSH (QuotesApi is already configured for
#    .NET SDK container publishing - see QuotesApi.csproj:
#      ContainerImageName = quotes-api
#      ContainerImageTag  = 0.1.0
#      ContainerBaseImage = mcr.microsoft.com/dotnet/aspnet:10.0-alpine
#    No Dockerfile is required; `dotnet publish` builds the image directly.)
# -----------------------------------------------------------------------

az acr create \
  --name thinkschoolquotesacr \
  --resource-group thinkschool-rg \
  --sku Basic

az acr login --name thinkschoolquotesacr

dotnet publish QuotesApi/QuotesApi.csproj \
  -c Release \
  -p:PublishProfile=DefaultContainer \
  -p:ContainerRegistry=thinkschoolquotesacr.azurecr.io \
  -p:ContainerImageTag=0.1.0

# -----------------------------------------------------------------------
# 5. CONTAINER APP CREATION
#    Required environment variables / secrets for QuotesApi (see
#    appsettings.json + Auth/JwtAuthenticationOptionsFactory.cs):
#      Jwt__Key       -> REQUIRED, min 256-bit secret, app fails fast at
#                        startup ("JWT key is not configured.") without it.
#                        Passed as a Container Apps secret, not a plain env
#                        var. Generate with `openssl rand -base64 32`.
#      Jwt__Issuer / Jwt__Audience -> already set in appsettings.json,
#                        override only if different per environment.
#      ConnectionStrings__DefaultConnection -> overridden to
#                        "Data Source=/tmp/quotes.db" (see lesson learned
#                        above - /app is not writable by the non-root
#                        container user). This is still ephemeral
#                        container filesystem storage: data will NOT
#                        survive a restart/new revision and will NOT be
#                        shared across replicas. Fine for a fundamentals
#                        exercise; a real deployment would need Azure
#                        Files mounted storage or a managed database
#                        instead.
#    The container listens on port 8080 (ASPNETCORE_HTTP_PORTS=8080,
#    baked into the aspnet base image), so --target-port 8080 matches
#    with no code changes required.
# -----------------------------------------------------------------------

JWT_SECRET=$(openssl rand -base64 32)

az containerapp create \
  --name quotes-api \
  --resource-group thinkschool-rg \
  --environment thinkschool-env \
  --image thinkschoolquotesacr.azurecr.io/quotes-api:0.1.0 \
  --target-port 8080 \
  --ingress external \
  --registry-server thinkschoolquotesacr.azurecr.io \
  --registry-identity system \
  --secrets jwt-key="$JWT_SECRET" \
  --env-vars Jwt__Key=secretref:jwt-key ConnectionStrings__DefaultConnection="Data Source=/tmp/quotes.db"

# If `az containerapp create` reports an internal server error partway
# through, it can still leave the app half-created without registry
# credentials wired up (image falls back to the platform's
# mcr.microsoft.com/k8se/quickstart placeholder). Recover with:
#
# az role assignment create \
#   --assignee <containerapp-system-identity-principal-id> \
#   --role AcrPull \
#   --scope <acr-resource-id>
#
# az containerapp registry set \
#   --name quotes-api \
#   --resource-group thinkschool-rg \
#   --server thinkschoolquotesacr.azurecr.io \
#   --identity system
#
# az containerapp update \
#   --name quotes-api \
#   --resource-group thinkschool-rg \
#   --image thinkschoolquotesacr.azurecr.io/quotes-api:0.1.0

# -----------------------------------------------------------------------
# 6. VERIFICATION
# -----------------------------------------------------------------------

az containerapp env show \
  --name thinkschool-env \
  --resource-group thinkschool-rg \
  --output json

az containerapp show \
  --name quotes-api \
  --resource-group thinkschool-rg \
  --output json

curl https://quotes-api.bravebay-90a32791.centralindia.azurecontainerapps.io/health
