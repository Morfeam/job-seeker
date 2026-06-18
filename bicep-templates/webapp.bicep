@description('Azure region for all resources')
param location string

@description('Deployment environment (e.g., dev, prod)')
param environment string

@description('Resource ID of the User-Assigned Managed Identity')
param userAssignedIdentityId string

@description('Client ID of the User-Assigned Managed Identity')
param userAssignedIdentityClientId string

@description('App Service Plan SKU')
param sku string = 'B1'

@description('Azure Table Storage service endpoint URL')
param tableServiceEndpoint string

@description('Azure Blob Storage service endpoint URL')
param blobServiceEndpoint string

@description('Key Vault URI for loading secrets at runtime')
param keyVaultUri string

var appInsightsName = 'job-seeker-ai-${environment}'
var planName = 'job-seeker-plan-${environment}'
var appServiceName = 'job-seeker-api-${environment}'

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: {
    'hidden-link:${resourceId('Microsoft.Web/sites', appServiceName)}': 'Resource'
  }
  properties: {
    Application_Type: 'web'
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  properties: {
    reserved: true
  }
  sku: {
    name: sku
  }
}

resource appService 'Microsoft.Web/sites@2023-12-01' = {
  name: appServiceName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appSettings: [
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: applicationInsights.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsights.properties.ConnectionString
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: userAssignedIdentityClientId
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'AZURE_TABLE_SERVICE_ENDPOINT'
          value: tableServiceEndpoint
        }
        {
          name: 'AZURE_BLOB_SERVICE_ENDPOINT'
          value: blobServiceEndpoint
        }
        {
          name: 'KEY_VAULT_URI'
          value: keyVaultUri
        }
        {
          name: 'AZURE_SUBSCRIPTION_ID'
          value: subscription().subscriptionId
        }
        {
          name: 'AZURE_RESOURCE_GROUP'
          value: resourceGroup().name
        }
        {
          name: 'CONTAINER_JOB_NAME'
          value: 'job-seeker-job-${environment}'
        }
      ]
    }
  }
}

output apiUrl string = 'https://${appService.properties.defaultHostName}'
output appServiceName string = appService.name
