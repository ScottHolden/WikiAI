namespace WikiAI;

public interface IStrategy
{
    string Name { get; }
    string DisplayName { get; }
    Task<StrategyResponse> GetResponseAsync(string question);
}
public record StrategyResponse(Dictionary<string, SourceReference> Sources, string Notes, string? SearchTerm);
public record SourceReference(string Content, string Url, string Title);