using J2N.Collections.Generic.Extensions;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Util;
using NUnit.Framework;
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
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace k2extensionsLib
{
    public class TreeNode
    {
        public TreeNode[] Children { get; set; }
        public ulong Label { get; set; }


        public TreeNode(int children)
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

        public ulong GetLabel()
        {
            return Label;
        }
        public void SetLabel(ulong label)
        {
            Label = label;
        }

    }

    internal class DynamicBitArray
    {
        internal List<ulong> data { get; set; } = new List<ulong>() { 0 };
        internal int firstFreeIndex { get; set; } = 0;

        public bool this[int index] { get => (data[index / 64] & ((ulong)1 << (index % 64))) != 0; set => data[index / 64] += (ulong)1 << (index % 64); }

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
            foreach (ulong d in data.Take(data.Count - 1))
            {
                res += Convert.ToString((long)d, 2).PadLeft(64, '0');
            }
            res += Convert.ToString((long)data[^1], 2).PadLeft(64, '0').Substring(0, firstFreeIndex);
            return res;
        }
    }

    internal class FlatPopcount
    {
        //L0-index missing, since optional

        private Int128[] _L1L2Index { get; set; } //TODO: Maybe use two ulongs instead
        private ulong[] _Data { get; set; }
        private long[] _SampelsOfOnePositions { get; set; }

        public FlatPopcount()
        {
            _Data = new ulong[0];
            _L1L2Index = new Int128[0];
            _SampelsOfOnePositions = new long[0];
        }

        public FlatPopcount(DynamicBitArray array)
        {
            _Data = array.data.ToArray();
            _L1L2Index = new Int128[(int)Math.Ceiling((double)_Data.Length/64)];
            _SampelsOfOnePositions = new long[0];
            Init();
        }

        internal string GetDataAsString()
        {
            var result = string.Join("", _Data.Select(x=>x.ToChars()));
            return result;
        }

        internal string GetAsBitstream()
        {
            var res = "";
            foreach (ulong d in _Data)
            {
                res += Convert.ToString((long)d, 2).PadLeft(64, '0');
            }
            return res;
        }

        internal void Store(string array)
        {
            _Data = new ulong[(int)Math.Ceiling(((double)array.Length) / 4)];
            _L1L2Index = new Int128[(int)Math.Ceiling((double)_Data.Length / 64)];
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
                _Data[i] = d;
            }
            Init();
        }

        private void Init()
        {
            long numberOfOnesBeforeL1 = 0;
            List<long> sampels = new List<long>();
            int index = 0;
            foreach (var l1 in _Data.Chunk(64))
            {
                int numberOfOnesBeforeL2 = 0;
                int[] numberOfOnesBeforeEachL2 = new int[8];
                int indexForL2 = 0;
                foreach (var l2 in l1.Chunk(8))
                {
                    numberOfOnesBeforeEachL2[indexForL2] = numberOfOnesBeforeL2;
                    var temp = l2.Select(BitOperations.PopCount).Sum();
                    if((numberOfOnesBeforeL2 + numberOfOnesBeforeL1)>>>13 != (numberOfOnesBeforeL2 + temp + numberOfOnesBeforeL1) >>> 13)
                    {
                        long remainingOnes = ((((numberOfOnesBeforeL2 + numberOfOnesBeforeL1) >>> 13) + 1) << 13) - (numberOfOnesBeforeL2 + numberOfOnesBeforeL1);
                        int relativePosition = Select1In512(l2, (int)remainingOnes);
                        sampels.Add(index * 4096 + indexForL2 * 512 + relativePosition);
                    }
                    numberOfOnesBeforeL2 += temp;
                    indexForL2++;
                }
                _L1L2Index[index] = _InitL1L2(numberOfOnesBeforeL1, numberOfOnesBeforeEachL2.Skip(0).ToArray(), out int blockPositionOf8192);
                numberOfOnesBeforeL1 += numberOfOnesBeforeL2;
                index++;
            }
            _SampelsOfOnePositions = sampels.ToArray();
        }

        private int Select1In512(ulong[] array, int nthOne)
        {
            int oneCounter = 0;
            int index = 0;
            foreach (var item in array)
            {
                oneCounter += BitOperations.PopCount(item);
                index += 64;
                if (oneCounter < nthOne) continue;
                int remainingOnes = nthOne - oneCounter + BitOperations.PopCount(item);
                ulong mask = ulong.MaxValue << 63;
                for (int i = 0; i < 64; i++)
                {
                    if (remainingOnes == BitOperations.PopCount(item & mask))
                    {
                        return index - 64 + i;
                    }
                    mask >>= 1;
                } 
            }
            return -1;
        }
        
        private Int128 _InitL1L2(long onesBeforeL1, int[] onesInL2, out int blockPositionOf8192)
        {
            blockPositionOf8192 = -1;
            Int128 result = 0;
            result += (Int128)onesBeforeL1 << 84; //Get size of 44 Bit and shift to front;
            var oneCounter = 0;
            var index = 44;
            foreach (var l2 in onesInL2)
            {
                int temp = oneCounter += l2;
                if(oneCounter >>> 13 != temp >>> 13)
                {
                    blockPositionOf8192 = (index - 44) / 12;
                }
                oneCounter += l2;
                result += (Int128)l2 << (116 - index); //Get size of 12 and shift to position
                index += 12;
            }
            return result;
        }

        internal bool this[int key]
        {
            get
            {
                int block = key / 64;
                int position = key % 64;
                var bit = (_Data[block] & ((ulong)1 << 63 - position)) != 0;
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
                ulong[] result = _Data[startBlock..(endBlock + 1)];
                if (startPosition != 0)
                {
                    for (int i = 0; i < result.Length - 1; i++)
                    {
                        result[i] <<= startPosition;
                        result[i] += result[i + 1] >>> (64 - startPosition);
                    }
                    result[^1] <<= startPosition;
                }
                endPosition -= startPosition;
                if (endPosition <= 0)
                {
                    result = result[0..^1];
                    result[^1] &= ulong.MaxValue << (-1 * endPosition);
                }
                else
                {
                    result[^1] &= ulong.MaxValue << (64 - endPosition);
                }
                return result;
            }
        }

        internal long Select1(int nthOne)
        {
            long position = _SampelsOfOnePositions[nthOne >> 13];
            int l1 = (int)(position >> 12);
            position = l1 * 4096;
            int remainingOnes = nthOne - getL1(l1);

            while (remainingOnes - getL1(l1) > 0)
            {
                remainingOnes -= getL1(l1);
                l1++;
                position += 4096;
            }
            int l2 = 0;
            while (remainingOnes - getL2(l1, l2) > 0)
            {
                l2++;
                remainingOnes -= getL2(l1, l2);
                position += 512;
            }
            position += Select1In512(_Data[(l1 * 64 + l2 * 8)..(l1 * 64 + l2 * 8 + 8)], remainingOnes);
            return position;
        }

        private int getL1(int position)
        {
            return (int)(_L1L2Index[position] >> 96);
        }

        private int getL2(int positionL1, int positionL2)
        {
            Int128 mask = Int128.MaxValue >>> 44 >>> (positionL2 * 12);
            return (int)(_L1L2Index[positionL1] & mask >>> ((7 - positionL2) * 12));
        }

        /// <summary>
        /// Rank implementation including the index nad the start, if given
        /// </summary>
        /// <param name="position"></param>
        /// <param name="start"></param>
        /// <returns></returns>
        internal int Rank1(int position, int start = 0)
        {
            int ignoredOnes = 0;
            if (start != 0)
            {
                ignoredOnes = Rank1(start - 1);
            }
            int block = position / 64;
            int l1 = block / 64;
            int l2 = block % 64 / 8;
            int l3 = block % 8;
            int relativePositionInL3 = position % 64;
            Assert.AreEqual(position, l1 * 4096 + l2 * 512 + l3 * 64 + relativePositionInL3);

            int result = getRankByBlocks(l1, l2, l3, relativePositionInL3);
            return result - ignoredOnes; 
        }

        private int getRankByBlocks(int l1, int l2, int l3, int relativePositionInL3)
        {
            int result = 0;
            Int128 blockIndex = _L1L2Index[l1];
            result += (int)(blockIndex >>> 84);
            blockIndex <<= 44;
            while (l2 > 0)
            {
                blockIndex <<= 12;
                l2--;
            }
            result += (int)(blockIndex >>> 116);

            ulong[] block512 = _Data[(l1 * 64 + l2 * 8)..(l1 * 64 + l2 * 8 + l3)];
            result += block512.Select(BitOperations.PopCount).Sum();

            ulong blockInBlock512 = _Data[l1 * 64 + l2 * 8 + l3];
            result += BitOperations.PopCount(blockInBlock512 & (ulong.MaxValue << (63 - relativePositionInL3)));

            return result;
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
                ulong[] result = data[startBlock..(endBlock + 1)];
                if (startPosition != 0)
                {
                    for (int i = 0; i < result.Length - 1; i++)
                    {
                        result[i] <<= startPosition;
                        result[i] += result[i + 1] >>> (64 - startPosition);
                    }
                    result[^1] <<= startPosition;
                }
                endPosition -= startPosition;
                if(endPosition <= 0)
                {
                    result = result[0..^1];
                    result[^1] &= ulong.MaxValue << (-1 * endPosition);
                }
                else
                {
                    result[^1] &= ulong.MaxValue << (64 - endPosition);
                }
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
            List<(int, int)> ranks;
            if (onesInBlock == 0)
            {
                onesInBlock = numberOfOnes - oneCounter[block - 1];
                ranks = ulongToRankArray(data[block-1]).ToList();
            }
            else
            {
                ranks = ulongToRankArray(data[block]).ToList();
            }
            int positionInRanks = ranks.Select(x => x.Item2).ToList().BinarySearch(onesInBlock);
            return block * 64 + ranks[positionInRanks].Item1;
        }

        internal string GetDataAsString()
        {
            var result = string.Join("", data.Select(x=>x.ToChars()));
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
            var result = new List<(int, int)>();
            ulong extractor = ulong.MaxValue >> 1;
            for (int i = 0; i < 64; i++)
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
        internal static char[] ToChars(this ulong value)
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
