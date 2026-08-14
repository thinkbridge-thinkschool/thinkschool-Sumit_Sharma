#!/usr/bin/env bash
# Day 5 - Task 3: Azure Container Apps Fundamentals
#
# This script is a REFERENCE ONLY. It does not run automatically and must
# be executed manually, command-by-command, once an Azure subscription is
# available.
#
# CURRENT STATUS (recorded during preparation):
#   - Azure CLI is installed locally (`az version` -> azure-cli 2.87.0).
#   - `az account show` fails with "Please run 'az login' to setup account."
#   - There is NO Azure subscription accessible from this machine right now.
#   - Because of this, none of the resource-creation commands below have
#     been run, and no Azure resources exist yet. Values such as
#     subscription ID, ACR login server, and FQDN are NOT known and must be
#     filled in from real `az` output when a subscription is available.
#
# Replace <PLACEHOLDER> values before running anything for real.

set -euo pipefail

# -----------------------------------------------------------------------
# 1. PREREQUISITE / LOGIN
# -----------------------------------------------------------------------

# Authenticate the CLI against an Azure AD account.
# az login

# Confirm the login succeeded and see which subscription is active.
# az account show

# If the account has more than one subscription, list them and select one.
# az account list --output table
# az account set --subscription "<SUBSCRIPTION_ID_OR_NAME>"

# -----------------------------------------------------------------------
# 2. RESOURCE GROUP CREATION
# -----------------------------------------------------------------------

# az group create \
#   --name thinkschool-rg \
#   --location centralindia

# -----------------------------------------------------------------------
# 3. CONTAINER APPS ENVIRONMENT CREATION
# -----------------------------------------------------------------------

# az containerapp env create \
#   --name thinkschool-env \
#   --resource-group thinkschool-rg \
#   --location centralindia

# -----------------------------------------------------------------------
# 4. CONTAINER IMAGE: BUILD & PUSH (QuotesApi is already configured for
#    .NET SDK container publishing - see QuotesApi.csproj:
#      ContainerImageName = quotes-api
#      ContainerImageTag  = 0.1.0
#      ContainerBaseImage = mcr.microsoft.com/dotnet/aspnet:10.0-alpine
#    No Dockerfile is required; `dotnet publish` builds the image directly.)
# -----------------------------------------------------------------------

# Create an Azure Container Registry (ACR) to host the image.
# az acr create \
#   --name <ACR_NAME> \
#   --resource-group thinkschool-rg \
#   --sku Basic

# Log the local Docker/SDK container tooling in to the ACR.
# az acr login --name <ACR_NAME>

# Publish straight to the ACR using the .NET SDK containerization support
# (adjust ContainerRegistry to the ACR login server, e.g. <ACR_NAME>.azurecr.io).
# dotnet publish QuotesApi/QuotesApi.csproj \
#   -c Release \
#   -p:PublishProfile=DefaultContainer \
#   -p:ContainerRegistry=<ACR_NAME>.azurecr.io \
#   -p:ContainerImageTag=0.1.0

# -----------------------------------------------------------------------
# 5. CONTAINER APP CREATION
#    Required environment variables / secrets for QuotesApi (see
#    appsettings.json + Auth/JwtAuthenticationOptionsFactory.cs):
#      Jwt__Key       -> REQUIRED, min 256-bit secret, app fails fast at
#                        startup ("JWT key is not configured.") without it.
#                        Pass as a Container Apps secret, not a plain env var.
#      Jwt__Issuer / Jwt__Audience -> already set in appsettings.json,
#                        override only if different per environment.
#      ConnectionStrings__DefaultConnection -> defaults to
#                        "Data Source=quotes.db" (SQLite file). This is
#                        ephemeral container filesystem storage: data will
#                        NOT survive a restart/new revision and will NOT be
#                        shared across replicas. Fine for a fundamentals
#                        exercise; a real deployment would need Azure Files
#                        mounted storage or a managed database instead.
#    The container listens on port 8080 by default (set by the
#    mcr.microsoft.com/dotnet/aspnet base image's ASPNETCORE_HTTP_PORTS),
#    so --target-port 8080 matches with no code changes required.
# -----------------------------------------------------------------------

# az containerapp create \
#   --name quotes-api \
#   --resource-group thinkschool-rg \
#   --environment thinkschool-env \
#   --image <ACR_NAME>.azurecr.io/quotes-api:0.1.0 \
#   --target-port 8080 \
#   --ingress external \
#   --registry-server <ACR_NAME>.azurecr.io \
#   --secrets jwt-key=<REPLACE_WITH_REAL_SECRET> \
#   --env-vars Jwt__Key=secretref:jwt-key

# -----------------------------------------------------------------------
# 6. VERIFICATION
# -----------------------------------------------------------------------

# Inspect the Container Apps environment.
# az containerapp env show \
#   --name thinkschool-env \
#   --resource-group thinkschool-rg \
#   --output json

# Inspect the deployed container app and confirm the /health endpoint.
# az containerapp show \
#   --name quotes-api \
#   --resource-group thinkschool-rg \
#   --output json

# curl https://<QUOTES_API_FQDN>/health
