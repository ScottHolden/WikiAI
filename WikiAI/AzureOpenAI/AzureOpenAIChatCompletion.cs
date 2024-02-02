using Azure;
using Azure.AI.OpenAI;

namespace WikiAI;

public class AzureOpenAIChatCompletion(AzureOpenAIConfig config)
{
	private readonly OpenAIClient _openAIClient = new(new Uri(config.AOAI_ENDPOINT), new AzureKeyCredential(config.AOAI_KEY));
	private readonly string _deploymentName = config.AOAI_DEPLOYMENT_CHAT;
	private readonly string _embeddingDeployment = config.AOAI_DEPLOYMENT_EMBEDDING;

	public async Task<float[]> GetEmbeddingAsync(string input)
	{
		var resp = await _openAIClient.GetEmbeddingsAsync(new EmbeddingsOptions(_embeddingDeployment, new[] { input }));
		return resp.Value.Data[0].Embedding.ToArray(); // TODO: Clean up memory usage
	}
	public async Task<string> GetChatCompletionAsync(string prompt, Dictionary<string, string> sources, string message)
	{
		var chatMessages = new ChatMessage[] {
			new(ChatRole.System, prompt),
			// Cheeky little trick to emulate data coming back from a function call
			new(ChatRole.Function, "Sources:\n" + string.Join("\n", sources.Select(x=>$"{x.Key}: \"\"\"{x.Value.ReplaceLineEndings(" ").Replace("\"","")}\"\"\""))){
				Name = "Sources"
			},
			new(ChatRole.User, message)
		};
		return await GetChatCompletionAsync(chatMessages);
	}
	public async Task<string> GetChatCompletionAsync(string prompt, string message)
	{
		var chatMessages = new ChatMessage[] {
			new(ChatRole.System, prompt),
			new(ChatRole.User, message)
		};
		return await GetChatCompletionAsync(chatMessages);
	}
	private async Task<string> GetChatCompletionAsync(ChatMessage[] chatMessages)
	{
		var chatCompletionsOptions = new ChatCompletionsOptions(_deploymentName, chatMessages)
		{
			MaxTokens = 200,
			Temperature = 0.4f
		};
		List<Exception> errors = new();
		for (int i = 0; i < 3; i++)
		{
			try
			{
				var operationResult = await _openAIClient.GetChatCompletionsAsync(chatCompletionsOptions);
				var result = operationResult.Value.Choices[0].Message.Content;
				if (!string.IsNullOrWhiteSpace(result))
				{
					return result;
				}
			}
			catch (Exception e)
			{
				errors.Add(e);
			}
		}
		throw new AggregateException(errors);
	}
}