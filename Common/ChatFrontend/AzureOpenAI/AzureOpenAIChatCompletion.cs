using Azure;
using Azure.AI.OpenAI;

public class AzureOpenAIChatCompletion
{
	private readonly OpenAIClient _openAIClient;
	private readonly string _deploymentName;
	private readonly string _embeddingDeployment;
	public AzureOpenAIChatCompletion(Uri endpoint, string key, string chatDeployment, string embeddingDeployment)
	{
		_openAIClient = new OpenAIClient(endpoint, new AzureKeyCredential(key));
		_deploymentName = chatDeployment;
		_embeddingDeployment = embeddingDeployment;
	}
	public async Task<float[]> GetEmbeddingAsync(string input)
	{
		var resp = await _openAIClient.GetEmbeddingsAsync(new EmbeddingsOptions(_embeddingDeployment, new[] { input }));
		return resp.Value.Data[0].Embedding.ToArray(); // TODO: Clean up memeory usage
	}
	public async Task<string> GetChatCompletionAsync(string prompt, Dictionary<string, string> sources, string message)
	{
		var chatMessages = new ChatMessage[] {
			new ChatMessage(ChatRole.System, prompt),
			new ChatMessage(ChatRole.Function, "Sources:\n" + string.Join("\n", sources.Select(x=>$"{x.Key}: \"\"\"{x.Value.ReplaceLineEndings(" ").Replace("\"","")}\"\"\""))){
				Name = "Sources"
			},
			new ChatMessage(ChatRole.User, message)
		};
		return await GetChatCompletionAsync(chatMessages);
	}
	public async Task<string> GetChatCompletionAsync(string prompt, string message)
	{
		var chatMessages = new ChatMessage[] {
			new ChatMessage(ChatRole.System, prompt),
			new ChatMessage(ChatRole.User, message)
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