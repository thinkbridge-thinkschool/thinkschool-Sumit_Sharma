targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the azd environment used to generate a short unique hash for all resources.')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@description('Id of the principal to grant Container Registry push access to (typically the deploying user). Leave blank when unknown.')
param principalId string = ''

@secure()
@description('JWT signing key for the internal auth scheme (Jwt:Key). Set via `azd env set-secret JWT_KEY` before provisioning.')
param jwtKey string

@description('Name of the existing resource group to deploy into. Reused from the Task 3 manual deployment, since the subscription only allows one Container Apps environment per region and that environment already lives here.')
param existingResourceGroupName string = 'thinkschool-rg'

@description('Name of the existing Container Apps environment to deploy into (see resources.bicep for why this is reused rather than created).')
param existingContainerAppsEnvironmentName string = 'thinkschool-env'

@description('Name of the existing Log Analytics workspace to back Application Insights.')
param existingLogAnalyticsWorkspaceName string = 'workspace-thinkschoolrgP4YD'

var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: existingResourceGroupName
}

module resources 'resources.bicep' = {
  name: 'resources-${resourceToken}'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    principalId: principalId
    jwtKey: jwtKey
    existingContainerAppsEnvironmentName: existingContainerAppsEnvironmentName
    existingLogAnalyticsWorkspaceName: existingLogAnalyticsWorkspaceName
  }
}

output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = resources.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID
output SERVICE_QUOTESAPI_ENDPOINT_URL string = resources.outputs.SERVICE_QUOTESAPI_ENDPOINT_URL
output AZURE_APPLICATION_INSIGHTS_NAME string = resources.outputs.AZURE_APPLICATION_INSIGHTS_NAME
