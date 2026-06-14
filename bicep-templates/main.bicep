@description('Azure region for all resources')
param location string

@description('Deployment environment (e.g., dev, prod)')
param environment string

@description('Object ID of the admin who will manage Key Vault secrets via the Azure Portal')
param keyVaultAdminObjectId string

var locationCode = replace(toLower(location), ' ', '')
var uaiName = 'job-seeker-uai-${environment}'
var keyVaultName = 'job-seeker-kv-${environment}'
var storageAccountName = 'jobseeker${environment}${locationCode}'

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2025-01-31-preview' = {
  name: uaiName
  location: location
}

resource keyVault 'Microsoft.KeyVault/vaults@2024-12-01-preview' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: keyVaultAdminObjectId
        permissions: {
          secrets: ['all']
          keys: ['all']
          certificates: ['all']
        }
      }
      {
        tenantId: subscription().tenantId
        objectId: managedIdentity.properties.principalId
        permissions: {
          secrets: ['get', 'list']
        }
      }
    ]
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource jobSearchResultsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: 'job-seeker-results'
  properties: {
    publicAccess: 'None'
  }
}

resource resumeContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: 'resume'
  properties: {
    publicAccess: 'None'
  }
}

resource coverLetterContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: 'cover-letter'
  properties: {
    publicAccess: 'None'
  }
}

resource criteriaContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: 'criteria'
  properties: {
    publicAccess: 'None'
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2025-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource jobLinksTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: tableService
  name: 'JobLinks'
}

resource customersTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: tableService
  name: 'Customers'
}

module webAppDeploy './webapp.bicep' = {
  name: 'webAppDeploy'
  params: {
    location: location
    environment: environment
    userAssignedIdentityId: managedIdentity.id
    userAssignedIdentityClientId: managedIdentity.properties.clientId
    tableServiceEndpoint: storageAccount.properties.primaryEndpoints.table
    keyVaultUri: keyVault.properties.vaultUri
  }
}

module containerEnvDeploy './container-env.bicep' = {
  name: 'containerEnvDeploy'
  params: {
    location: location
    environment: environment
    managedIdentityId: managedIdentity.id
    managedIdentityPrincipalId: managedIdentity.properties.principalId
  }
}

output loginServer string = containerEnvDeploy.outputs.loginServer
output containerAppEnvironmentId string = containerEnvDeploy.outputs.containerAppEnvironmentId
output managedIdentityId string = managedIdentity.id
output managedIdentityClientId string = managedIdentity.properties.clientId
output keyVaultUri string = keyVault.properties.vaultUri
output apiUrl string = webAppDeploy.outputs.apiUrl
output appServiceName string = webAppDeploy.outputs.appServiceName
