
## Goal

A Linux directory structure contains 100G worth of files. The depth and number of sub-directories and files is not known.
Soft-links and hard-links can also be expected.  Write, in the language of your choice, a program that traverses the whole structure as fast as possible and reports duplicate files. Duplicates are files with same content. Be prepared to discuss the strategy that you taken and its trade-offs.

## Approach

- The directory structure of unknown depth calls for recursion through sub-directories.  

- The amount of data is large-ish and certainly does not fit in memory. The code uses Streams to avoid loading whole files in memory then computes a hash that uniquely represents the content of each file (some collisions are expected but are the exceptions). 

- Storing millions of hashes in a traditional data structure (hashset) will also exceed memory limits. 
Instead, I use a bloom filter to store an approximation of the set. A Bloom filter is a space-efficient probabilistic data structure used to test whether an element is a member of a set.  

- For maximum speed, the portion of the code that computes Hashes is multi-threaded (runs on the .NET threadpool). Computation is performed in an embarrassingly parallel manner and with immutable data structures, thus there is no need for locking. 

- Possible improvement: technically, the IO is partially synchronous, as .NET does not implement async hashing yet.

- Possible improvement: use Akka.NET or project Orleans to distribute the hashing loads on multiple servers, not just multiple cores in the same server.
  

  
