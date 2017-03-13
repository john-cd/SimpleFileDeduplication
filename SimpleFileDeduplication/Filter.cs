using System;
using System.Collections;

namespace Client
{
    /// <summary>
    /// Bloom filter - a probablistic data structure used to test whether an element is a member of a set.
    /// False positive matches are possible (but rare), but false negatives are not.
    /// In other words, a query returns either "possibly in set" or "definitely not in set".
    /// 
    /// Code modified from https://bloomfilter.codeplex.com/SourceControl/latest#BloomFilter/Filter.cs
    /// 
    /// Here's an example demonstrating its use with strings:
    /// int capacity = 2000000; // the number of items you expect to add to the filter
    /// Filter<string> filter = new Filter<string>(capacity);
    /// add your items, using:
    /// filter.Add("SomeString");
    /// now you can check for them, using:
    /// if (filter.Contains("SomeString"))
    /// Console.WriteLine("Match!");
    /// </summary>
    /// <typeparam name="T">the type of the objects to store</typeparam>
    public class Filter<T>
    {
        /// <summary>
        /// A function that can be used to hash input.
        /// </summary>
        /// <param name="input">The values to be hashed.</param>
        /// <returns>The resulting hash code.</returns>
        public delegate int HashFunction(T input);

        private readonly int _hashFunctionsCount;
        private readonly BitArray _filterBits;
        private readonly HashFunction _primaryHashFunction;
        private readonly HashFunction _secondaryHashFunction;

        /// <summary>
        /// Creates a new Bloom filter, specifying an error rate of 1/capacity, using the optimal size for the underlying data structure based on the desired capacity and error rate, as well as the optimal number of hash functions.
        /// A secondary hash function will be provided for you if your type T is either string or int. Otherwise an exception will be thrown. If you are not using these types please use the overload that supports custom hash functions.
        /// </summary>
        /// <param name="capacity">The anticipated number of items to be added to the filter. More than this number of items can be added, but the error rate will exceed what is expected.</param>
        public Filter(int capacity) : this(capacity, null) { }

        /// <summary>
        /// Creates a new Bloom filter, using the optimal size for the underlying data structure based on the desired capacity and error rate, as well as the optimal number of hash functions.
        /// A secondary hash function will be provided for you if your type T is either string or int. Otherwise an exception will be thrown. If you are not using these types please use the overload that supports custom hash functions.
        /// </summary>
        /// <param name="capacity">The anticipated number of items to be added to the filter. More than this number of items can be added, but the error rate will exceed what is expected.</param>
        /// <param name="errorRate">The acceptable false-positive rate (e.g., 0.01F = 1%)</param>
        public Filter(int capacity, float errorRate) : this(capacity, errorRate, null) { }

        /// <summary>
        /// Creates a new Bloom filter, specifying an error rate of 1/capacity, using the optimal size for the underlying data structure based on the desired capacity and error rate, as well as the optimal number of hash functions.
        /// </summary>
        /// <param name="capacity">The anticipated number of items to be added to the filter. More than this number of items can be added, but the error rate will exceed what is expected.</param>
        /// <param name="hashFunction">The function to hash the input values. Do not use GetHashCode(). If it is null, and T is string or int a hash function will be provided for you.</param>
        public Filter(int capacity, HashFunction hashFunction) : this(capacity, BestErrorRate(capacity), hashFunction) { }

        /// <summary>
        /// Creates a new Bloom filter, using the optimal size for the underlying data structure based on the desired capacity and error rate, as well as the optimal number of hash functions.
        /// </summary>
        /// <param name="capacity">The anticipated number of items to be added to the filter. More than this number of items can be added, but the error rate will exceed what is expected.</param>
        /// <param name="errorRate">The accepable false-positive rate (e.g., 0.01F = 1%)</param>
        /// <param name="hashFunction">The function to hash the input values. Do not use GetHashCode(). If it is null, and T is string or int a hash function will be provided for you.</param>
        public Filter(int capacity, float errorRate, HashFunction hashFunction) : this(capacity, errorRate, hashFunction, BestM(capacity, errorRate), BestK(capacity, errorRate)) { }

