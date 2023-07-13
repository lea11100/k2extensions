using ICSharpCode.SharpZipLib.Zip;
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
    public abstract class K2ArrayIndex : IK2Extension
    {
        int _Size
        {
            get
            {
                return Math.Max(Subjects.Length, Objects.Length);
            }
        }
        protected virtual FlatPopcount _Labels { get; set; } = new();
        protected int _RankUntilLeaves { get; set; }
        protected FlatPopcount _T { get; set; } = new ();
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

        public K2ArrayIndex(int k)
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
                Subjects = so.Concat(Subjects.Where(x => !so.Contains(x))).ToArray();
                Objects = so.Concat(Objects.Where(x => !so.Contains(x))).ToArray();
            }
            else
            {
                Subjects = Subjects.Concat(Objects).Distinct().ToArray();
                Objects = Subjects;
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
            _RankUntilLeaves = _T.Rank1(startLeaves);
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            var result = new List<Triple>();
            int positionInTypes = Array.IndexOf(Predicates, p);
            List<int> nodesWithType = _GetNodesWithType(positionInTypes);
            foreach (var n in nodesWithType)
            {
                //long positionInNodes = _StartLeaves + n * Predicates.Count();
                long positionInNodes = _T.Select1(_RankUntilLeaves + n + 1);
                Tuple<int, int> cell = _GetCell(positionInNodes);
                var r = new Triple(Subjects.ElementAt(cell.Item1), Predicates[positionInTypes], Objects.ElementAt(cell.Item2));
                result.Add(r);
            }
            return result.ToArray();
        }

        public Triple[] Connections(INode s, INode o)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       where subj.i == obj.i
                                       select ((int?)subj.v, (int?)obj.v)).ToList();

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
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       where subj.i == obj.i
                                       select ((int?)subj.v, (int?)obj.v)).ToList();

            var result = _FindNodesRec(0, path, p, new List<(int, int)>());
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
            Triple[] result = _FindNodesRec(0, path, p, new List<(int, int)>());
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

            Triple[] result = _FindNodesRec(0, path, p, new List<(int, int)>());
            return result;
        }

        protected abstract void _BuildLabels(List<ulong> labels);

        protected abstract INode[] _GetLabelFromLeafPosition(int position);

        protected abstract List<int> _GetNodesWithType(int positionOfType);

        private TreeNode _BuildK2Tree(TripleStore g, int h)
        {
            var root = new TreeNode(_K * _K);

            var paths = from t in g.Triples
                        group t by new
                        {
                            s = Array.IndexOf(Subjects.ToArray(), t.Subject),
                            o = Array.IndexOf(Objects.ToArray(), t.Object),
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
                currentNode.SetLabel(label);
            }

            return root;
        }

        private void _FindPaths(TreeNode node, ref List<DynamicBitArray> dynT, ref List<ulong> labels, int level)
        {
            if (level == dynT.Count)
            {
                labels.Add(node.GetLabel());
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
        /// <param name="position"></param>
        /// <returns></returns>
        private Tuple<int, int> _GetCell(long position)
        {
            int power = 1;
            int col = 0;
            int row = 0;
            while (position > 0)
            {
                int submatrixPos = (int)position % (_K * _K);
                int submatrixColumn = submatrixPos % _K;
                int submatrixRow = submatrixPos / _K;
                col += submatrixColumn * power;
                row += submatrixRow * power;
                int numberOf1Bits = (int)position / (_K * _K);
                if (numberOf1Bits == 0)
                    position = 0;
                else
                    position = _T.Select1(numberOf1Bits);
                power *= _K;
            }
            return new Tuple<int, int>(row, col);
        }

        private Triple[] _FindNodesRec(int positionInNodes, List<(int?, int?)> searchPath, INode? predicate, List<(int, int)> parentPath)
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
                        INode[] preds = _GetLabelFromLeafPosition(rankInLeaves);
                        if (predicate != null) preds = preds.Where(x => x.Equals(predicate)).ToArray();
                        if (preds.Length != 0) result.AddRange(preds.Select(x => new Triple(Subjects.ElementAt(posS), x, Objects.ElementAt(posO))));
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

    public class K2ArrayIndexPositional : K2ArrayIndex
    {
        public K2ArrayIndexPositional(int k) : base(k) { }

        protected override void _BuildLabels(List<ulong> labels)
        {
            var dynamicLabels = new DynamicBitArray();
            foreach (var label in labels)
            {
                dynamicLabels.AddRange(label, Predicates.Length);
            }
            _Labels = new FlatPopcount(dynamicLabels);
        }

        protected override INode[] _GetLabelFromLeafPosition(int position)
        {
            ulong[] l = _Labels[(Predicates.Length * position)..(Predicates.Length * position + Predicates.Length)];
            var result = _GetPredicatesFromBitStream(l);
            return result.ToArray();
        }

        protected override List<int> _GetNodesWithType(int positionOfType)
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

        private INode[] _GetPredicatesFromBitStream(ulong[] stream)
        {
            int position = 0;
            var result = new List<INode>();
            foreach (var block in stream)
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

    public class K2ArrayIndexK2 : K2ArrayIndex
    {
        private K2Tree _LabelTree { get; set; } = new(0,0,0);
        protected override FlatPopcount _Labels { get => _LabelTree.T; set => _LabelTree.T = value; }

        public K2ArrayIndexK2(int k) : base(k) { }

        protected override void _BuildLabels(List<ulong> labels)
        {
            _LabelTree = new K2Tree(_K, labels.Count, Predicates.Length);
            var cells = labels.SelectMany((x, i) => _ExtractLabelPositions(x).Select(y => (i, y)));
            _LabelTree.Store(cells);
        }

        protected override INode[] _GetLabelFromLeafPosition(int position)
        {
            List<(int?, int?)> path = (from p in position.ToBase(_K, _LabelTree._Size.ToBase(_K).Length)                                     
                                       select ((int?)p, (int?)null)).ToList();

            var cells = _LabelTree.FindNodes(0, path, new List<(int, int)>());
            var result = cells.Select(x => Predicates[x.Item2]);
            return result.ToArray();
        }

        protected override List<int> _GetNodesWithType(int positionOfType)
        {
            List<(int?, int?)> path = (from p in positionOfType.ToBase(_K, _LabelTree._Size.ToBase(_K).Length)
                                       select ((int?)null, (int?)p)).ToList();

            var cells = _LabelTree.FindNodes(0, path, new List<(int, int)>());
            var result = cells.Select(x => x.Item1);
            return result.ToList();
        }

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
