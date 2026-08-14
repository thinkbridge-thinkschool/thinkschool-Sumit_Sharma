@description('Primary location for all resources')
param location string

@description('Unique token used to build deterministic resource names')
param resourceToken string

param tags object = {}

@description('Id of the principal to grant Container Registry push access to. Leave blank when unknown.')
param principalId string = ''

@secure()
@description('JWT signing key for the internal auth scheme (Jwt:Key)')
param jwtKey string

var acrPullRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)

var acrPushRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '8311e382-0749-4cb8-b61a-304f252e45ec'
)

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${resourceToken}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// Workspace-based Application Insights component for QuotesApi request/
// dependency/exception telemetry (the `requests` table queried by
// app-insights-latency.kql). Backed by the same Log Analytics workspace
// already used for Container Apps platform logs.
resource appInsights 'Microsoft.Insights/components@2020-02-02-preview' = {
  name: 'appi-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: 'acr${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource quotesApiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-quotesapi-${resourceToken}'
  location: location
  tags: tags
}

resource acrPullForIdentity 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, quotesApiIdentity.id, acrPullRoleDefinitionId)
  scope: containerRegistry
  properties: {
    roleDefinitionId: acrPullRoleDefinitionId
    principalId: quotesApiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Lets `azd deploy` (running as the signed-in developer) push freshly built
// images to the registry. Skipped when no principalId is supplied.
resource acrPushForDeployer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  name: guid(containerRegistry.id, principalId, acrPushRoleDefinitionId)
  scope: containerRegistry
  properties: {
    roleDefinitionId: acrPushRoleDefinitionId
    principalId: principalId
    principalType: 'User'
  }
}

resource quotesApi 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'quotes-api-${resourceToken}'
  location: location
  // The azd-service-name tag is how `azd deploy` finds this Container App
  // for the `quotesapi` service defined in azure.yaml.
  tags: union(tags, { 'azd-service-name': 'quotesapi' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${quotesApiIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: quotesApiIdentity.id
        }
      ]
      secrets: [
        {
          name: 'jwt-key'
          value: jwtKey
        }
        {
          name: 'appinsights-connection-string'
          value: appInsights.properties.ConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          // Placeholder image until `azd deploy` publishes the real
          // quotes-api image built from QuotesApi.csproj.
          name: 'quotes-api'
          image: 'mcr.microsoft.com/dotnet/aspnet:10.0-alpine'
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'Jwt__Key'
              secretRef: 'jwt-key'
            }
            {
              name: 'ConnectionStrings__DefaultConnection'
              value: 'Data Source=quotes.db'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              secretRef: 'appinsights-connection-string'
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  dependsOn: [
    acrPullForIdentity
  ]
}

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.properties.loginServer
output AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = containerAppsEnvironment.id
output SERVICE_QUOTESAPI_ENDPOINT_URL string = 'https://${quotesApi.properties.configuration.ingress.fqdn}'
// Connection string itself is intentionally not exposed as a plain output;
// read it via the `appinsights-connection-string` Container App secret or
// `az monitor app-insights component show` once deployed.
output AZURE_APPLICATION_INSIGHTS_NAME string = appInsights.name
