
using WikiAI;

public class WholePageVectorChunker : IVectorChunker
{
	private readonly AzureOpenAIChatCompletion _azureOpenAI;
	public WholePageVectorChunker(AzureOpenAIChatCompletion azureOpenAI)
	{
		_azureOpenAI = azureOpenAI;
	}

	public async Task<VectorChunk> ChunkAsync(WikiPage page)
	{
		var textToVector = $"{page.Title}\n{page.Content}".Trim();
		var vector = await _azureOpenAI.GetEmbeddingAsync(textToVector);
		return new VectorChunk(page.PageId, page.Title, page.Url, page.Content, 0, vector);
	}
}
