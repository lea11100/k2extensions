using Lucene.Net.Util;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;

namespace k2extensionsLib
{
    internal class DynamicBitArray : IList<bool>
    {
        BitArray data { get; set; } = new BitArray(128, false);
        int lastIndex { get; set; } = 0;

        public bool this[int index] { get => data[index]; set => data[index] = value; }

        public int Count => data.Count;

        public bool IsReadOnly => data.IsReadOnly;

        public void Add(bool item)
        {
            if (lastIndex >= data.Count)
                data.Length += 128;
            data[lastIndex] = item;
            lastIndex++;
        }

        public void AddRange(BitArray array)
        {
            for (int i = 0; i < array.Length; i++)
            {
                Add(array[i]);
            }
        }

        public void Clear()
        {
            data = new BitArray(128, false);
            lastIndex = 0;
        }

        public bool Contains(bool item)
        {
            return data.Cast<bool>().Contains(item);
        }

        public void CopyTo(bool[] array, int arrayIndex)
        {
            data.CopyTo(array, arrayIndex);
        }

        public IEnumerator<bool> GetEnumerator()
        {
            return data.Cast<bool>().GetEnumerator();
        }

        public int IndexOf(bool item)
        {
            return data.Cast<bool>().ToList().IndexOf(item);
        }

        public void Insert(int index, bool item)
        {
            throw new NotImplementedException();
        }

        public bool Remove(bool item)
        {
            throw new NotImplementedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return data.GetEnumerator();
        }

        public BitArray GetFittedArray()
        {
            shrink();
            return data;
        }

        private void shrink()
        {
            data.Length = lastIndex;
        }
    }

    internal class FastRankBitArray
    {
        private ulong[] data;
        private int[] oneCounter;

        internal FastRankBitArray()
        {
            data = new ulong[0];
            oneCounter = new int[0];
        }

        internal FastRankBitArray(BitArray array)
        {
            data = new ulong[0];
            oneCounter = new int[0];
            Store(array);
        }

        internal void Store(BitArray array)
        {
            data = new ulong[(int)Math.Ceiling(((double)array.Length) / 64)];
            oneCounter = new int[(int)Math.Ceiling(((double)array.Length) / 64)];
            for (int i = 0; i < Math.Ceiling((double)array.Length/64)*64; i++)
            {
                int j = i / 64;
                data[j] <<= 1;
                if (i < array.Length)
                {
                    data[j] += array[i] ? 1 : (ulong)0;
                }
            }
            initOneCounter();
        }



        internal void Store(string array)
        {
            data = new ulong[(int)Math.Ceiling(((double)array.Length) / 4)];
            oneCounter = new int[(int)Math.Ceiling(((double)array.Length) / 4)];
            var chunkedArray = array.Chunk(4);
            for (int i = 0; i < chunkedArray.Count(); i++)
            {
                char[] chunk = chunkedArray.ElementAt(i);
                ulong d = 0;
                for (int j = 0; j < 4; j++)
                {
                    int c = chunk[j];
                    d += (ulong)c;
                    d <<= 16;
                }
                data[i] = d;
            }
            initOneCounter();
        }

        internal IEnumerator GetEnumerator()
        {
            return data.GetEnumerator();
        }

        internal bool this[int key]
        {
            get
            {
                int block = key / 64;
                int position = key % 64;
                var bit = (data[block] & ((ulong)1 << 63 - position)) != 0;
                return bit;
            }
        }

        internal ulong[] this[Range range]
        {
            get
            {
                int startBlock = range.Start.Value / 64;
                int startPosition = range.Start.Value % 64;
                int endBlock = range.End.Value / 64;
                int endPosition = range.End.Value % 64;
                ulong[] result = data[startBlock..(endBlock+1)];
                for (int i = 0; i < result.Length - 1; i++)
                {
                    result[i] <<= startPosition;
                    result[i] |= result[i + 1] >>> startPosition;
                }
                result[^1] = result[^1] & (ulong.MaxValue << (endPosition - 1));
                return result;
            }
        }

