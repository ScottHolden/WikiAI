using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using WikiAI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.BindConfiguration<ConfluenceClientConfig>();
builder.Services.BindConfiguration<AzureOpenAIConfig>();

builder.Services.AddSingleton<WikiCopilot>();
builder.Services.AddSingleton<IWikiClient>(
	x => x.GetRequiredService<ConfluenceClientConfig>().IsConfigured ? 
			x.GetRequiredService<ConfluenceClient>() : 
			x.GetRequiredService<FakeWiki>()
);

builder.Services.AddSingleton<IVectorChunker, WholePageVectorChunker>();

builder.Services.AddSingletonIfConfigured<IMongoDatabase, MongoConfig>((config) =>
{
	var client = new MongoClient(config.MONGO_CONNECTION_STRING);
	var dbName = string.IsNullOrWhiteSpace(config.MONGO_DATABASE_NAME) ? "wiki" : config.MONGO_DATABASE_NAME;
	return client.GetDatabase(dbName);
});
builder.Services.AddSingletonIfConfigured<SearchClient, AISearchConfig>((config) => new SearchClient(
	new Uri(config.AISEARCH_ENDPOINT), 
	config.AISEARCH_INDEX, 
	new AzureKeyCredential(config.AISEARCH_KEY)
));
builder.Services.AddSingletonIfConfigured<SearchIndexClient, AISearchConfig>((config) => new SearchIndexClient(
	new Uri(config.AISEARCH_ENDPOINT), 
	new AzureKeyCredential(config.AISEARCH_KEY)
));


// Need to refactor this, messy way of doing optional service resolution
builder.Services.AddSingleton<VectorSearch>(x =>
{
	var mongo = x.GetService<IMongoDatabase>();
	if (mongo == null) return null!;

	var chatCompletion = x.GetRequiredService<AzureOpenAIChatCompletion>();
	return new VectorSearch(chatCompletion, mongo);
});
builder.Services.AddSingleton<AzureAISearch>(x =>
{
	var searchClient = x.GetService<SearchClient>();
	if (searchClient == null) return null!;

	var searchIndexClient = x.GetService<SearchIndexClient>();
	if (searchIndexClient == null) return null!;

	var wikiClient = x.GetRequiredService<IWikiClient>();
	var chatCompletion = x.GetRequiredService<AzureOpenAIChatCompletion>();
	return new AzureAISearch(searchIndexClient, searchClient, wikiClient, chatCompletion);
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
app.MapPost("/api/init", async (
		[FromServices] IWikiClient wikiClient, 
		[FromServices] IVectorChunker chunker, 
		[FromServices] AzureAISearch? aiSearch, 
		[FromServices] VectorSearch? vectorSearch)
	=>
	{
		if (aiSearch == null && vectorSearch == null) return "Init skipped, neither Vector Search or AI Search was configured";
		await InitAsync(wikiClient, chunker, aiSearch, vectorSearch);
		if (vectorSearch == null) return "Vector Search init complete (AI Search was skipped as not configured)";
		if (aiSearch == null) return "AI Search init complete (Vector Search was skipped as not configured)";
		return "Vector Search & AI Search init complete";
	}
);

// Auto init!
var initTask = InitFromServicesAsync(app.Services);
app.Run();


static async Task InitAsync(IWikiClient wikiClient, IVectorChunker chunker, AzureAISearch? aiSearch, VectorSearch? vectorSearch)
{
	if (aiSearch == null && vectorSearch == null) return;
	var pagesToInit = await wikiClient.GetAllPagesAsync();
	var chunks = await BuildChunksAsync(chunker, pagesToInit);
	if (vectorSearch != null) await vectorSearch.BuildDatabaseAsync(chunks);
	if (aiSearch != null) await aiSearch.BuildIndexAsync(pagesToInit);
}
static async Task<List<VectorChunk>> BuildChunksAsync(IVectorChunker chunker, IReadOnlyList<WikiPage> pages)
{
	List<VectorChunk> chunks = new();
	foreach(var page in pages)
	{
		var chunk = await chunker.ChunkAsync(page);
		chunks.Add(chunk);
		// Don't overload the chunker
		await Task.Delay(Random.Shared.Next(200, 500));
	}
	return chunks;
}
static Task InitFromServicesAsync(IServiceProvider serviceProvider)
	=> InitAsync(serviceProvider.GetRequiredService<IWikiClient>(),
					serviceProvider.GetRequiredService<IVectorChunker>(),
					serviceProvider.GetService<AzureAISearch>(),
					serviceProvider.GetService<VectorSearch>());
public record AskRequest(string question);
