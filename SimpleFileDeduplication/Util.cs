using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static System.String;

namespace Client
{
    internal static class Util
    {
        /// <summary>
        /// Create fake files + some duplicates for testing
        /// </summary>
        /// <param name="rootFolderPath"></param>
        public static void CreateFakeFiles(string rootFolderPath, int originalFileCount = 0, int dupeFileCount = 0)
        {
            if (IsNullOrWhiteSpace(rootFolderPath))
                throw new ArgumentNullException(nameof(rootFolderPath));

            var random = new Random(42);
            originalFileCount = (originalFileCount <= 0) ? random.Next(10, 50) : originalFileCount;
            dupeFileCount = (dupeFileCount <= 0) ? random.Next(10, 20) : dupeFileCount;

            // random file path generator
            string GetRandomString(int length = 2) => Path.GetRandomFileName().Replace(".", "").Substring(0, length);  // Return n character string

            string GetRandomDirs() => Path.Combine(Enumerable.Range(0, random.Next(1, 3)).Select(i => GetRandomString()).ToArray());

            string GetRandomFilePath(string src = null) => Path.Combine(rootFolderPath, GetRandomDirs(), $"{GetRandomString()}{(src != null ? $"_copyof_{src}" : "")}.data");

            int sizeMB;
            var filePaths = Enumerable.Range(0, originalFileCount).Select(i => GetRandomFilePath()).ToList();

            foreach (string filePath in filePaths)
            {
                sizeMB = random.Next(1, 10);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    using (FileStream fs = File.Create(filePath))
                    {
                        fs.Seek(sizeMB * 1024L * 1024L, SeekOrigin.Begin);
                        fs.WriteByte(0);
                    }
                    Trace.WriteLine(filePath);
                }
                catch (IOException ex) // file exists already
                {
                    Trace.WriteLine(ex.Message);
                }
            }
            // create duplicates
            var dupes = Enumerable.Range(0, dupeFileCount).Select(i => filePaths[random.Next(0, filePaths.Count() - 1)]).Select(s => (src: s, dest: GetRandomFilePath(Path.GetFileNameWithoutExtension(s))));
            foreach (var dupe in dupes)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dupe.dest));
                    File.Copy(dupe.src, dupe.dest, true);
                }
                catch (IOException ex)  // file exists already
                {
                    Trace.WriteLine(ex.Message);
                }
            }
        }
    }
}
