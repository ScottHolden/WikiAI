@description('Location for all resources.')
param location string = resourceGroup().location

@description('A prefix to add to the start of all resource names. Note: A "unique" suffix will also be added')
param prefix string = 'wiki'

param mongoUsername string = 'wikivector'
@secure()
param mongoPassword string = '${uniqueString(newGuid())}-${uniqueString(newGuid())}'

param allowAllFirewall bool = true

//var uniqueNameFormat = '${prefix}-{0}-${uniqueString(resourceGroup().id, prefix)}'
var uniqueShortNameFormat = toLower('${prefix}{0}${uniqueString(resourceGroup().id, prefix)}')
var uniqueShortName = format(uniqueShortNameFormat, '')

resource mongo 'Microsoft.DocumentDB/mongoClusters@2023-09-15-preview' = {
  name: uniqueShortName
  location: location
  properties: {
    administratorLogin: mongoUsername
    administratorLoginPassword: mongoPassword
    nodeGroupSpecs: [
        {
            kind: 'Shard'
            nodeCount: 1
            sku: 'M40'
            diskSizeGB: 128
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

resource aisearch 'Microsoft.Search/searchServices@2023-11-01' = {
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
