using System.Collections.Immutable;

namespace Client
{ 
    /// <summary>
    /// A batch of file hashes
    /// 
    /// Immutable Data Transfer Object
    /// </summary>
    public class FileHashBatch
    {
        public FileHashBatch(string batchName, ImmutableDictionary<string, ImmutableArray<byte>> data)
        {
            this.BatchName = batchName;
            this.Data = data;
        }

        public string BatchName { get; }

        public ImmutableDictionary<string, ImmutableArray<byte>> Data { get; }

    }
}
