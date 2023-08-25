using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;
using VDS.RDF.Shacl.Validation;

namespace k2extensionsLib
{
    /// <summary>
    /// Base class for the Leaf Rank extension
    /// </summary>
    public abstract class LeafRank : IK2Extension
    {
        /// <summary>
        /// Size of the matrix
        /// </summary>
        int _Size
        {
            get
            {
                return Math.Max(Subjects.Length, Objects.Length);
            }
        }
        /// <summary>
        /// Separate data structure
        /// </summary>
        protected virtual FlatPopcount _Labels { get; set; } = new();
        /// <summary>
        /// Stores the number of ones until the last layer
        /// </summary>
        protected int _RankUntilLeaves { get; set; }
        /// <summary>
        /// Data of the k^2 tree
        /// </summary>
        protected FlatPopcount _T { get; set; } = new();
        /// <summary>
        /// Used k
        /// </summary>
        protected int _K { get; set; }

        public INode[] Subjects { get; set; } = Array.Empty<INode>();
        public INode[] Objects { get; set; } = Array.Empty<INode>();
        public INode[] Predicates { get; set; } = Array.Empty<INode>();
        public int StorageSpace
        {
            get
            {
                int result = _T.Length() / 8;
                result += _Labels.Length() / 8;
                return result;
            }
        }

        public LeafRank(int k)
        {
            _K = k;
        }

        public void Compress(TripleStore graph, bool useK2Triples)
        {
            var labels = new List<ulong>();
            Subjects = graph.Triples.Select(x => x.Subject).Distinct().ToArray();
            Objects = graph.Triples.Select(x => x.Object).Distinct().ToArray();
            if (useK2Triples)
            {
                var so = Subjects.Intersect(Objects);
                Subjects = so.Concat(Subjects.Except(so)).ToArray();
                Objects = so.Concat(Objects.Except(so)).ToArray();
            }
            Predicates = graph.Triples.Select(x => x.Predicate).Distinct().ToArray();

            int size = Math.Max(Subjects.Length, Objects.Length);
            int N = _K;
            int h = 1;
            while (N < size)
            {
                N *= _K;
                h++;

            }

            var levels = new List<DynamicBitArray>();
            for (int i = 0; i < h; i++)
            {
                levels.Add(new DynamicBitArray());
            }

            //compressRec(ref levels, ref labels, 0, graph, 0, 0, N, k);
            var root = _BuildK2Tree(graph, h);

            _FindPaths(root, ref levels, ref labels, 0);

            _BuildLabels(labels);

            var n = new DynamicBitArray();
            foreach (var l in levels.Take(levels.Count - 1))
            {
                n.AddRange(l);
            }
            int startLeaves = (n.data.Count - 1) * 64 + n.lastUsedIndex + 1;
            n.AddRange(levels.Last());
            _T = new FlatPopcount(n);
            _RankUntilLeaves = _T.Rank1(startLeaves-1);
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            //buttom-up approach
            var result = new List<Triple>();
            int positionInTypes = Array.IndexOf(Predicates, p);
            List<int> nodesWithType = _GetNodesWithPredicate(positionInTypes);
            List<(long, int, int)> cellStore = new();
            foreach (var n in nodesWithType)
            {
                //long positionInNodes = _StartLeaves + n * Predicates.Count();
                long positionInNodes = _T.Select1(_RankUntilLeaves + n + 1);
                Tuple<int, int> cell = _GetCell(positionInNodes, ref cellStore);
                var r = new Triple(Subjects.ElementAt(cell.Item1), Predicates[positionInTypes], Objects.ElementAt(cell.Item2));
                result.Add(r);
            }
            return result.ToArray();

            //top-down approach:
            //(int?, int?)[] path = new (int?, int?)[_Size.ToBase(_K).Length];
            //Array.Fill(path, (null, null));
            //Triple[] result = _FindNodesRec(0, path.ToList(), Array.IndexOf(Predicates, p), new List<(int, int)>());
            //return result;
        }

        public Triple[] Connections(INode s, INode o)
        {
            List<(int?, int?)> path = Enumerable.Zip(Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>(), Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>()).ToList();
            var result = _FindNodesRec(0, path, null, new List<(int, int)>());
            return result;
        }

        public Triple[] Decomp()
        {
            (int?, int?)[] path = new (int?, int?)[_Size.ToBase(_K).Length];
            Array.Fill(path, (null, null));
            Triple[] result = _FindNodesRec(0, path.ToList(), null, new List<(int, int)>());
            return result;
        }

