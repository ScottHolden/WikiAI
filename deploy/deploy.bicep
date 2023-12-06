@description('Location for all resources.')
param location string = resourceGroup().location

@description('A prefix to add to the start of all resource names. Note: A "unique" suffix will also be added')
param prefix string = 'wiki'

param mongoUsername string = 'wikivector'
@secure()
param mongoPassword string = '${uniqueString(newGuid())}-${uniqueString(newGuid())}'

//var uniqueNameFormat = '${prefix}-{0}-${uniqueString(resourceGroup().id, prefix)}'
var uniqueShortNameFormat = toLower('${prefix}{0}${uniqueString(resourceGroup().id, prefix)}')
var uniqueShortName = format(uniqueShortNameFormat, '')

resource cluster 'Microsoft.DocumentDB/mongoClusters@2023-09-15-preview' = {
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
  resource firewallRules 'firewallRules@2023-09-15-preview' = {
    name: 'AllowAllAzureServices'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }
}
