public interface IVectorSearch
{
    Task<List<string>> BuildDatabaseAsync(IReadOnlyList<VectorChunk> chunks);
    Task<string[]> SearchAsync(string question, int limit = 5);
}