        public Triple[] Exists(INode s, INode p, INode o)
        {
            List<(int?, int?)> path = Enumerable.Zip(Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>(), Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>()).ToList();
            var result = _FindNodesRec(0, path, Array.IndexOf(Predicates.ToArray(),p), new List<(int, int)>());
            return result;
        }

        public Triple[] Prec(INode o)
        {
            List<(int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)null, (int?)obj.v)).ToList();
            Triple[] result = _FindNodesRec(0, path, null, new List<(int, int)>());
            return result;
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            List<(int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)null, (int?)obj.v)).ToList();
            Triple[] result = _FindNodesRec(0, path, Array.IndexOf(Predicates.ToArray(), p), new List<(int, int)>());
            return result;
        }

        public Triple[] Succ(INode s)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)subj.v, (int?)null)).ToList();

            Triple[] result = _FindNodesRec(0, path, null, new List<(int, int)>());
            return result;
        }

        public Triple[] SuccOfType(INode s, INode p)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)subj.v, (int?)null)).ToList();

            Triple[] result = _FindNodesRec(0, path, Array.IndexOf(Predicates.ToArray(), p), new List<(int, int)>());
            return result;
        }

        /// <summary>
        /// Builds and stores the separate data structure
        /// </summary>
        /// <param name="labels">List of all labels encoded as bit masks. Each entry belongs to one node pair/one leaf in the tree</param>
        protected abstract void _BuildLabels(List<ulong> labels);

        /// <summary>
        /// Get all prediactes using the separate datastructure based on a position in the leaves
        /// </summary>
        /// <param name="position">Position of the leaf in the bitstream</param>
        /// <returns>All predicates belonging to the node pair</returns>
        protected abstract INode[] _GetPredicatesFromLeafPosition(int position);

        /// <summary>
        /// Checks whether a leaf positon (identified by its rank) has a specific predicate
        /// </summary>
        /// <param name="rankInLeaves">Rank of the leaf</param>
        /// <param name="predicate">Position of the predicate in the bit mask</param>
        /// <returns>True if the predicate exists</returns>
        protected abstract bool _PositionHasPredicate(int rankInLeaves, int predicate);

        /// <summary>
        /// Get the ranks of all node pairs having a specific type/predicate
        /// </summary>
        /// <param name="predicate">Position of the predicate in the bit mask</param>
        /// <returns>List of all ranks having the predicate</returns>
        protected abstract List<int> _GetNodesWithPredicate(int predicate);

        /// <summary>
        /// Builds the pointer-based k^2 tree and store the belonging labels at the leaves
        /// </summary>
        /// <param name="g">Graph containing the data</param>
        /// <param name="h">height of the tree</param>
        /// <returns>Root of the tree</returns>
        private TreeNode _BuildK2Tree(TripleStore g, int h)
        {
            var root = new TreeNode(_K * _K);

            var subs = Subjects.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
            var objs = Objects.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);

            var paths = from t in g.Triples
                        group t by new
                        {
                            s = subs[t.Subject],
                            o = objs[t.Object],
                        } into subjObjGroup
                        select (Enumerable.Zip(subjObjGroup.Key.s.ToBase(_K, h), subjObjGroup.Key.o.ToBase(_K, h)), subjObjGroup.Select(x => x.Predicate));

            foreach ((IEnumerable<(int, int)>, IEnumerable<INode>) path in paths)
            {
                var currentNode = root;
                var child = new TreeNode(_K * _K);
                foreach ((int, int) level in path.Item1) //Loop through path
                {
                    int quadrant = level.Item1 * _K + level.Item2;
                    child = new TreeNode(_K * _K);
                    currentNode = currentNode.SetChild(quadrant, child);
                }
                ulong label = 0;
                for (int l = 0; l < Predicates.Length; l++)
                {
                    if (path.Item2.Contains(Predicates[l]))
                    {
                        label += (ulong)1 << (63 - l);
                    }
                }
                currentNode.Label = label;
            }

            return root;
        }

        /// <summary>
        /// Translates a pointer-bases tree to a bitstream. The labels are stored in parallel
        /// </summary>
        /// <param name="node">Current node</param>
        /// <param name="dynT">Tree as bitstream stored level wise</param>
        /// <param name="labels">Container that stores the labels as bit mask for each leaf</param>
        /// <param name="level">Current level</param>
        private void _FindPaths(TreeNode node, ref List<DynamicBitArray> dynT, ref List<ulong> labels, int level)
        {
            if (level == dynT.Count)
            {
                labels.Add(node.Label);
                return;
            }
            uint n = 0;
            for (int i = 0; i < _K * _K; i++)
            {
                var child = node.GetChild(i);
                if (child != null)
                {
                    n += (uint)1 << (31 - i);
                    _FindPaths(child, ref dynT, ref labels, level + 1);
                }
            }
            dynT[level].AddRange(n, _K * _K);
        }

        /// <summary>
        /// Returns the cell-coordinates for a specific position in the leaves
        /// </summary>
        /// <param name="position">Current position in bit stream</param>
        /// <param name="cellStore">Container storing positions that were already visited with their (partly) cell coordinates</param>
        /// <returns></returns>
        private Tuple<int, int> _GetCell(long position, ref List<(long,int,int)> cellStore)
        {
            int power = 1;
            int col = 0;
            int row = 0;
            List<(long,int,int)> parent = new();
            int h = 1;
            while (position > 0)
            {
                
                int submatrixPos = (int)position % (_K * _K);
                int submatrixColumn = submatrixPos % _K;
                int submatrixRow = submatrixPos / _K;
                if (cellStore.Count - h > 0 && cellStore[^h].Item1 == position)
                {
                    row += cellStore[^h].Item2;
                    col += cellStore[^h].Item3;
                    parent.Add((position, cellStore[^h].Item2, cellStore[^h].Item3));
                    break;
                }
                else
                {
                    parent.Add((position, submatrixRow * power, submatrixColumn * power));
                }
                col += submatrixColumn * power;
                row += submatrixRow * power;            
                int numberOf1Bits = (int)position / (_K * _K);
                if (numberOf1Bits == 0)
                    position = 0;
                else
                    position = _T.Select1(numberOf1Bits);
                power *= _K;
                h++;
            }
            int sum_row = 0;
            int sum_col = 0;
            parent.Reverse();
            List<(long, int, int)> path = new();
            for (int i = 0; i< parent.Count; i++)
            {
                var p = parent[i];
                sum_row += p.Item2;
                sum_col += p.Item3;
                path.Add((p.Item1, sum_row, sum_col));
            }
            cellStore = cellStore.Take(cellStore.Count - h).Concat(path).ToList();
            return new Tuple<int, int>(row, col);
        }

        /// <summary>
        /// Calculate the triples of the given search path
        /// </summary>
        /// <param name="positionInNodes">Current position in <see cref="T"/></param>
        /// <param name="searchPath">Search path for traversing the tree</param>
        /// <param name="predicate">Optional position of the predicate in the bit mask. Null, if the request uses an unbounded predicate</param>
        /// <param name="parentPath">Path to the current position"/></param>
        /// <returns>Found triples</returns>
        private Triple[] _FindNodesRec(int positionInNodes, List<(int?, int?)> searchPath, int? predicate, List<(int, int)> parentPath)
        {
            var result = new List<Triple>();
            (int?, int?) position = searchPath[0];
            searchPath = searchPath.Skip(1).ToList();
            for (int s = position.Item1 ?? 0; s < (position.Item1 + 1 ?? _K); s++)
            {
                for (int o = position.Item2 ?? 0; o < (position.Item2 + 1 ?? _K); o++)
                {
                    int relativePosition = s * _K + o;
                    int pos = positionInNodes + relativePosition;
                    List<(int, int)> parent = parentPath.Append((s, o)).ToList();
                    if (searchPath.Count == 0 && _T[pos])
                    {
                        int posS = parent.Select(x => x.Item1).FromBase(_K);
                        int posO = parent.Select(x => x.Item2).FromBase(_K);
                        int rankInLeaves = _T.Rank1(pos) - 1 - _RankUntilLeaves;
                        if (predicate == null)
                        {
                            INode[] preds = _GetPredicatesFromLeafPosition(rankInLeaves);
                            if (preds.Length != 0) result.AddRange(preds.Select(x => new Triple(Subjects.ElementAt(posS), x, Objects.ElementAt(posO))));
                        }
                        else if (_PositionHasPredicate(rankInLeaves, predicate.Value))
                        {
                            result.Add(new Triple(Subjects.ElementAt(posS), Predicates[predicate.Value], Objects.ElementAt(posO)));
                        }
                    }
                    else if (_T[pos])
                    {
                        pos = _T.Rank1(pos) * _K * _K;
                        result.AddRange(_FindNodesRec(pos, searchPath, predicate, parent));
                    }
                }
            }
            return result.ToArray();
        }
    }

    public class LeafRankV1 : LeafRank
    {
        public LeafRankV1(int k) : base(k) { }

        protected override void _BuildLabels(List<ulong> labels)
        {
            var dynamicLabels = new DynamicBitArray();
            foreach (var label in labels)
            {
                dynamicLabels.AddRange(label, Predicates.Length);
            }
            _Labels = new FlatPopcount(dynamicLabels);
        }

        protected override INode[] _GetPredicatesFromLeafPosition(int position)
        {
            ulong[] l = _Labels[(Predicates.Length * position)..(Predicates.Length * position + Predicates.Length)];
            var result = _GetPredicatesFromBitmask(l);
            return result.ToArray();
        }

        protected override bool _PositionHasPredicate(int position, int predicate)
        {
            ulong[] labels = _Labels[(Predicates.Length * position)..(Predicates.Length * position + Predicates.Length)];
            return (labels[0] & (1UL << (63 - predicate))) != 0;
        }

        protected override List<int> _GetNodesWithPredicate(int positionOfType)
        {
            var result = new List<int>();
            int counter = positionOfType;
            int index = 0;
            while (counter < _Labels.Length())
            {
                if (_Labels[counter]) result.Add(index);
                counter += Predicates.Length;
                index++;
            }
            return result;
        }

        /// <summary>
        /// Extracts the prediactes from a bitmaks
        /// </summary>
        /// <param name="bitmask">Used bit mask</param>
        /// <returns>Extracted predicates</returns>
        private INode[] _GetPredicatesFromBitmask(ulong[] bitmask)
        {
            int position = 0;
            var result = new List<INode>();
            foreach (var block in bitmask)
            {
                for (int j = 63; j >= 0; j--)
                {
                    if ((block & ((ulong)1 << j)) != 0)
                    {
                        result.Add(Predicates.ElementAt(position));
                    }
                    position++;
                    if (position >= Predicates.Length)
                    {
                        return result.ToArray();
                    }
                }
            }
            return result.ToArray();
        }
    }

    public class LeafRankK2 : LeafRank
    {
        /// <summary>
        /// Stores the k^2 tree
        /// </summary>
        private K2Tree _LabelTree { get; set; } = new(0, 0, 0);
        protected override FlatPopcount _Labels { get => _LabelTree.T; set => _LabelTree.T = value; }

        public LeafRankK2(int k) : base(k) { }

        protected override void _BuildLabels(List<ulong> labels)
        {
            _LabelTree = new K2Tree(_K, labels.Count, Predicates.Length);
            var cells = labels.SelectMany((x, i) => _ExtractLabelPositions(x).Select(y => (i, y)));
            _LabelTree.Store(cells);
        }

        protected override INode[] _GetPredicatesFromLeafPosition(int position)
        {
            List<(int?, int?)> path = (from p in position.ToBase(_K, _LabelTree._Size.ToBase(_K).Length)
                                       select ((int?)p, (int?)null)).ToList();

            var cells = _LabelTree.FindNodes(0, path, new List<(int, int)>());
            var result = cells.Select(x => Predicates[x.Item2]);
            return result.ToArray();
        }

        protected override bool _PositionHasPredicate(int position, int predicate)
        {
            List<(int?, int?)> path = Enumerable.Zip(position.ToBase(_K, _LabelTree._Size.ToBase(_K).Length).Cast<int?>(), predicate.ToBase(_K, _LabelTree._Size.ToBase(_K).Length).Cast<int?>()).ToList();

            var cells = _LabelTree.FindNodes(0, path, new List<(int, int)>());
            return cells.Length != 0;

        }

        protected override List<int> _GetNodesWithPredicate(int positionOfType)
        {
            List<(int?, int?)> path = (from p in positionOfType.ToBase(_K, _LabelTree._Size.ToBase(_K).Length)
                                       select ((int?)null, (int?)p)).ToList();

            var cells = _LabelTree.FindNodes(0, path, new List<(int, int)>());
            var result = cells.Select(x => x.Item1);
            return result.ToList();
        }

        /// <summary>
        /// Get the position of existing predicates in a bit mask
        /// </summary>
        /// <param name="label">Bit mask</param>
        /// <returns>Positon of existing predicates</returns>
        private List<int> _ExtractLabelPositions(ulong label)
        {
            ulong mask = 1ul << 63;
            var result = new List<int>();
            for (int i = 0; i < Predicates.Length; i++)
            {
                if ((mask & label) != 0)
                {
                    result.Add(i);
                }
                mask >>= 1;
            }
            return result;
        }
    }

}