        internal int Length()
        {
            return data.Length * 64;
        }

        internal int Rank1(int index, int start = 0)
        {
            int ignoredOnes = 0;
            if (start != 0)
            {
                ignoredOnes = Rank1(start - 1);
            }
            int block = index / 64;
            int posInBlock = index % 64;
            int result = oneCounter[block];
            result += BitOperations.PopCount(data[block] >>> (63 - posInBlock));
            return result - ignoredOnes;
        }

        internal int Select1(int numberOfOnes)
        {
            int block = Array.BinarySearch(oneCounter, numberOfOnes);
            int onesInBlock = numberOfOnes - oneCounter[block];
            int positionInBlock = Array.BinarySearch(ulongToArray(data[block]), onesInBlock, new _CompareByRank());
            return block * 64 + positionInBlock;
        }

        internal string GetDataAsString()
        {
            var result = string.Join("", data.Select(ulongToChar));
            return result;
        }

        private char[] ulongToChar(ulong value)
        {
            ulong extractor = (ulong)short.MaxValue;
            var result = new char[4];
            for (int i = 0; i < 4; i++)
            {
                int c = (int)(value & extractor) >> (i * 16);
                result[^(i + 1)] = (char)c;
                extractor <<= 16;
            }
            return result;
        }

        private void initOneCounter()
        {
            int counter = 0;
            for (int i = 0; i < data.Length; i++)
            {
                oneCounter[i] = counter;
                counter += BitOperations.PopCount(data[i]);
            }
        }

        private ulong[] ulongToArray(ulong value)
        {
            var result = new ulong[64];
            for (int i = 0; i < 64; i++)
            {
                result[i] = value;
                value = value >>> 1;
            }
            return result;
        }

        private class _CompareByRank : IComparer
        {
            public int Compare(object? x, object? y)
            {
                ulong v1 = (ulong)(x ?? 0);
                ulong v2 = (ulong)(y ?? 0);

                return BitOperations.PopCount(v1) - BitOperations.PopCount(v2);
            }
        }
    }

    public interface IK2Extension
    {
        IEnumerable<INode> Subjects { get; set; }
        IEnumerable<INode> Objects { get; set; }
        IEnumerable<INode> Predicates { get; set; }
        void Compress(IGraph graph, bool useK2Triples);
        Triple[] Decomp();
        Triple[] Prec(INode o);
        Triple[] Succ(INode s);
        Triple[] AllEdgesOfType(INode p);
        Triple[] Connections(INode s, INode o);
        Triple[] PrecOfType(INode o, INode p);
        Triple[] SuccOfType(INode s, INode p);
        bool Exists(INode s, INode p, INode o);
        void Store(string filename);
        void Load(string filename);
    }

    internal static class GeneralExtensions
    {
        internal static int rank1(this BitArray array, int index)
        {
            int result = array.Cast<bool>().Take(index + 1).Count(x => x);
            return result;
        }

        internal static int select1(this BitArray array, int k, int start = 0)
        {
            int result = array.Cast<bool>().Select((v, i) => new { value = v, index = i })
                    .Where(item => item.value && item.index >= start)
                    .Skip(k - 1)
                    .FirstOrDefault()?.index ?? 0;
            return result;
        }

        internal static int[] ToBase(this int value, int baseSize)
        {
            Stack<int> digits = new Stack<int>();

            long tmp = value;
            while (tmp != 0)
            {
                digits.Push((int)(tmp % baseSize));
                tmp = (long)((tmp - digits.Peek()) / baseSize);
            }

            return digits.ToArray();
        }

        internal static int FromBase(this IEnumerable<int> value, int baseSize)
        {
            int result = 0;
            int b = 1;
            for (int i = value.Count() - 1; i >= 0; i--)
            {
                result += b * value.ElementAt(i);
                b *= baseSize;
            }
            return result;
        }

        internal static IEnumerable<Triple> Sort(this IEnumerable<Triple> list)
        {
            return list.OrderBy(t => t.Subject).ThenBy(t => t.Object).ThenBy(t => t.Predicate);
        }
    }
}
