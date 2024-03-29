﻿using HtmlAgilityPack;
using J2N.Collections.Generic.Extensions;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Util;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using VDS.RDF;
using VDS.RDF.Query.Algebra;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace k2extensionsLib
{
    /// <summary>
    /// Implementation of a k^2 tree
    /// </summary>
    public class K2Tree
    {
        /// <summary>
        /// Used k
        /// </summary>
        int _K { get; }
        /// <summary>
        /// Size of the matrix
        /// </summary>
        internal int _Size { get { return Math.Max(_Row, _Cols); } }
        /// <summary>
        /// Number of rows
        /// </summary>
        internal int _Row { get; }
        /// <summary>
        /// Number of columns
        /// </summary>
        internal int _Cols { get; }
        /// <summary>
        /// Data
        /// </summary>
        internal FlatPopcount T { get; set; }

        /// <summary>
        /// Creates empty k^2 tree
        /// </summary>
        /// <param name="k">Used k</param>
        /// <param name="row">Number of rows in adjacency matrix</param>
        /// <param name="cols">Number of columns adjacency matrix</param>
        public K2Tree(int k, int row, int cols)
        {
            T = new FlatPopcount();
            _K = k;
            _Row = row;
            _Cols = cols;
        }

        /// <summary>
        /// Stores given adjacency matrix using a k^2 tree
        /// </summary>
        /// <param name="cells">Cell coordinations for cells with value 1</param>
        public void Store(IEnumerable<(int, int)> cells)
        {
            int size = Math.Max(_Row, _Cols);
            int N = _K;
            int h = 1;
            while (N < size)
            {
                N *= _K;
                h++;
            }
            var root = new TreeNode(_K * _K);
            var paths = from c in cells.ToArray()
                        select Enumerable.Zip(c.Item1.ToBase(_K, h), c.Item2.ToBase(_K, h));

            foreach (IEnumerable<(int, int)> path in paths)
            {
                var currentNode = root;
                foreach ((int, int) p in path)
                {
                    int quadrant = p.Item1 * _K + p.Item2;
                    var child = new TreeNode(_K * _K);
                    currentNode = currentNode.SetChild(quadrant, child);
                }

            }

            List<DynamicBitArray> dynT = new();
            for (int i = 0; i < h; i++)
            {
                dynT.Add(new DynamicBitArray());
            }
            _FindPaths(root, ref dynT, 0);
            var flatT = new DynamicBitArray();
            for (int i = 0; i < dynT.Count; i++)
            {
                flatT.AddRange(dynT[i]);
            }
            T = new FlatPopcount(flatT);
        }

        /// <summary>
        /// Calculate those cell coodinates having value one with respect to the given search path
        /// </summary>
        /// <param name="positionInNodes">Current position in <see cref="T"/></param>
        /// <param name="searchPath">Search path for traversing the tree</param>
        /// <param name="parentPath">Path to the current position"/></param>
        /// <returns>Found cell coodinates</returns>
        public (int, int)[] FindNodes(int positionInNodes, List<(int?, int?)> searchPath, List<(int, int)> parentPath)
        {
            var result = new List<(int, int)>();
            (int?, int?) position = searchPath[0];
            searchPath = searchPath.Skip(1).ToList();
            for (int s = position.Item1 ?? 0; s < (position.Item1 + 1 ?? _K); s++)
            {
                for (int o = position.Item2 ?? 0; o < (position.Item2 + 1 ?? _K); o++)
                {
                    int relativePosition = s * _K + o;
                    int pos = positionInNodes + relativePosition;
                    List<(int, int)> parent = parentPath.Append((s, o)).ToList();
                    if (searchPath.Count == 0 && T[pos])
                    {
                        int posR = parent.Select(x => x.Item1).FromBase(_K);
                        int posC = parent.Select(x => x.Item2).FromBase(_K);
                        result.Add((posR, posC));
                    }
                    else if (T[pos])
                    {
                        pos = T.Rank1(pos) * _K * _K;
                        result.AddRange(FindNodes(pos, searchPath, parent));
                    }
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Translates a pointer-bases tree to a bitstream
        /// </summary>
        /// <param name="node">Current node</param>
        /// <param name="dynT">Tree as bitstream stored level wise</param>
        /// <param name="level">Current level</param>
        private void _FindPaths(TreeNode node, ref List<DynamicBitArray> dynT, int level)
        {
            if (level == dynT.Count)
            {
                return;
            }
            uint n = 0;
            for (int i = 0; i < _K * _K; i++)
            {
                var child = node.GetChild(i);
                if (child != null)
                {
                    n += (uint)1 << (31 - i);
                    _FindPaths(child, ref dynT, level + 1);
                }
            }
            dynT[level].AddRange(n, _K * _K);
        }
    }

    /// <summary>
    /// Class representing a node in a pointer-based tree
    /// </summary>
    public class TreeNode
    {
        /// <summary>
        /// Children of the node
        /// </summary>
        public TreeNode[] Children { get; set; }
        /// <summary>
        /// Optional label of the node. Only used in Leaf Rank
        /// </summary>
        public ulong Label { get; set; }

        /// <summary>
        /// Creates node with the given number of children
        /// </summary>
        /// <param name="children">Number of children</param>
        public TreeNode(int children)
        {
            Children = new TreeNode[children];
        }

        /// <summary>
        /// Sets a child of the node. If the child alread exists the child is not reset.
        /// </summary>
        /// <param name="index">Index of the child that needs to be set</param>
        /// <param name="child">Value of the child</param>
        /// <returns>Either the new set child or the existing one</returns>
        public TreeNode SetChild(int index, TreeNode child)
        {
            Children[index] = Children[index] ?? child;
            return Children[index];
        }

        /// <summary>
        /// Get child at a specific index
        /// </summary>
        /// <param name="index">Index of the child</param>
        /// <returns>Child a the index</returns>
        public TreeNode GetChild(int index)
        {
            return Children[index];
        }
    }

    /// <summary>
    /// Class that stores bitstream of a variable length and extend it during runtime
    /// </summary>
    public class DynamicBitArray
    {
        /// <summary>
        /// List storing the data left aligned
        /// </summary>
        internal List<ulong> data { get; set; } = new List<ulong>() { 0 };
        /// <summary>
        /// Index of the last set position in the last piece of data
        /// </summary>
        internal int lastUsedIndex { get; set; } = -1;

        /// <summary>
        /// Add a piece of data having the size of 32 bit
        /// </summary>
        /// <param name="array">Left aligned data that needs to be add</param>
        /// <param name="length">Actual length of the data</param>
        public void AddRange(uint array, int length)
        {
            AddRange(((ulong)array) << 32, length);
        }

        /// <summary>
        /// Add a piece of data having the size of 64 bit
        /// </summary>
        /// <param name="array">Left aligned data that needs to be add</param>
        /// <param name="length">Actual length of the data</param>
        public void AddRange(ulong array, int length)
        {
            ulong firstPart = array >>> (lastUsedIndex + 1);
            ulong secondPart = array << (63 - lastUsedIndex);
            if (lastUsedIndex == 63)
            {
                data.Add(firstPart);
                lastUsedIndex = length - 1;
            }
            else
            {
                data[^1] += firstPart;
                lastUsedIndex += length;
                if (lastUsedIndex >= 64)
                    data.Add(secondPart);
                lastUsedIndex %= 64;
            }
        }

        /// <summary>
        /// Concats with another <see cref="DynamicBitArray"/>
        /// </summary>
        /// <param name="array">Other <see cref="DynamicBitArray"/></param>
        public void AddRange(DynamicBitArray array)
        {
            foreach (ulong item in array.data.Take(array.data.Count - 1))
            {
                AddRange(item, 64);
            }
            AddRange(array.data[^1], array.lastUsedIndex + 1);
        }
    }


    /// <summary>
    /// FlatPopcount data structure
    /// </summary>
    public class FlatPopcount
    {
        //L0-index missing, since optional

        /// <summary>
        /// Interleaved L1 and L2 indices
        /// </summary>
        private UInt128[] _L1L2Index { get; set; }
        /// <summary>
        /// Actual data of the bitstream
        /// </summary>
        private ulong[] _Data { get; set; }
        /// <summary>
        /// Position of every 8192th one
        /// </summary>
        private long[] _SampelsOfOnePositions { get; set; }

        /// <summary>
        /// Creates an empty data structure
        /// </summary>
        public FlatPopcount()
        {
            _Data = Array.Empty<ulong>();
            _L1L2Index = Array.Empty<UInt128>();
            _SampelsOfOnePositions = Array.Empty<long>();
        }

        /// <summary>
        /// Creates the data structure from a <see cref="DynamicBitArray"/>
        /// </summary>
        /// <param name="array">Contains the data that needs to be stored</param>
        public FlatPopcount(DynamicBitArray array)
        {
            _Data = array.data.ToArray();
            _L1L2Index = new UInt128[(int)Math.Ceiling((double)_Data.Length / 64)];
            _SampelsOfOnePositions = Array.Empty<long>();
            Init();
        }

        /// <summary>
        /// Size of the datastructure in bits without the overhead
        /// </summary>
        /// <returns>Size of the datastructure in bits</returns>
        internal int Length()
        {
            return _Data.Length * 64;
        }

        /// <summary>
        /// Initiates the L1 and L2 indices and stores the samples of one positions 
        /// </summary>
        private void Init()
        {
            ulong l1Index = 0;
            var sampels = new List<long>() { 0 };
            int i = 0;
            foreach (var l1 in _Data.Chunk(64))
            {
                _L1L2Index[i] = ((UInt128)l1Index)<<84;
                uint l2Index = 0;
                int j = 0;
                foreach (var l2 in l1.Chunk(8))
                {
                    uint onesInL2 = (uint)l2.Select(BitOperations.PopCount).Sum();
                    if (j < 7)
                    {
                        _L1L2Index[i] += (UInt128)(l2Index + onesInL2) << ((6 - j) * 12);
                    }
                    if ((l2Index + l1Index) >>> 13 != (l2Index + onesInL2 + l1Index) >>> 13)
                    {
                        ulong remainingOnes = ((((l2Index + l1Index) >>> 13) + 1) << 13) - (l2Index + l1Index);
                        int relativePosition = Select1In512((int)remainingOnes, l2);
                        sampels.Add(i * 4096 + j * 512 + relativePosition);
                    }
                    l2Index += onesInL2;
                    j++;
                }
                while(j < 7)
                {
                    _L1L2Index[i] += ((UInt128)l2Index) << ((6 - j) * 12);
                    j++;
                }
                l1Index += l2Index;
                i++;
            }
            _SampelsOfOnePositions = sampels.ToArray();
        }

        /// <summary>
        /// Performs a select_1 query on a 512 bit large bitstream
        /// </summary>
        /// <param name="T">Bitstream of size 512 bit</param>
        /// <param name="n">Rank of the one that needs to be selected</param>
        /// <returns>Position of the n-th one</returns>
        private static int Select1In512(int n, ulong[] T)
        {
            int oneCounter = 0;
            int index = 0;
            foreach (var item in T)
            {
                int popcnt = BitOperations.PopCount(item);
                oneCounter += popcnt;
                if (oneCounter < n)
                {
                    index += 64;
                    continue;
                }
                int remainingOnes = n - oneCounter + popcnt;
                ulong mask = 1UL << (popcnt - remainingOnes);
                ulong pbd = Bmi2.X64.ParallelBitDeposit(mask, item);
                return index + (int)ulong.LeadingZeroCount(pbd);
            }
            throw new Exception();
        }

        /// <summary>
        /// Gets the value of the bit at the given position
        /// </summary>
        /// <param name="key">Position</param>
        /// <returns>Value of the bit</returns>
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

        /// <summary>
        /// Gets the left aligned stream of bits in the given range
        /// </summary>
        /// <param name="range">Range of positions thats needs to be extracted</param>
        /// <returns>Left aligned bit stream</returns>
        internal ulong[] this[Range range]
        {
            get
            {
                int startBlock = range.Start.Value / 64;
                int startPosition = range.Start.Value % 64;
                int endBlock = range.End.Value / 64;
                int endPosition = range.End.Value % 64;
                ulong[] result = endPosition == 0 ? _Data[startBlock..endBlock].Append(0UL).ToArray() : _Data[startBlock..(endBlock + 1)];
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

        /// <summary>
        /// Perform a select_1 query on the data
        /// </summary>
        /// <param name="n">Rank of the one that needs to be selected</param>
        /// <returns>Position of the n-th one</returns>
        internal long Select1(long n)
        {
            //Get L1
            long position = _SampelsOfOnePositions[n >> 13];
            int l1 = (int)(position >> 12);
            position = l1 << 12;
            long remainingOnes = n;
            while ((l1 + 1) < _L1L2Index.Length && getL1(l1 + 1) < n)
            {
                l1++;
                position += 1L << 12;
            }
            remainingOnes -= getL1(l1);

            //Get L2
            byte[] bytes = BitConverter.GetBytes((ulong)(_L1L2Index[l1] >>> 64)).Reverse().Concat(BitConverter.GetBytes((ulong)_L1L2Index[l1]).Reverse()).ToArray(); //"Reverse()" to convert to big endian
            Vector128<byte> v = Vector128.Create(bytes);
            //Positions:    00 01 02 03 04 05 06 07 08 09 10 11 12 13 14 15
            //Shuffeled:    -1 -1 05 06 07 08 08 09 10 11 11 12 13 14 14 15
            //To big endian:-1 -1 06 05 08 07 09 08 11 10 12 11 14 13 15 14
            Vector128<byte> shuffle_mask = Vector128.Create(-1, -1, 06, 05, 08, 07, 09, 08, 11, 10, 12, 11, 14, 13, 15, 14).AsByte();
            Vector128<byte> v_shuffeled = Ssse3.Shuffle(v.AsByte(), shuffle_mask);
            Vector128<ushort> upper = Sse2.And(v_shuffeled.AsUInt16(), Vector128.Create((ushort)0b111111111111));
            Vector128<ushort> lower = Sse2.ShiftRightLogical(v_shuffeled.AsUInt16(), 4);
            Vector128<ushort> blocks = Sse41.Blend(lower, upper, 0b10101010);
            Vector128<short> comp = Sse2.CompareGreaterThan(Vector128.Create((short)remainingOnes), blocks.AsInt16());
            int mask = Sse2.MoveMask(comp.AsByte());
            var l2 = (int)Popcnt.PopCount((uint)mask) / 2 - 1;
            position += l2 * (1L << 9);
            remainingOnes -= blocks[l2];

            //Get position in L2-Block
            var l2Block = _Data.Skip(l1 * 64 + l2 * 8).Take(8).ToArray();
            position += Select1In512((int)remainingOnes, l2Block);
            return position;
        }

        /// <summary>
        /// Extract the L1 index from the interleved L1/L2 index a the given position
        /// </summary>
        /// <param name="position">Position L1/L2 index in <see cref="_L1L2Index"/></param>
        /// <returns>Value of L1 index</returns>
        private long getL1(int position)
        {
            return (int)(_L1L2Index[position] >>> 84);
        }

        /// <summary>
        /// Performs rank_1 query on the data
        /// </summary>
        /// <param name="p">Position in the data</param>
        /// <returns>Rank of the position</returns>
        internal int Rank1(int p)
        {
            int block = p / 64;
            int l1 = block / 64;
            int l2 = block % 64 / 8;
            int l3 = block % 8;
            int relativePositionInL3 = p % 64;
            int result = getRankByBlocks(l1, l2, l3, relativePositionInL3);
            return result;
        }

        /// <summary>
        /// Calculate the rank based on the given parameters
        /// </summary>
        /// <param name="l1">Position of the L1 block</param>
        /// <param name="l2">Relative position of the L2 block in L1 block</param>
        /// <param name="l3">Relative position of 64 bit block in L2 block</param>
        /// <param name="relativePositionInL3">Relative position L3 block</param>
        /// <returns>Rank of the position</returns>
        private int getRankByBlocks(int l1, int l2, int l3, int relativePositionInL3)
        {
            int result = 0;
            //Get L1
            UInt128 blockIndex = _L1L2Index[l1];
            result += (int)(blockIndex >>> 84);

            //Get L2
            if (l2 != 0)
            {
                blockIndex <<= 44 + (l2 - 1) * 12;
                result += (int)(blockIndex >>> 116);
            }

            //Get 64-Bit-Block in L2-Block
            ulong[] block512 = _Data[(l1 * 64 + l2 * 8)..(l1 * 64 + l2 * 8 + l3)];
            result += block512.Select(BitOperations.PopCount).Sum();

            //Get last block
            ulong blockInBlock512 = _Data[l1 * 64 + l2 * 8 + l3];
            result += BitOperations.PopCount(blockInBlock512 & (ulong.MaxValue << (63 - relativePositionInL3)));

            return result;
        }
    }

    public interface IK2Extension
    {
        /// <summary>
        /// Nodes that belong to the rows of the adjacency matrix
        /// </summary>
        INode[] Subjects { get; set; }
        /// <summary>
        /// Nodes that belong to the columns of the adjacency matrix
        /// </summary>
        INode[] Objects { get; set; }
        /// <summary>
        /// Nodes that belong to the different connection types
        /// </summary>
        INode[] Predicates { get; set; }
        /// <summary>
        /// Size of the raw data structure (without overhead of the rank/select data structure
        /// </summary>
        int StorageSpace { get; }
        /// <summary>
        /// Compress the given <see cref="TripleStore"/> using the compression technique of the class. The compression can use k2-triples.
        /// </summary>
        void Compress(TripleStore graph, bool useK2Triples);
        /// <summary>
        /// Retrieve all <see cref="Triple"/> of the graph
        /// </summary>
        Triple[] Decomp();
        /// <summary>
        /// Retrieve all ingoing edges of a given node
        /// </summary>
        Triple[] Prec(INode o);
        /// <summary>
        /// Retrieve all outgoing edges of a given node
        /// </summary>
        Triple[] Succ(INode s);
        /// <summary>
        /// Retrieve all edges of a specific type which is specified as a <see cref="INode"/>
        /// </summary>
        Triple[] AllEdgesOfType(INode p);
        /// <summary>
        /// Retrieve all edges that connect two given nodes
        /// </summary>
        Triple[] Connections(INode s, INode o);
        /// <summary>
        /// Retrieve all ingoing edges of a given node which belong to a specific edges type
        /// </summary>
        Triple[] PrecOfType(INode o, INode p);
        /// <summary>
        /// Retrieve all outgoing edges of a given node which belong to a specific edges type
        /// </summary>
        Triple[] SuccOfType(INode s, INode p);
        /// <summary>
        /// Indicates whether an edge of a speciffic type exists between to given nodes
        /// </summary>
        Triple[] Exists(INode s, INode p, INode o);
        /// <summary>
        /// Store the tree structure in a given file. This also stores the information about the nodes in <see cref="Subjects"/>, <see cref="Objects"/> and <see cref="Predicates"/>.
        /// </summary>
        /// <param name="filename"></param>
    }

    /// <summary>
    /// Provides general extensions used in the project
    /// </summary>
    internal static class GeneralExtensions
    {
        /// <summary>
        /// Transfroms a decimal number to the k-th base and padds left with zeros if needed to the given length
        /// </summary>
        /// <param name="value">Decimal number</param>
        /// <param name="baseSize">k</param>
        /// <param name="length">Desired length of the number after transformation. If 0, then the number is not padded</param>
        /// <returns>Transformed number in form of a list of numbers where each position represents a digit in the k-th base</returns>
        internal static int[] ToBase(this int value, int baseSize, int length = 0)
        {
            var digits = new Stack<int>();
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

        /// <summary>
        /// Transforms a number to the k-th base to a decimal number
        /// </summary>
        /// <param name="value">Number to the to the k-th base in form of a list of numbers where each position represents a digit in the k-th base</param>
        /// <param name="baseSize">k</param>
        /// <returns>Transformed decimal number</returns>
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

        /// <summary>
        /// Sorts a list for tripled using the order S -> O -> P
        /// </summary>
        /// <param name="list">List that needs to be sorted</param>
        /// <returns>Sorted list</returns>
        internal static IEnumerable<Triple> Sort(this IEnumerable<Triple> list)
        {
            return list.OrderBy(t => t.Subject.ToString()).ThenBy(t => t.Object.ToString()).ThenBy(t => t.Predicate.ToString());
        }
    }
}
