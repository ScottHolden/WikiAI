@description('Location for all resources.')
param location string = resourceGroup().location

@description('A prefix to add to the start of all resource names. Note: A "unique" suffix will also be added')
param prefix string = 'wiki'

param databaseUsername string = 'wikivector'
@secure()
param databasePassword string = '${uniqueString(newGuid())}-${uniqueString(newGuid())}'

param allowAllFirewall bool = true

param deployPostgres bool = true
param deployMongovCore bool = false
param deployAzureAISearch bool = true
param deployAzureOpenAI bool = true

param deployWikiAIContainerApp bool = true
param containerAppImage string = 'ghcr.io/scottholden/wikiai:release'

param confluenceDomain string = ''
param confluenceEmail string = ''
param confluenceApiKey string = ''

@description('Tags to apply to all deployed resources')
param tags object = {}

var uniqueNameFormat = '${prefix}-{0}-${uniqueString(resourceGroup().id, prefix)}'
var uniqueShortNameFormat = toLower('${prefix}{0}${uniqueString(resourceGroup().id, prefix)}')
var uniqueShortName = format(uniqueShortNameFormat, '')
var mongoConnectionString = 'mongodb+srv://{0}:{1}@{2}.global.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false&maxIdleTimeMS=120000'
var postgresConnectionString = 'Server={0};Database={1};Port=5432;User Id={2};Password={3};Ssl Mode=Require;'
var postgresDatabaseName = 'wiki'

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2023-06-01-preview' = if (deployPostgres) {
  name: uniqueShortName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    administratorLogin: databaseUsername
    administratorLoginPassword: databasePassword
    storage: {
      storageSizeGB: 32
    }
    createMode: 'Default'
    version: '16'
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
  }
  resource config 'configurations@2023-06-01-preview' = {
    name: 'azure.extensions'
    properties: {
      value: 'VECTOR'
      source: 'user-override'
    }
  }
  resource db 'databases@2023-06-01-preview' = {
    name: postgresDatabaseName
    properties: {
      charset: 'UTF8'
      collation: 'en_US.utf8'
    }
  }
}

resource mongo 'Microsoft.DocumentDB/mongoClusters@2023-09-15-preview' = if (deployMongovCore) {
  name: uniqueShortName
  location: location
  properties: {
    administratorLogin: databaseUsername
    administratorLoginPassword: databasePassword
    nodeGroupSpecs: [
      {
        kind: 'Shard'
        nodeCount: 1
        sku: 'M25'
        diskSizeGB: 64
        enableHa: false
      }
    ]
  }
  resource allowAzure 'firewallRules@2023-09-15-preview' = {
    name: 'AllowAllAzureServices'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }
  resource allowAll 'firewallRules@2023-09-15-preview' = if (allowAllFirewall) {
    name: 'AllowAll'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '255.255.255.255'
    }
  }
}

resource aisearch 'Microsoft.Search/searchServices@2023-11-01' = if (deployAzureAISearch) {
  name: uniqueShortName
  location: location
  sku: {
    name: 'basic'
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
    semanticSearch: 'free'
  }
}

resource openai 'Microsoft.CognitiveServices/accounts@2023-05-01' = if (deployAzureOpenAI) {
  name: format(uniqueNameFormat, 'openai')
  location: location
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: format(uniqueNameFormat, 'openai')
  }
  resource gpt35turbo16k 'deployments@2023-10-01-preview' = {
    name: 'gpt-35-turbo-16k'
    sku: {
      name: 'Standard'
      capacity: 75
    }
    properties: {
      model: {
        format: 'OpenAI'
        name: 'gpt-35-turbo-16k'
      }
      #disable-next-line BCP073
      dynamicThrottlingEnabled: true
      versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    }
  }
  resource embedding 'deployments@2023-10-01-preview' = {
    name: 'text-embedding-ada-002'
    sku: {
      name: 'Standard'
      capacity: 75
    }
    properties: {
      model: {
        format: 'OpenAI'
        name: 'text-embedding-ada-002'
        version: '2'
      }
      #disable-next-line BCP073
      dynamicThrottlingEnabled: true
      versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    }
    dependsOn: [ gpt35turbo16k ]
  }
  tags: tags
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = if (deployWikiAIContainerApp) {
  name: uniqueShortName
  location: location
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource containerAppEnv 'Microsoft.App/managedEnvironments@2022-06-01-preview' = if (deployWikiAIContainerApp) {
  name: uniqueShortName // TODO: Update
  location: location
  sku: {
    name: 'Consumption'
  }
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

resource containerApp 'Microsoft.App/containerApps@2023-05-02-preview' = if (deployWikiAIContainerApp) {
  name: uniqueShortName // TODO: Update
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      secrets: [
        {
          name: 'confluenceapikey'
          value: confluenceApiKey
        }
        {
          name: 'aoaikey'
          value: deployAzureOpenAI ? openai.listKeys().key1 : ''
        }
        {
          name: 'mongoconnectionstring'
          value: deployMongovCore ? format(mongoConnectionString, databaseUsername, databasePassword, mongo.name) : ''
        }
        {
          name: 'aisearchkey'
          value: deployAzureAISearch ? aisearch.listAdminKeys().primaryKey : ''
        }
        {
          name: 'postgresconnectionstring'
          value: deployPostgres ? format(postgresConnectionString, postgres.properties.fullyQualifiedDomainName, postgres::db.name, databaseUsername, databasePassword) : ''
        }
      ]
    }
    template: {
      revisionSuffix: 'release'
      containers: [
        {
          name: 'wikiai'
          image: containerAppImage
          resources: {
            cpu: json('.25')
            memory: '.5Gi'
          }
          env: [
            {
              name: 'CONFLUENCE_API_KEY'
              secretRef: 'confluenceapikey'
            }
            {
              name: 'CONFLUENCE_DOMAIN'
              value: confluenceDomain
            }
            {
              name: 'CONFLUENCE_EMAIL'
              value: confluenceEmail
            }
            {
              name: 'AOAI_ENDPOINT'
              value: deployAzureOpenAI ? openai.properties.endpoint : ''
            }
            {
              name: 'AOAI_KEY'
              secretRef: 'aoaikey'
            }
            {
              name: 'AOAI_DEPLOYMENT_CHAT'
              value: deployAzureOpenAI ? openai::gpt35turbo16k.name : ''
            }
            {
              name: 'AOAI_DEPLOYMENT_EMBEDDING'
              value: deployAzureOpenAI ? openai::embedding.name : ''
            }
            {
              name: 'MONGO_CONNECTION_STRING'
              secretRef: 'mongoconnectionstring'
            }
            {
              name: 'POSTGRES_CONNECTION_STRING'
              secretRef: 'postgresconnectionstring'
            }
            {
              name: 'AISEARCH_ENDPOINT'
              value: deployAzureAISearch ? format('https://{0}.search.windows.net', aisearch.name) : ''
            }
            {
              name: 'AISEARCH_KEY'
              secretRef: 'aisearchkey'
            }
            {
              name: 'AISEARCH_INDEX'
              value: 'wiki'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http-requests'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}