        /// <summary>
        /// Creates a new Bloom filter.
        /// </summary>
        /// <param name="capacity">The anticipated number of items to be added to the filter. More than this number of items can be added, but the error rate will exceed what is expected.</param>
        /// <param name="errorRate">The accepable false-positive rate (e.g., 0.01F = 1%)</param>
        /// <param name="hashFunction">The function to hash the input values. Do not use GetHashCode(). If it is null, and T is string or int a hash function will be provided for you.</param>
        /// <param name="bitArrayLength">The number of elements in the BitArray.</param>
        /// <param name="hashFunctionsCount">The number of hash functions to use.</param>
        protected Filter(int capacity, float errorRate, HashFunction hashFunction, int bitArrayLength, int hashFunctionsCount)
        {
            // validate the params are in range
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "capacity must be > 0");
            if (errorRate >= 1 || errorRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(errorRate), errorRate, String.Format("errorRate must be between 0 and 1, exclusive. Was {0}", errorRate));
            if (bitArrayLength < 1) // from overflow in bestM calculation
                throw new ArgumentOutOfRangeException(String.Format("The provided capacity and errorRate values would result in an array of length > int.MaxValue. Please reduce either of these values. Capacity: {0}, Error rate: {1}", capacity, errorRate));

            // set the secondary hash function
            if (hashFunction == null)
            {
                //If you are going to be working with some other type T you will have to provide your own hash algorithm that takes a T and returns an int. Do NOT use the BCL's GetHashCode method. If you end up creating one for a common type (e.g., CRC for files) please add it to the source so that others may make use of your work.
                //Overloads are provided for specifying your own error rate, hash function, array size, double hash
                if (typeof(T) == typeof(byte[]))
                {
                    this._secondaryHashFunction = HashBytes;
                }
                else if (typeof(T) == typeof(String))
                {
                    this._secondaryHashFunction = HashString;
                }
                else if (typeof(T) == typeof(int))
                {
                    this._secondaryHashFunction = HashInt32;
                }
                else
                    throw new ArgumentNullException("hashFunction", "Please provide a hash function for your type T, when T is not a string or int or byte[].");
            }
            else
                this._secondaryHashFunction = hashFunction;

            // fix of a bug in the original code  
            if ( ! typeof(T).IsValueType)
            {
                if (typeof(T) == typeof(byte[]))
                {
                    this._primaryHashFunction = HashBytes2;
                }
                else
                    throw new NotSupportedException("Unsupported type.")
;            }

            this._hashFunctionsCount = hashFunctionsCount;
            this._filterBits = new BitArray(bitArrayLength);
        }

        /// <summary>
        /// Adds a new item to the filter. It cannot be removed.
        /// </summary>
        /// <param name="item"></param>
        public void Add(T item)
        {
            int primaryHash = (this._primaryHashFunction != null) ? this._primaryHashFunction(item): item.GetHashCode();  // Fix of bug in original code - item.GetHashCode() not suitable for non-ValueType
            int secondaryHash = _secondaryHashFunction(item);
            for (int i = 0; i < _hashFunctionsCount; i++)
            {
                int hash = ComputeHash(primaryHash, secondaryHash, i);
                this._filterBits[hash] = true;
            }
        }

