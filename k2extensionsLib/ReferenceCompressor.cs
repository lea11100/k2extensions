﻿using AngleSharp.Common;
using System.Collections;
using System.Drawing.Drawing2D;
using System.Drawing;
using VDS.RDF;
using VDS.RDF.Query;
using VDS.RDF.Query.FullText.Indexing.Lucene;
using NUnit.Framework;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace k2extensionsLib
{
    public class K3 : IK2Extension
    {
        FlatPopcount _T { get; set; }
        bool _UseK2Triples { get; set; }
        readonly int _K;
        int _Size
        {
            get
            {
                return Math.Max(Subjects.Length, Math.Max(Objects.Length, Predicates.Length));
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
                return result;
            }
        }

       
        public K3(int k)
        {
            _K = k;
            _T = new FlatPopcount();
            _UseK2Triples = false;
            Subjects = Array.Empty<INode>();
            Predicates = Array.Empty<INode>();
            Objects = Array.Empty<INode>();
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            List<(int?, int?, int?)> path = Array.IndexOf(Predicates.ToArray(), p).ToBase(_K, _Size.ToBase(_K).Length)
                .Select<int, (int?, int?, int?)>(x => (null, x, null)).ToList();
            Triple[] result = _FindNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public void Compress(IGraph graph, bool useK2Triples)
        {
            this._UseK2Triples = useK2Triples;
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

            int size = Math.Max(Math.Max(Subjects.Length, Objects.Length), Predicates.Length);
            int N = _K;
            int h = 1;
            while (N < size)
            {
                N *= _K;
                h++;
            }

            var dict = graph.Triples.GroupBy(t => (t.Subject, t.Object), e => e.Predicate).ToDictionary(k => k.Key, n => n.ToArray());

            var root = _BuildK3(graph, h);
            var dynT = new List<DynamicBitArray>();
            for (int i = 0; i < h; i++)
            {
                dynT.Add(new DynamicBitArray());
            }
            _FindPaths(root, ref dynT, 0);

            var n = new DynamicBitArray();
            foreach (var l in dynT)
            {
                n.AddRange(l);
            }
            _T = new FlatPopcount(n);
        }

        public Triple[] Connections(INode s, INode o)
        {
            List<(int?, int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             where subj.i == obj.i
                                             select ((int?)subj.v, (int?)null, (int?)obj.v)).ToList();


            Triple[] result = _FindNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public Triple[] Decomp()
        {
            (int?, int?, int?)[] path = new (int?, int?, int?)[_Size.ToBase(_K).Length];
            Array.Fill(path, (null, null, null));
            Triple[] result = _FindNodesRec(0, path.ToList(), new List<(int, int, int)>());
            return result;
        }

        public bool Exists(INode s, INode p, INode o)
        {
            List<(int?, int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             from pred in Array.IndexOf(Predicates.ToArray(), p).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             where subj.i == obj.i && obj.i == pred.i
                                             select ((int?)subj.v, (int?)pred.v, (int?)obj.v)).ToList();
            Triple[] result = _FindNodesRec(0, path, new List<(int, int, int)>());
            return result.Any();
        }

        public void Load(string filename, bool useK2Triple)
        {
            using var sr = new StreamReader(filename);
            string line = sr.ReadLine() ?? "";
            var nf = new NodeFactory(new NodeFactoryOptions());
            _T.Store(line);
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

        public Triple[] Prec(INode o)
        {
            List<(int?, int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             select ((int?)null, (int?)null, (int?)obj.v)).ToList();


            Triple[] result = _FindNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            List<(int?, int?, int?)> path = (from pred in Array.IndexOf(Predicates.ToArray(), p).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             where pred.i == obj.i
                                             select ((int?)null, (int?)pred.v, (int?)obj.v)).ToList();


            Triple[] result = _FindNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public void Store(string filename)
        {
            using var sw = File.CreateText(filename);
            sw.WriteLine(_T.GetDataAsString());
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

        public Triple[] Succ(INode s)
        {
            List<(int?, int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             select ((int?)subj.v, (int?)null, (int?)null)).ToList();
            Triple[] result = _FindNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public Triple[] SuccOfType(INode s, INode p)
        {

            List<(int?, int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             from pred in Array.IndexOf(Predicates.ToArray(), p).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                             where pred.i == subj.i
                                             select ((int?)subj.v, (int?)pred.v, (int?)null)).ToList();

            Triple[] result = _FindNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        private TreeNode _BuildK3(IGraph g, int h)
        {
            var root = new TreeNode(_K * _K * _K);

            var paths = from t in g.Triples
                        select Enumerable.Zip(Array.IndexOf(Subjects.ToArray(), t.Subject).ToBase(_K, h), Array.IndexOf(Predicates.ToArray(), t.Predicate).ToBase(_K, h), Array.IndexOf(Objects.ToArray(), t.Object).ToBase(_K, h));

            foreach (IEnumerable<(int, int, int)> path in paths)
            {
                var currentNode = root;
                foreach ((int, int, int) p in path)
                {
                    int quadrant = p.Item1 * _K * _K + p.Item2 * _K + p.Item3;
                    var child = new TreeNode(_K * _K * _K);
                    currentNode = currentNode.SetChild(quadrant, child);
                }

            }

            return root;
        }

        private void _FindPaths(TreeNode node, ref List<DynamicBitArray> dynT, int level)
        {
            if (level == dynT.Count)
            {
                return;
            }
            uint n = 0;
            for (int i = 0; i < _K * _K * _K; i++)
            {
                var child = node.GetChild(i);
                if (child != null)
                {
                    n += (uint)1 << (31 - i);
                    _FindPaths(child, ref dynT, level + 1);
                }
            }
            dynT[level].AddRange(n, _K * _K * _K);
        }

        private Triple[] _FindNodesRec(int positionInNodes, List<(int?, int?, int?)> searchPath, List<(int, int, int)> parentPath)
        {
            var result = new List<Triple>();
            (int?, int?, int?) position = searchPath[0];
            searchPath = searchPath.Skip(1).ToList();
            for (int s = position.Item1 ?? 0; s < (position.Item1 + 1 ?? _K); s++)
            {
                for (int p = position.Item2 ?? 0; p < (position.Item2 + 1 ?? _K); p++)
                {
                    for (int o = position.Item3 ?? 0; o < (position.Item3 + 1 ?? _K); o++)
                    {
                        int relativePosition = s * _K * _K + p * _K + o;
                        int pos = positionInNodes + relativePosition;
                        List<(int, int, int)> parent = parentPath.Append((s, p, o)).ToList();
                        if (searchPath.Count == 0 && _T[pos])
                        {
                            int posS = parent.Select(x => x.Item1).FromBase(_K);
                            int posP = parent.Select(x => x.Item2).FromBase(_K);
                            int posO = parent.Select(x => x.Item3).FromBase(_K);
                            result.Add(new Triple(Subjects.ElementAt(posS), Predicates.ElementAt(posP), Objects.ElementAt(posO)));
                        }
                        else if (_T[pos])
                        {
                            pos = _T.Rank1(pos) * _K * _K * _K;
                            result.AddRange(_FindNodesRec(pos, searchPath, parent));
                        }
                    }
                }
            }
            return result.ToArray();
        }
    }

    public class MK2 : IK2Extension
    {
        int _Size
        {
            get
            {
                return Math.Max(Subjects.Length, Objects.Length);
            }
        }
        Dictionary<INode, FlatPopcount> _T { get; set; }
        bool _UseK2Triples { get; set; }
        int _K { get; set; }

        public INode[] Subjects { get; set; }
        public INode[] Objects { get; set; }
        public INode[] Predicates { get; set; }
        public int StorageSpace
        {
            get
            {
                int result = 0;
                foreach (var t in _T)
                {
                    result += t.Value.Length() / 8;
                }
                return result;
            }
        }

        public MK2(int k)
        {
            this._K = k;
            _T = new Dictionary<INode, FlatPopcount>();
            Subjects = Array.Empty<INode>();
            Predicates = Array.Empty<INode>();
            Objects = Array.Empty<INode>();
            _UseK2Triples = false;
        }

        public void Compress(IGraph graph, bool useK2Triples)
        {
            DynamicBitArray[] dynT = Array.Empty<DynamicBitArray>();
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

            foreach (var pred in Predicates)
            {
                List<DynamicBitArray> dynTForPred = new();
                for (int i = 0; i < h; i++)
                {
                    dynTForPred.Add(new DynamicBitArray());
                }

                var root = _BuildK2Tree(graph, pred, h);
                _FindPaths(root, ref dynTForPred, 0);

                var flatT = new DynamicBitArray();
                for (int i = 0; i < dynTForPred.Count; i++)
                {
                    flatT.AddRange(dynTForPred[i]);
                }

                var frba = new FlatPopcount(flatT);
                _T.Add(pred, frba);
            }
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            (int?, int?)[] path = new (int?, int?)[_Size.ToBase(_K).Length];
            Array.Fill(path, (null, null));
            Triple[] result = _FindNodesRec(p, 0, path.ToList(), new List<(int, int)>());
            return result;
        }

        public Triple[] Connections(INode s, INode o)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       where subj.i == obj.i
                                       select ((int?)subj.v, (int?)obj.v)).ToList();
            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(_FindNodesRec(p, 0, path, new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public Triple[] Decomp()
        {
            (int?, int?)[] path = new (int?, int?)[_Size.ToBase(_K).Length];
            Array.Fill(path, (null, null));
            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(_FindNodesRec(p, 0, path.ToList(), new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public bool Exists(INode s, INode p, INode o)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       where subj.i == obj.i
                                       select ((int?)subj.v, (int?)obj.v)).ToList();
            Triple[] result = _FindNodesRec(p, 0, path, new List<(int, int)>());
            return result.Any();
        }

        public Triple[] Prec(INode o)
        {
            List<(int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)null, (int?)obj.v)).ToList();
            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(_FindNodesRec(p, 0, path, new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            List<(int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)null, (int?)obj.v)).ToList();
            Triple[] result = _FindNodesRec(p, 0, path, new List<(int, int)>());
            return result;
        }

        public Triple[] Succ(INode s)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)subj.v, (int?)null)).ToList();

            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(_FindNodesRec(p, 0, path, new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public Triple[] SuccOfType(INode s, INode p)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)subj.v, (int?)null)).ToList();

            Triple[] result = _FindNodesRec(p, 0, path, new List<(int, int)>());
            return result;
        }

        public void Load(string filename, bool useK2Triple)
        {
            using var sr = new StreamReader(filename);
            string line = sr.ReadLine() ?? "";
            var nf = new NodeFactory(new NodeFactoryOptions());
            var l = new List<FlatPopcount>();
            while (line != "Tree stop")
            {
                l.Add(new FlatPopcount());
                l[^1].Store(line);
                line = sr.ReadLine() ?? "";
            }
            line = sr.ReadLine() ?? "";
            Predicates = line.Split(" ").Select(x => nf.CreateLiteralNode(x)).ToArray();
            foreach (var (pred, index) in Predicates.Select((v, i) => (v, i)))
            {
                _T.Add(pred, l[index]);
            }
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

        public void Store(string filename)
        {
            using var sw = File.CreateText(filename);
            foreach (var (k, v) in _T)
            {
                sw.WriteLine(v.GetDataAsString());
            }
            sw.WriteLine("Tree stop");
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

        private TreeNode _BuildK2Tree(IGraph g, INode pred, int h)
        {
            var root = new TreeNode(_K * _K);

            var paths = from t in g.GetTriplesWithPredicate(pred)
                        select Enumerable.Zip(Array.IndexOf(Subjects.ToArray(), t.Subject).ToBase(_K, h), Array.IndexOf(Objects.ToArray(), t.Object).ToBase(_K, h));

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

            return root;
        }

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

        private Triple[] _FindNodesRec(INode predicate, int positionInNodes, List<(int?, int?)> searchPath, List<(int, int)> parentPath)
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
                    if (searchPath.Count == 0 && _T[predicate][pos])
                    {
                        int posS = parent.Select(x => x.Item1).FromBase(_K);
                        int posO = parent.Select(x => x.Item2).FromBase(_K);
                        result.Add(new Triple(Subjects.ElementAt(posS), predicate, Objects.ElementAt(posO)));
                    }
                    else if (_T[predicate][pos])
                    {
                        pos = _T[predicate].Rank1(pos) * _K * _K;
                        result.AddRange(_FindNodesRec(predicate, pos, searchPath, parent));
                    }
                }
            }
            return result.ToArray();
        }
    }
}
