@description('Azure region for all resources')
param location string

@description('Deployment environment (e.g., dev, prod)')
param environment string

@description('Full container image reference (e.g., registry.azurecr.io/job-seeker:tag)')
param imageName string

@description('ACR login server FQDN')
param loginServer string

@description('Resource ID of the Container App Environment')
param containerAppEnvironmentId string

@description('Resource ID of the User-Assigned Managed Identity')
param managedIdentityId string

@description('Client ID of the User-Assigned Managed Identity')
param managedIdentityClientId string

@description('Key Vault URI (e.g., https://job-seeker-kv-dev.vault.azure.net/)')
param keyVaultUri string

var jobName = 'job-seeker-job-${environment}'

resource containerAppJob 'Microsoft.App/jobs@2025-02-02-preview' = {
  name: jobName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityId}': {}
    }
  }
  properties: {
    environmentId: containerAppEnvironmentId
    configuration: {
      triggerType: 'Schedule'
      replicaTimeout: 1800
      replicaRetryLimit: 3
      scheduleTriggerConfig: {
        cronExpression: '0 14 * * *'
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          server: loginServer
          identity: managedIdentityId
        }
      ]
      secrets: [
        {
          name: 'storage-connection-string'
          keyVaultUrl: '${keyVaultUri}secrets/storage-connection-string'
          identity: managedIdentityId
        }
        {
          name: 'resend-api-key'
          keyVaultUrl: '${keyVaultUri}secrets/resend-api-key'
          identity: managedIdentityId
        }
        {
          name: 'recipient-email'
          keyVaultUrl: '${keyVaultUri}secrets/recipient-email'
          identity: managedIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'job-seeker'
          image: imageName
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'AZURE_STORAGE_CONNECTION_STRING'
              secretRef: 'storage-connection-string'
            }
            {
              name: 'RESEND_API_KEY'
              secretRef: 'resend-api-key'
            }
            {
              name: 'RECIPIENT_EMAIL'
              secretRef: 'recipient-email'
            }
          ]
        }
      ]
    }
  }
}
