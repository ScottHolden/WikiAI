using System.Text.RegularExpressions;

namespace WikiAI;

public class WikiCopilot(
    AzureOpenAIChatCompletion _chatCompletion,
    DirectToWikiStrategy _defaultStrategy,
    IEnumerable<IStrategy> _strategies)
{
    private const string WikiQuestionPrompt = """
		You are an intelligent assistant helping developers with questions contained within a Wiki.
		Use 'you' to refer to the individual asking the questions even if they ask with 'I'.
		Answer the following question using only the data provided in the sources below. ONLY use the data below, do NOT make up answers.
		Each source has a name followed by colon and the actual information, always include the source name for each fact you use in the response.
		Use square brackets to reference the source, for example if you use the "Source1:" source, reference it as [Source1]. Don't combine sources, list each source separately, for example [Source1][Source2].
		Answer in a a single paragraph at most including references, keep all answers simple and short.
		If you cannot answer using the sources below, say you are unable to find information within the Wiki.

		Example:
		---
		SourceX: "Azure is Microsoft's cloud"
		SourceY: "Webapps can be used to host websites"
		SourceZ: "Azure storage can be used to store blobs"
		Question: "What should I host a website on?"
		Answer: "You can host websites on Webapps[SourceX]"
		---
		""";

    public async Task<AnswerResponse> AskViaStrategyAsync(string question, string? strategyName)
        => await AskViaStrategyAsync(question, FindStrategy(strategyName));
    public async Task<AnswerResponse> AskViaStrategyAsync(string question, IStrategy strategy)
    {
        var resp = await strategy.GetResponseAsync(question);
        return await GetFormattedResponseAsync(question, resp);
    }
    private IStrategy FindStrategy(string? strategyName)
    {
        if (string.IsNullOrWhiteSpace(strategyName)) return _defaultStrategy;
        foreach (var strategy in _strategies.Where(x => x != null))
        {
            if (strategy.Name.Equals(strategyName, StringComparison.OrdinalIgnoreCase))
            {
                return strategy;
            }
        }
        return _defaultStrategy;
    }
    public IReadOnlyDictionary<string, string> ListStrategies()
        => _strategies.Where(x => x != null).ToDictionary(x => x.Name, x => x.DisplayName);

    private async Task<AnswerResponse> GetFormattedResponseAsync(string question, StrategyResponse sourcesToUse)
    {
        // If we don't have any references, we can't answer!
        var empty = new Dictionary<string, AnswerReference>();
        var emptyArray = Array.Empty<AnswerReference>();
        if (sourcesToUse.Sources.Count < 1)
        {
            var respMessage = $"Could not find any references in the wiki related to \"{question}\".";
            if (!string.IsNullOrWhiteSpace(sourcesToUse.SearchTerm))
            {
                respMessage += $" Searched for \"{sourcesToUse.SearchTerm}\"";
            }
            return new AnswerResponse(respMessage, empty, sourcesToUse.Notes, emptyArray, sourcesToUse.SearchTerm);
        }

        // Ask Azure OpenAI for a response
        string response = await _chatCompletion.GetChatCompletionAsync(WikiQuestionPrompt, sourcesToUse.Sources.ToDictionary(x => "Source" + x.Key, x => x.Value.Content), question + "\nOnly use sources provided and reference all sources.");

        // Include links to any pages references
        var refNumber = s_referenceFind.Matches(response)
                                        .Select(x => x.Groups[1].Value)
                                        .Distinct()
                                        .Where(sourcesToUse.Sources.ContainsKey)
                                        .Select((x, i) => (x, i))
                                        .ToDictionary(x => x.x, x => x.i + 1);

        var refs = refNumber
                    .Where(x => sourcesToUse.Sources.ContainsKey(x.Key))
                    .ToDictionary(x => x.Value.ToString(), x => new AnswerReference(x.Key, sourcesToUse.Sources[x.Key].Title, sourcesToUse.Sources[x.Key].Url));

        string finalResponse = s_referenceFind.Replace(response, x => refNumber.TryGetValue(x.Groups[1].Value, out int value) ? $"[{value}]" : "[Unknown]");

        var allRefs = sourcesToUse.Sources.Select(x => new AnswerReference(x.Key, x.Value.Title, x.Value.Url)).ToArray();

        return new AnswerResponse(finalResponse, refs, sourcesToUse.Notes, allRefs, sourcesToUse.SearchTerm);
    }
    private static readonly Regex s_referenceFind = new("\\[Source([^\\]]+)\\]");

}

public record AnswerResponse(string answer, Dictionary<string, AnswerReference> references, string notes, AnswerReference[] allReferences, string? searchQuery);
public record AnswerReference(string pageId, string title, string url);
