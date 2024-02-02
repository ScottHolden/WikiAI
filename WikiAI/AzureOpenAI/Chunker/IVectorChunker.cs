namespace WikiAI;
public interface IVectorChunker
{
	Task<VectorChunk> ChunkAsync(WikiPage page);
}
