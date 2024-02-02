namespace WikiAI;
public record VectorChunk(string PageId, string Title, string Url, string ChunkContent, int? Offset, float[] Vector);