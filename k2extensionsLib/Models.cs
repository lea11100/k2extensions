using J2N.Collections.Generic.Extensions;
using Lucene.Net.Analysis.Miscellaneous;
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
    public class TreeNode
    {
        public TreeNode[] Children { get; set; }

        public TreeNode(int children = 4)
        {
            Children = new TreeNode[children];
        }

        public TreeNode SetChild(int index, TreeNode child)
        {
            Children[index] = Children[index] ?? child;
            return Children[index];
        }

        public TreeNode GetChild(int index)
        {
            return Children[index];
        }
    }

    internal class DynamicBitArray
    {
        internal List<ulong> data { get; set; } = new List<ulong>() { 0 };
        internal int firstFreeIndex { get; set; } = 0;

        public bool this[int index] { get => (data[index/64] & ((ulong)1<<(index%64))) != 0; set => data[index/64] += (ulong)1<<(index%64); }

        public void AddRange(uint array, int length)
        {
            //array <<= 32 - length;
            ulong firstPart = (ulong)array << 32 >>> firstFreeIndex;
            ulong secondPart = (ulong)array << (32 + (64 - firstFreeIndex));
            data[^1] += firstPart;
            firstFreeIndex += length;
            if (firstFreeIndex >= 64)
            {
                data.Add(secondPart);
            }
            firstFreeIndex %= 64;           
        }

        public void AddRange(ulong array, int length)
        {
            ulong firstPart = array >>> firstFreeIndex;
            ulong secondPart = 0;
            if (firstFreeIndex != 0)
                secondPart = array << (64 - firstFreeIndex);
            data[^1] += firstPart;
            firstFreeIndex += length;
            if (firstFreeIndex >= 64)
            {
                data.Add(secondPart);
            }
            firstFreeIndex %= 64;
        }

        public void AddRange(DynamicBitArray array)
        {
            foreach (ulong item in array.data.Take(array.data.Count - 1))
            {
                AddRange(item, 64);
            }
            if (array.firstFreeIndex != 0)
            {
                AddRange(array.data[^1], array.firstFreeIndex);
            }
        }

        public string GetAsString()
        {
            var res = "";
            foreach(ulong d in data.Take(data.Count - 1))
            {
                res += Convert.ToString((long)d, 2).PadLeft(64, '0');
            }
            res += Convert.ToString((long)data[^1], 2).PadLeft(64,'0').Substring(0,firstFreeIndex);
            return res;
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

        internal FastRankBitArray(DynamicBitArray array)
        {
            data = array.data.ToArray();
            oneCounter = new int[array.data.Count];
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
                for (int i = 0; i < result.Length-1; i++)
                {
                    result[i] <<= startPosition;
                    result[i] |= result[i + 1] >>> startPosition;
                }              
                result[^1] = result[^1] & (ulong.MaxValue << (63 - endPosition));
                result[^1] <<= startPosition;
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
            if (numberOfOnes == 0) return 0;
            int block = Array.BinarySearch(oneCounter, numberOfOnes);
            if (block < 0) block = ~block - 1;
            int onesInBlock = numberOfOnes - oneCounter[block];
            List<(int, int)> ranks = ulongToRankArray(data[block]).ToList();
            int positionInRanks = ranks.Select(x => x.Item2).ToList().BinarySearch(onesInBlock);
            return block * 64 + ranks[positionInRanks].Item1;
        }

        internal string GetDataAsString()
        {
            var result = string.Join("", data.Select(ulongToChar));
            return result;
        }

        internal string GetAsBitstream()
        {
            var res = "";
            foreach (ulong d in data)
            {
                res += Convert.ToString((long)d, 2).PadLeft(64, '0');
            }
            return res;
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

        private (int, int)[] ulongToRankArray(ulong value)
        {
            var result = new List<(int,int)>();
            ulong extractor = ulong.MaxValue >> 1;
            for (int i = 0; i <64; i++)
            {
                ulong v = value & ~extractor;
                if (result.Count == 0)
                    result.Add((i, BitOperations.PopCount(v)));
                else if (result.Last().Item2 != BitOperations.PopCount(v))
                    result.Add((i, BitOperations.PopCount(v)));
                extractor >>= 1;
            }
            return result.ToArray();
        }
    }

    public interface IK2Extension
    {
        INode[] Subjects { get; set; }
        INode[] Objects { get; set; }
        INode[] Predicates { get; set; }
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
        void Load(string filename, bool useK2Triple);
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

        internal static int[] ToBase(this int value, int baseSize, int length = 0)
        {
            Stack<int> digits = new Stack<int>();
            int[] result;
            long tmp = value;
            while (tmp != 0)
            {
                digits.Push((int)(tmp % baseSize));
                tmp = (long)((tmp - digits.Peek()) / baseSize);
            }
            if (length != 0)
            {
                result = new int[length];
                digits.ToArray().CopyTo(result, length - digits.Count);
            }
            else
                result = digits.ToArray();
            return result;
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
            return list.OrderBy(t => t.Subject.ToString()).ThenBy(t => t.Object.ToString()).ThenBy(t => t.Predicate.ToString());
        }
    }
}
