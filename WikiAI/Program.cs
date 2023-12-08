using Azure;
using Azure.Search.Documents;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using WikiAI;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<WikiCopilot>();
builder.Services.AddSingleton<IWikiClient>(x =>
{
	var config = x.GetRequiredService<IConfiguration>();
	string confluenceDomain = config.RequiredConfigValue("CONFLUENCE_DOMAIN");
	string confluenceEmail = config.RequiredConfigValue("CONFLUENCE_EMAIL");
	string confluenceKey = config.RequiredConfigValue("CONFLUENCE_API_KEY");
	return new ConfluenceClient(confluenceDomain, confluenceEmail, confluenceKey);
});
builder.Services.AddSingleton<AzureOpenAIChatCompletion>(x =>
{
	var config = x.GetRequiredService<IConfiguration>();
	Uri endpoint = new(config.RequiredConfigValue("AOAI_ENDPOINT"));
	string key = config.RequiredConfigValue("AOAI_KEY");
	string chatDeployment = config.RequiredConfigValue("AOAI_DEPLOYMENT_CHAT");
	string embeddingDeployment = config.RequiredConfigValue("AOAI_DEPLOYMENT_EMBEDDING");
	return new AzureOpenAIChatCompletion(endpoint, key, chatDeployment, embeddingDeployment);
});
builder.Services.AddSingleton<SearchClient>(x =>
{
	var config = x.GetRequiredService<IConfiguration>();
	try
	{
		var endpoint = config.RequiredConfigValue("AISEARCH_ENDPOINT");
		var key = config.RequiredConfigValue("AISEARCH_KEY");
		var index = config.RequiredConfigValue("AISEARCH_INDEX");
		return new SearchClient(new Uri(endpoint), index, new AzureKeyCredential(key));
	}
	catch (Exception ex)
	{
		x.GetService<ILogger>()?.LogWarning(ex, "Missing Azure AI Search Configuration");
		return null!;
	}
});
builder.Services.AddSingleton<IMongoDatabase>(x =>
{
	var config = x.GetRequiredService<IConfiguration>();
	try
	{
		var connectionString = config.RequiredConfigValue("MONGO_CONNECTIONSTRING");
		var client = new MongoClient(connectionString);
		var databaseName = config.ConfigValueOrDefault("MONGO_DATABASE", "wiki");
		return client.GetDatabase(databaseName);
	}
	catch (Exception ex)
	{
		x.GetService<ILogger>()?.LogWarning(ex, "Missing MongoDB Configuration");
		return null!;
	}
});
// Need to refactor this, messy way of doing optional service resolution
builder.Services.AddSingleton<VectorSearch>(x =>
{
	var mongo = x.GetService<IMongoDatabase>();
	if (mongo == null) return null!;

	var wikiClient = x.GetRequiredService<IWikiClient>();
	var chatCompletion = x.GetRequiredService<AzureOpenAIChatCompletion>();
	return new VectorSearch(wikiClient, chatCompletion, mongo);
});
builder.Services.AddSingleton<AzureAISearch>(x =>
{
	var searchClient = x.GetService<SearchClient>();
	if (searchClient == null) return null!;

	var wikiClient = x.GetRequiredService<IWikiClient>();
	var chatCompletion = x.GetRequiredService<AzureOpenAIChatCompletion>();
	return new AzureAISearch(searchClient, wikiClient, chatCompletion);
});
builder.Services.AddSingleton<DirectToWikiStrategy>();
builder.Services.AddSingleton<VectorSearchWikiStrategy>(x =>
{
	var vectorSearch = x.GetService<VectorSearch>();
	if (vectorSearch == null) return null!;

	var wikiClient = x.GetRequiredService<IWikiClient>();
	return new VectorSearchWikiStrategy(wikiClient, vectorSearch);
});
builder.Services.AddSingleton<AzureAISearchStrategy>(x =>
{
	var aiSearch = x.GetService<AzureAISearch>();
	if (aiSearch == null) return null!;

	return new AzureAISearchStrategy(aiSearch);
});

var app = builder.Build();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/ask", async ([FromBody] AskRequest req, [FromServices] WikiCopilot wc, string? engine)
	=> engine?.ToLower() switch
	{
		"vector" => await wc.AskWithVectorSearchAsync(req.question),
		"aisearch" => await wc.AskWithAzureAISearchAsync(req.question),
		_ => await wc.AskDirectToWikiAsync(req.question)
	}
);

app.MapPost("/api/vector/init", async ([FromServices] VectorSearch? vs) =>
{
	if (vs == null) return new List<string> { "Couldn't init vector db, missing configuration" };
	return await vs.BuildDatabaseAsync();
});
app.MapPost("/api/aisearch/init", async ([FromServices] AzureAISearch? ais) =>
{
	if (ais == null) return new List<string> { "Couldn't init Azure AI Search, missing configuration" };
	return await ais.BuildIndexAsync();
});

app.Run();

public record AskRequest(string question);