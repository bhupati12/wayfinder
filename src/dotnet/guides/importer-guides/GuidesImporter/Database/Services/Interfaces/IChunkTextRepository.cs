namespace GuidesImporter.Database.Services.Interfaces;

/// <summary>Reads chunk texts for the answer judge.</summary>
public interface IChunkTextRepository
{
    /// <summary>"Title — Section" plus the chunk text, keyed by chunk id.</summary>
    Task<IReadOnlyDictionary<long, string>> GetTextsAsync(IReadOnlyCollection<long> chunkIds, CancellationToken cancellationToken);

    /// <summary>Settings of the stored data, for the report: chunk count, embedding model.</summary>
    Task<(int Articles, int Chunks, string? EmbeddingModel)> DescribeAsync(CancellationToken cancellationToken);
}
