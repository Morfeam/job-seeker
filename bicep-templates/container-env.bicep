@description('Azure region for all resources')
param location string

@description('Deployment environment (e.g., dev, prod)')
param environment string

@description('Resource ID of the User-Assigned Managed Identity')
param managedIdentityId string

@description('Principal ID of the User-Assigned Managed Identity')
param managedIdentityPrincipalId string

var locationCode = replace(toLower(location), ' ', '')
var logAnalyticsName = 'job-seeker-law-${environment}'
var acrName = 'jobseekeracr${environment}${locationCode}'
var containerEnvName = 'job-seeker-env-${environment}'

// AcrPull built-in role
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'Free'
    }
    retentionInDays: 30
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2025-11-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, managedIdentityId, acrPullRoleId)
  scope: containerRegistry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2025-07-01' = {
  name: containerEnvName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    publicNetworkAccess: 'Disabled'
  }
}

output loginServer string = containerRegistry.properties.loginServer
output containerAppEnvironmentId string = containerAppEnvironment.id
