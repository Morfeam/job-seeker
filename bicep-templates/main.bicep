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

resource resumesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: 'resumes'
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

resource customerCriteriaTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: tableService
  name: 'CustomerCriteria'
}

resource resumesTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: tableService
  name: 'Resumes'
}

// Storage Table Data Contributor — allows the managed identity to read/write table rows
var storageTableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
resource tableRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, managedIdentity.id, storageTableDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorRoleId)
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Blob Data Contributor — allows the web app's managed identity to read job-result
// blobs and read/write/delete criteria blobs (the Criteria CRUD feature needs write access;
// Reader alone is no longer sufficient).
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
resource blobRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, managedIdentity.id, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Blob Delegator — grants permission to generate a user-delegation key,
// required for BlobServiceClient.GetUserDelegationKeyAsync when the client is
// authenticated via managed identity (no account key) — needed for the
// time-limited resume-download SAS links. Storage Blob Data Contributor alone
// does not include the generateUserDelegationKey action.
var storageBlobDelegatorRoleId = 'db58b8e5-c6ad-4a2a-8342-4190687cbf4a'
resource blobDelegatorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, managedIdentity.id, storageBlobDelegatorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDelegatorRoleId)
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

module webAppDeploy './webapp.bicep' = {
  name: 'webAppDeploy'
  params: {
    location: location
    environment: environment
    userAssignedIdentityId: managedIdentity.id
    userAssignedIdentityClientId: managedIdentity.properties.clientId
    tableServiceEndpoint: storageAccount.properties.primaryEndpoints.table
    blobServiceEndpoint: storageAccount.properties.primaryEndpoints.blob
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
output managedIdentityPrincipalId string = managedIdentity.properties.principalId
output keyVaultUri string = keyVault.properties.vaultUri
output apiUrl string = webAppDeploy.outputs.apiUrl
output appServiceName string = webAppDeploy.outputs.appServiceName
