﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;

namespace k2extensionsLib
{
    public class K2ArrayIndex : IK2Extension
    {
        FlatPopcount _T { get; set; }
        FlatPopcount _Labels { get; set; }
        int _StartLeaves { get; set; }
        int _K { get; set; }
        private bool _UseK2Triples { get; set; }
        int _Size
        {
            get
            {
                return Math.Max(Subjects.Length, Objects.Length);
            }
        }

        public INode[] Subjects { get; set; }
        public INode[] Objects { get; set; }
        public INode[] Predicates { get; set; }
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
            _UseK2Triples = false;
            _T = new FlatPopcount();
            _Labels = new FlatPopcount();
            Subjects = Array.Empty<INode>();
            Predicates = Array.Empty<INode>();
            Objects = Array.Empty<INode>();
        }

        public void Compress(IGraph graph, bool useK2Triples)
        {
            var labels = new DynamicBitArray();
            _UseK2Triples = useK2Triples;
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

            _Labels = new FlatPopcount(labels);
            var n = new DynamicBitArray();
            foreach (var l in levels.Take(levels.Count - 1))
            {
                n.AddRange(l);
            }
            _StartLeaves = (n.data.Count - 1) * 64 + n.firstFreeIndex;
            n.AddRange(levels.Last());
            _T = new FlatPopcount(n);
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            var result = new List<Triple>();
            int positionInTypes = Array.IndexOf(Predicates, p);
            var nodesWithType = new List<int>();
            int counter = positionInTypes;
            int index = 0;
            while (counter < _Labels.Length())
            {
                if (_Labels[counter]) nodesWithType.Add(index);
                counter += Predicates.Length;
                index++;
            }
            foreach (var n in nodesWithType)
            {
                //long positionInNodes = _StartLeaves + n * Predicates.Count();
                long positionInNodes = _T.Select1(_T.Rank1(_StartLeaves) + n + 1);
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

        public bool Exists(INode s, INode p, INode o)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       where subj.i == obj.i
                                       select ((int?)subj.v, (int?)obj.v)).ToList();

            var result = _FindNodesRec(0, path, p, new List<(int, int)>());
            return result.Any();
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

        public void Store(string filename)
        {
            using var sw = File.CreateText(filename);
            sw.WriteLine(_StartLeaves);
            sw.WriteLine(_T.GetDataAsString());
            sw.WriteLine(_Labels.GetDataAsString());
            sw.WriteLine(string.Join(" ", Predicates.ToList()));
            if (_UseK2Triples)
            {
                var so = Subjects.Intersect(Objects);
                sw.WriteLine(string.Join(" ", so));
                sw.WriteLine(string.Join(" ", Subjects.Where(x => !so.Contains(x))));
                sw.WriteLine(string.Join(" ", Objects.Where(x => !so.Contains(x))));
            }
            else
            {
                sw.WriteLine(string.Join(" ", Subjects.ToList()));
            }
        }

        public void Load(string filename, bool useK2Triple)
        {
            using var sr = new StreamReader(filename);
            string line = sr.ReadLine() ?? "";
            var nf = new NodeFactory(new NodeFactoryOptions());
            _StartLeaves = int.Parse(line);
            line = sr.ReadLine() ?? "";
            _T.Store(line);
            line = sr.ReadLine() ?? "";
            _Labels.Store(line);
            line = sr.ReadLine() ?? "";
            Predicates = line.Split(" ").Select(x => nf.CreateLiteralNode(x)).ToArray();
            if (useK2Triple)
            {
                line = sr.ReadLine() ?? "";
                var so = line.Split(" ").Select(x => nf.CreateLiteralNode(x));
                line = sr.ReadLine() ?? "";
                Subjects = so.Concat(line.Split(" ").Select(x => nf.CreateLiteralNode(x))).ToArray();
                line = sr.ReadLine() ?? "";
                Objects = so.Concat(line.Split(" ").Select(x => nf.CreateLiteralNode(x))).ToArray();
            }
            else
            {
                line = sr.ReadLine() ?? "";
                Subjects = line.Split(" ").Select(x => nf.CreateLiteralNode(x)).ToArray();
                Objects = Subjects;
            }
        }

        private TreeNode _BuildK2Tree(IGraph g, int h)
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

        private void _FindPaths(TreeNode node, ref List<DynamicBitArray> dynT, ref DynamicBitArray dynLabels, int level)
        {
            if (level == dynT.Count)
            {
                dynLabels.AddRange(node.GetLabel(), Predicates.Length);
                return;
            }
            uint n = 0;
            for (int i = 0; i < _K * _K; i++)
            {
                var child = node.GetChild(i);
                if (child != null)
                {
                    n += (uint)1 << (31 - i);
                    _FindPaths(child, ref dynT, ref dynLabels, level + 1);
                }
            }
            dynT[level].AddRange(n, _K * _K);
        }

        private INode[] _GetLabelFormLeafPosition(int position)
        {
            int rankInLeaves = _T.Rank1(position, _StartLeaves) - 1;
            ulong[] l = _Labels[(Predicates.Length * rankInLeaves)..(Predicates.Length * rankInLeaves + Predicates.Length)];
            var result = _GetPredicatesFromBitStream(l);
            return result.ToArray();
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
                        INode[] preds = _GetLabelFormLeafPosition(pos);
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

}