        /// <summary>
        /// Checks for the existence of the item in the filter for a given probability.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Contains(T item)
        {
            int primaryHash = (this._primaryHashFunction != null) ? this._primaryHashFunction(item) : item.GetHashCode();
            int secondaryHash = _secondaryHashFunction(item);
            for (int i = 0; i < _hashFunctionsCount; i++)
            {
                int hash = ComputeHash(primaryHash, secondaryHash, i);
                if (_filterBits[hash] == false)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The ratio of true bits in the filter. E.g., 1 true bit in a 10 bit filter means a truthiness of 0.1.
        /// </summary>
        public double Truthiness
        {
            get
            {
                return (double)TrueBits() / _filterBits.Count;
            }
        }

        private int TrueBits()
        {
            int output = 0;
            foreach (bool bit in _filterBits)
            {
                if (bit == true)
                    output++;
            }
            return output;
        }

        /// <summary>
        /// Performs Dillinger and Manolios double hashing. 
        /// </summary>
        private int ComputeHash(int primaryHash, int secondaryHash, int i)
        {
            int resultingHash = (primaryHash + (i * secondaryHash)) % _filterBits.Count;
            return Math.Abs((int)resultingHash);
        }


        private static int BestK(int capacity, float errorRate)
        {
            return (int)Math.Round(Math.Log(2.0) * BestM(capacity, errorRate) / capacity);
        }

        //Bloom filters can't be resized, so the capacity is important for memory sizing. The false-positive rate also plays in here. 
        //If you don't specify false-positive rate you will get 1 / capacity, unless that is too small in which case you will get 0.6185^(int.MaxValue / capacity), which is nearly optimal

        private static int BestM(int capacity, float errorRate)
        {
            return (int)Math.Ceiling(capacity * Math.Log(errorRate, (1.0 / Math.Pow(2, Math.Log(2.0)))));
        }

        private static float BestErrorRate(int capacity)
        {
            float c = (float)(1.0 / capacity);
            if (c != 0)
                return c;
            else
                return (float)Math.Pow(0.6185, int.MaxValue / capacity); // http://www.cs.princeton.edu/courses/archive/spring02/cs493/lec7.pdf
        }

        /// <summary>
        /// Hashes a 32-bit signed int using Thomas Wang's method v3.1 (http://www.concentric.net/~Ttwang/tech/inthash.htm).
        /// Runtime is suggested to be 11 cycles. 
        /// </summary>
        /// <param name="input">The integer to hash.</param>
        /// <returns>The hashed result.</returns>
        private static int HashInt32(T input)
        {
            uint? x = input as uint?;
            unchecked
            {
                x = ~x + (x << 15); // x = (x << 15) - x- 1, as (~x) + y is equivalent to y - x - 1 in two's complement representation
                x = x ^ (x >> 12);
                x = x + (x << 2);
                x = x ^ (x >> 4);
                x = x * 2057; // x = (x + (x << 3)) + (x<< 11);
                x = x ^ (x >> 16);
                return (int)x;
            }
        }

        /// <summary>
        /// Hashes a string using Bob Jenkin's "One At A Time" method from Dr. Dobbs (http://burtleburtle.net/bob/hash/doobs.html).
        /// Runtime is suggested to be 9x+9, where x = input.Length. 
        /// </summary>
        /// <param name="input">The string to hash.</param>
        /// <returns>The hashed result.</returns>
        private static int HashString(T input)
        {
            string s = input as string;
            int hash = 0;

            for (int i = 0; i < s.Length; i++)
            {
                hash += s[i];
                hash += (hash << 10);
                hash ^= (hash >> 6);
            }
            hash += (hash << 3);
            hash ^= (hash >> 11);
            hash += (hash << 15);
            return hash;
        }

        private static int HashBytes(T input)
        {
            byte[] s = input as byte[];
            int hash = 0;

            for (int i = 0; i < s.Length; i++)
            {
                hash += s[i];
                hash += (hash << 10);
                hash ^= (hash >> 6);
            }
            hash += (hash << 3);
            hash ^= (hash >> 11);
            hash += (hash << 15);
            return hash;
        }

        public static int HashBytes2(T input)
        {
            byte[] data = input as byte[];
            unchecked
            {
                const int p = 16777619;
                int hash = (int)2166136261;

                for (int i = 0; i < data.Length; i++)
                    hash = (hash ^ data[i]) * p;

                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;
                return hash;
            }
        }
    }
}
