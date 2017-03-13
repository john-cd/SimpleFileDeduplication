using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using static System.String;

// ImmutableDictionary<string, ImmutableArray<byte>>> ProcessFileBatchAsync(IImmutableQueue<string> filePaths

namespace Client
{
    internal class Client
    {
        private const int _capacity = 2_000_000; // the number of items you expect to add to the bloom filter

        /// <summary>
        /// Set of all file hashes, stored in a Bloom Filter, which is very compact.
        /// </summary>
        private Filter<byte[]> _fileHashesBloomFilter = new Filter<byte[]>(_capacity);

        private readonly string _rootFolderPath;

        public IList<string> Duplicates { get; } = new List<string>();

        public Client(string rootFolderPath)
        {
            this._rootFolderPath = rootFolderPath;
        }

        public async Task Run()
        {
            // Lazily generate file batches, then... 
            var _tasks = GenerateFilePathBatches(this._rootFolderPath)                           // batches are immutable, thus thread-safe
                .Select((batch, index) => Task.Run(() => ProcessFileBatch($"B{index}", batch)))  // schedule CPU-bound hashing on Threadpool
                .ToList();

            // Interleaving:
            // Repeat the following until no tasks remain: 
            //    - Grab the first one that finishes. 
            //    - Retrieve the results from the task 
            //    - Remove the task from the list. 
            //    - Merge the results into the bloom filter.
            while (_tasks.Count() > 0)
            {
                var nextCompletedTask = await Task.WhenAny(_tasks);
                _tasks.Remove(nextCompletedTask); // not ideal because O(number of batches^2); solution in https://blogs.msdn.microsoft.com/pfxteam/2012/08/02/processing-tasks-as-they-complete/
                try
                {
                    var results = await nextCompletedTask;
                    AddHashesAndCheckDuplicates(results);
                }
                catch (OperationCanceledException ex)
                {
                    Trace.WriteLine(ex); /* ignore */
                }
            }
        }

        /// <summary>
        /// Generate batches of files with roughly the same number of bytes.
        /// </summary>
        /// <param name="rootFolderPath">the path of the root folder</param>
        /// <param name="rootFolderPath">the maximum size of a batch</param>
        /// <returns>immutable queue of file paths</returns>
        internal IEnumerable<IImmutableQueue<string>> GenerateFilePathBatches(string rootFolderPath, long maxBatchBytes = 5 * 1024 * 1024) // max artificially low for demo purposes
        {
            if (IsNullOrWhiteSpace(rootFolderPath))
                throw new ArgumentNullException(nameof(rootFolderPath));

            // retrieve all file paths; 
            var rootFolder = new DirectoryInfo(rootFolderPath);
            var allFileInfos = rootFolder.EnumerateFiles("*.*", SearchOption.AllDirectories).OrderBy(fi => fi.Length);

            // local function 
            IEnumerable<IImmutableQueue<string>> Generator(IOrderedEnumerable<FileInfo> fileInfos)
            {
                long cumulativeBytes = 0;
                var batch = new Queue<string>();

                foreach (var fi in fileInfos)
                {
                    batch.Enqueue(fi.FullName);
                    cumulativeBytes += fi.Length;
                    if (cumulativeBytes >= maxBatchBytes)
                    {
                        Trace.WriteLine($"---------------\nBatch size: {cumulativeBytes}");
                        TraceDetails(batch);
                        yield return ImmutableQueue.CreateRange<string>(batch);
                        // reset
                        batch.Clear();
                        cumulativeBytes = 0L;
                    }
                }
                Trace.WriteLine($"---------------\nBatch size: {cumulativeBytes}");
                TraceDetails(batch);
                yield return ImmutableQueue.CreateRange<string>(batch);
            }
            //
            return Generator(allFileInfos);
        }

        private void TraceDetails(IEnumerable<string> batch)
        {
            Trace.Indent();
            foreach (var fullname in batch)
                Trace.WriteLine(fullname);
            Trace.Unindent();
        }

        /// <summary>
        /// Process a batch of files by computing the file's contents' hash.
        /// </summary>
        /// <param name="filePaths">A queue of file paths to process</param>
        /// <returns>A dictionary mapping filepath to hash code</returns>
        private FileHashBatch ProcessFileBatch(string batchName, IImmutableQueue<string> filePaths)
        {
            Trace.WriteLine($"Starting processing batch {batchName}");
            var results = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>();

            foreach (var filePath in filePaths)
            {
                byte[] hash;
                using (var md5 = MD5.Create())
                {
                    FileStream fs = null;
                    try
                    {
                        fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        // stream-based, thus large file friendly 
                        // not everything is in memory
                        hash = md5.ComputeHash(fs);
                        fs = null;
                        results.Add(filePath, ImmutableArray.Create(hash));
                    }
                    catch (Exception)
                    {
                        // TODO: handle IO exceptions; it is possible that a file disappears before we process the batch.
                        throw;
                    }
                    finally
                    {
                        fs?.Dispose();
                    }
                }
            } // foreach
            Trace.WriteLine($"Finishing processing batch {batchName}");
            return new FileHashBatch(batchName, results.ToImmutable());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="results"></param>
        private void AddHashesAndCheckDuplicates(FileHashBatch results)
        {
            Trace.WriteLine($"Processing {results.BatchName}");
            Trace.Indent();
            foreach (var result in results.Data)
            {
                Trace.WriteLine($"{result.Key} --> {Convert.ToBase64String(result.Value.ToArray())}");

                byte[] hash = result.Value.ToArray();
                if (_fileHashesBloomFilter.Contains(hash))
                {
                    Trace.WriteLine($"{result.Key} has a possible duplicate!");
                    // TO DO: there is a small probability that the Bloom Filter would report a False Positive. 
                    // Revisit by comparing the MD5 hashes (or the actual contents) of the likely duplicates only. 
                    this.Duplicates.Add(result.Key);
                }
                else
                    _fileHashesBloomFilter.Add(hash);
            }
            Trace.Unindent();
        }
    }
}
