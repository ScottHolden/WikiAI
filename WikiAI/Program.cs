using Microsoft.AspNetCore.Mvc;
using WikiAI;
using WikiAI.Strategies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.BindConfiguration<ConfluenceClientConfig>();
builder.Services.BindConfiguration<AzureOpenAIConfig>();
builder.Services.BindConfiguration<MongoConfig>();
builder.Services.BindConfiguration<AISearchConfig>();
builder.Services.BindConfiguration<PostgresConfig>();

builder.Services.AddSingleton<ConfluenceClient>();
builder.Services.AddSingleton<FakeWiki>();

builder.Services.AddSingleton<WikiCopilot>();
builder.Services.AddSingleton<IWikiClient>(
    x => x.GetRequiredService<ConfluenceClientConfig>().IsConfigured ?
            x.GetRequiredService<ConfluenceClient>() :
            x.GetRequiredService<FakeWiki>()
);

builder.Services.AddSingleton<IVectorChunker, WholePageVectorChunker>();
builder.Services.AddSingleton<IndexerManager>();
builder.Services.AddSingleton<WikiSourceReferenceBuilder>();
builder.Services.AddSingleton<AzureOpenAIChatCompletion>();

// Strategies
builder.Services.AddSingleton<DirectToWikiStrategy>();
builder.Services.AddSingleton<MongoVectorSearchStrategy>();
builder.Services.AddSingleton<AzureAISearchStrategy>();
builder.Services.AddSingleton<PostgresVectorStrategy>();

builder.Services.AddConfiguredEnumerableItems<IIndexer>(
    new IsConfiguredEnumerableItem<MongoConfig, MongoVectorSearchStrategy, IIndexer>(),
    new IsConfiguredEnumerableItem<AISearchConfig, AzureAISearchStrategy, IIndexer>(),
    new IsConfiguredEnumerableItem<PostgresConfig, PostgresVectorStrategy, IIndexer>()
);
builder.Services.AddConfiguredEnumerableItems<IStrategy>(
    new AlwaysEnumerableItem<DirectToWikiStrategy, IStrategy>(),
    new IsConfiguredEnumerableItem<MongoConfig, MongoVectorSearchStrategy, IStrategy>(),
    new IsConfiguredEnumerableItem<AISearchConfig, AzureAISearchStrategy, IStrategy>(),
    new IsConfiguredEnumerableItem<PostgresConfig, PostgresVectorStrategy, IStrategy>()
);



var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();

app.MapPost("/api/ask", async ([FromBody] AskRequest req, [FromServices] WikiCopilot wc, string? engine)
    => await wc.AskViaStrategyAsync(req.question, engine)
);
app.MapGet("/api/strats", ([FromServices] WikiCopilot wc) => wc.ListStrategies());
app.MapGet("/api/init", async ([FromServices] IndexerManager indexerManager)
    =>
    {
        var count = await indexerManager.InitAsync();
        return $"Init'ed {count} indexers";
    }
);

// Auto init!
var initTask = app.Services.GetRequiredService<IndexerManager>().InitAsync();
app.MapRazorPages();
app.Run();

public record AskRequest(string question);
