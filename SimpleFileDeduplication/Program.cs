using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static System.String;

namespace Client
{
    internal class Program
    {
        static int Main(string[] args)
        {
            try
            {
                Trace.Listeners.Add(new ConsoleTraceListener());

                string rootFolderPath = Path.GetFullPath(@"..\..\..\data");

                // Create a set of artifical files for testing purposes
                foreach (var dir in Directory.EnumerateDirectories(rootFolderPath))
                    Directory.Delete(dir, true);

                Util.CreateFakeFiles(rootFolderPath, 2, 2);

                // Init client
                var client = new Client(rootFolderPath);

                // Start the client's work and wait synchronously
                client.Run().Wait();

                foreach(string dupe in client.Duplicates)
                {
                    Console.WriteLine($"Duplicate found: {dupe}");
                }
            }
            catch (Exception ex)  
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                Trace.Flush();
            }

            Console.WriteLine("Press Enter to terminate...");
            Console.ReadLine();
            return 0;
        }
    }
}