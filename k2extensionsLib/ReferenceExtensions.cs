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
using System.Numerics;
using System;

namespace k2extensionsLib
{
    /// <summary>
    /// Representation of a k^3 tree
    /// </summary>
    public class K3 : IK2Extension
    {
        /// <summary>
        /// Data
        /// </summary>
        FlatPopcount _T { get; set; }
        /// <summary>
        /// Used k
        /// </summary>
        readonly int _K;
        /// <summary>
        /// Size of the matrix
        /// </summary>
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

        public void Compress(TripleStore graph, bool useK2Triples)
        {
            Subjects = graph.Triples.Select(x => x.Subject).Distinct().ToArray();
            Objects = graph.Triples.Select(x => x.Object).Distinct().ToArray();
            if (useK2Triples)
            {
                var so = Subjects.Intersect(Objects);
                Subjects = so.Concat(Subjects.Except(so)).ToArray();
                Objects = so.Concat(Objects.Except(so)).ToArray();
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
            List<(int?,int?, int?)> path = Enumerable.Zip(
                Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>(), 
                Enumerable.Repeat((int?)null, _Size.ToBase(_K).Length), 
                Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>()).ToList();

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

        public Triple[] Exists(INode s, INode p, INode o)
        {
            List<(int?, int?, int?)> path = Enumerable.Zip(
                Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>(),
                Array.IndexOf(Predicates.ToArray(), p).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>(), 
                Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>())
                .ToList();

            Triple[] result = _FindNodesRec(0, path, new List<(int, int, int)>());
            return result;
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
            List<(int?, int?, int?)> path = Enumerable.Zip(
                Enumerable.Repeat((int?)null, _Size.ToBase(_K).Length),
                Array.IndexOf(Predicates.ToArray(), p).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>(),
                Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>())
                .ToList();


            Triple[] result = _FindNodesRec(0, path, new List<(int, int, int)>());
            return result;
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

            List<(int?, int?, int?)> path = Enumerable.Zip(
                Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>(),
                Array.IndexOf(Predicates.ToArray(), p).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>(),
                Enumerable.Repeat((int?)null, _Size.ToBase(_K).Length))
                .ToList();

            Triple[] result = _FindNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        /// <summary>
        /// Builds the pointer-based k^3 tree
        /// </summary>
        /// <param name="g">Graph containing the data</param>
        /// <param name="h">height of the tree</param>
        /// <returns>Root of the tree</returns>
        private TreeNode _BuildK3(TripleStore g, int h)
        {
            var root = new TreeNode(_K * _K * _K);

            var subs = Subjects.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
            var preds = Predicates.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
            var objs = Objects.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);

            var paths = from t in g.Triples
                        select Enumerable.Zip(subs[t.Subject].ToBase(_K, h), preds[t.Predicate].ToBase(_K, h), objs[t.Object].ToBase(_K, h));

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

        /// <summary>
        /// Calculate the triples of the given search path
        /// </summary>
        /// <param name="positionInNodes">Current position in <see cref="T"/></param>
        /// <param name="searchPath">Search path for traversing the tree</param>
        /// <param name="parentPath">Path to the current position"/></param>
        /// <returns>Found triples</returns>
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
        /// Data in form of a dictionary storing a k^2 tree foreach predicate
        /// </summary>
        Dictionary<INode, K2Tree> _T { get; set; }
        /// <summary>
        /// Used k
        /// </summary>
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
                    result += t.Value.T.Length() / 8;
                }
                return result;
            }
        }

        public MK2(int k)
        {
            this._K = k;
            _T = new Dictionary<INode, K2Tree>();
            Subjects = Array.Empty<INode>();
            Predicates = Array.Empty<INode>();
            Objects = Array.Empty<INode>();
        }

        public void Compress(TripleStore graph, bool useK2Triples)
        {
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
            var subs = Subjects.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i);
            var objs = Objects.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i);
            foreach (var pred in Predicates)
            {
                var treeForPred = new K2Tree(_K, Subjects.Length, Objects.Length);
                var cells = from triple in graph.Triples.WithPredicate(pred)
                            select (subs[triple.Subject], objs[triple.Object]);

                treeForPred.Store(cells);
                _T.Add(pred, treeForPred);
            }
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            (int?, int?)[] path = new (int?, int?)[_Size.ToBase(_K).Length];
            Array.Fill(path, (null, null));
            Triple[] result = _FindNodes(p, path.ToList());
            return result;
        }

        public Triple[] Connections(INode s, INode o)
        {
            List<(int?, int?)> path = Enumerable.Zip(Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>(), Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>()).ToList();
            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(_FindNodes(p, path));
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
                result.AddRange(_FindNodes(p, path.ToList()));
            }
            return result.ToArray();
        }

        public Triple[] Exists(INode s, INode p, INode o)
        {
            List<(int?, int?)> path = Enumerable.Zip(Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>(), Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Cast<int?>()).ToList();
            Triple[] result = _FindNodes(p, path);
            return result;
        }

        public Triple[] Prec(INode o)
        {
            List<(int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)null, (int?)obj.v)).ToList();
            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(_FindNodes(p, path));
            }
            return result.ToArray();
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            List<(int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)null, (int?)obj.v)).ToList();
            Triple[] result = _FindNodes(p, path);
            return result;
        }

        public Triple[] Succ(INode s)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)subj.v, (int?)null)).ToList();

            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(_FindNodes(p, path));
            }
            return result.ToArray();
        }

        public Triple[] SuccOfType(INode s, INode p)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(_K, _Size.ToBase(_K).Length).Select((v, i) => (v, i))
                                       select ((int?)subj.v, (int?)null)).ToList();

            Triple[] result = _FindNodes(p, path);
            return result;
        }

        /// <summary>
        /// Calculate the triples of the given search path
        /// </summary>
        /// <param name="predicate">Predicate to identify the used k^2 tree</param>
        /// <param name="searchPath">Search path for traversing the tree</param>
        /// <returns>Found triples</returns>
        private Triple[] _FindNodes(INode predicate, List<(int?, int?)> searchPath)
        {
            var cells = _T[predicate].FindNodes(0, searchPath, new List<(int, int)>());
            var result = cells.Select(x => new Triple(Subjects.ElementAt(x.Item1), predicate, Objects.ElementAt(x.Item2)));
            return result.ToArray();
        }
    }
}
