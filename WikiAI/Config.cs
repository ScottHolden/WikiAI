// Excuse the naming standard here
public interface IConfigurable { bool IsConfigured { get; } }
public record ConfluenceClientConfig(string CONFLUENCE_API_KEY, string CONFLUENCE_DOMAIN, string CONFLUENCE_EMAIL) : IConfigurable
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(CONFLUENCE_API_KEY) &&
                                !string.IsNullOrWhiteSpace(CONFLUENCE_DOMAIN) &&
                                !string.IsNullOrWhiteSpace(CONFLUENCE_EMAIL);
}
public record AzureOpenAIConfig(string AOAI_ENDPOINT, string AOAI_KEY, string AOAI_DEPLOYMENT_CHAT, string AOAI_DEPLOYMENT_EMBEDDING);
public record MongoConfig(string MONGO_CONNECTION_STRING, string MONGO_DATABASE_NAME) : IConfigurable
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(MONGO_CONNECTION_STRING);
}
public record AISearchConfig(string AISEARCH_ENDPOINT, string AISEARCH_KEY, string AISEARCH_INDEX) : IConfigurable
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AISEARCH_ENDPOINT) &&
                                !string.IsNullOrWhiteSpace(AISEARCH_KEY) &&
                                !string.IsNullOrWhiteSpace(AISEARCH_INDEX);
}
public record PostgresConfig(string POSTGRES_CONNECTION_STRING, string POSTGRES_DATABASE_NAME) : IConfigurable
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(POSTGRES_CONNECTION_STRING) &&
                                !string.IsNullOrWhiteSpace(POSTGRES_DATABASE_NAME);
}