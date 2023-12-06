using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

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
	Uri endpoint = new Uri(config.RequiredConfigValue("AOAI_ENDPOINT"));
	string key = config.RequiredConfigValue("AOAI_KEY");
	string chatDeployment = config.RequiredConfigValue("AOAI_DEPLOYMENT_CHAT");
	string embeddingDeployment = config.RequiredConfigValue("AOAI_DEPLOYMENT_EMBEDDING");
	return new AzureOpenAIChatCompletion(endpoint, key, chatDeployment, embeddingDeployment);
});
builder.Services.AddSingleton<IMongoDatabase>(x =>
{
	var config = x.GetRequiredService<IConfiguration>();
	var connectionString = config.RequiredConfigValue("MONGO_CONNECTIONSTRING");
	var client = new MongoClient(connectionString);
	var databaseName = config.ConfigValueOrDefault("MONGO_DATABASE", "wiki");
	return client.GetDatabase(databaseName); ;
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

app.Run();

public record AskRequest(string question);
