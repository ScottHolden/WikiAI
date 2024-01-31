namespace WikiAI;

public interface IStrategy
{
	Task<StrategyResponse> GetResponseAsync(string question);
}
public interface IIndexer
{
	Task BuildDatabaseAsync(IReadOnlyList<VectorChunk> chunks);
}
public record StrategyResponse(Dictionary<string, SourceReference> Sources, string Notes, string? SearchTerm);
public record SourceReference(string Content, string Url, string Title);