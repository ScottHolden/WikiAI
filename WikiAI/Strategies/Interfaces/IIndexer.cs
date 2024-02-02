namespace WikiAI;

public interface IIndexer
{
	Task BuildDatabaseAsync(IReadOnlyList<VectorChunk> chunks);
}
