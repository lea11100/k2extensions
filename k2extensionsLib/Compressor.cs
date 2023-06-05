using AngleSharp.Common;
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
    public class K2ArrayIndex : IK2Extension
    {
        FastRankBitArray t { get; set; }
        FastRankBitArray labels { get; set; }
        int startLeaves { get; set; }
        int k { get; set; }
        private bool useK2Triples { get; set; }
        int size
        {
            get
            {
                return Math.Max(Subjects.Count(), Objects.Count());
            }
        }
        public INode[] Subjects { get; set; }
        public INode[] Objects { get; set; }
        public INode[] Predicates { get; set; }

        public K2ArrayIndex(int k)
        {
            this.k = k;
            useK2Triples = false;
            t = new FastRankBitArray();
            labels = new FastRankBitArray();
            Subjects = new INode[0];
            Predicates = new INode[0];
            Objects = new INode[0];
        }

        public void Compress(IGraph graph, bool useK2Triples)
        {
            DynamicBitArray labels = new DynamicBitArray();
            this.useK2Triples = useK2Triples;
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

            int size = Math.Max(Subjects.Count(), Objects.Count());
            int N = k;
            int h = 1;
            while (N < size)
            {
                N *= k;
                h++;

            }

            List<DynamicBitArray> levels = new List<DynamicBitArray>();
            List<List<bool>> levelsTest = new List<List<bool>>();
            for (int i = 0; i < h; i++)
            {
                levels.Add(new DynamicBitArray());
                levelsTest.Add(new List<bool>());
            }

            //compressRec(ref levels, ref labels, 0, graph, 0, 0, N, k);
            var root = buildK2Tree(graph, h);

            findPaths(root, ref levels, ref levelsTest, ref labels, 0);

            this.labels = new FastRankBitArray(labels);
            DynamicBitArray n = new DynamicBitArray();
            foreach (var l in levels.Take(levels.Count() - 1))
            {
                n.AddRange(l);
            }
            startLeaves = (n.data.Count - 1)*64 + n.firstFreeIndex;
            n.AddRange(levels.Last());
            t = new FastRankBitArray(n);
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            List<Triple> result = new List<Triple>();
            int positionInTypes = Array.IndexOf(Predicates.ToArray(), p);
            List<int> nodesWithType = new List<int>();
            int counter = positionInTypes;
            int index = 0;
            while (counter < labels.Length())
            {
                if (labels[counter]) nodesWithType.Add(index);
                counter += Predicates.Count();
                index++;
            }
            foreach (var n in nodesWithType)
            {
                int positionInNodes = t.Select1(t.Rank1(startLeaves) + n + 1);
                Tuple<int, int> cell = getCell(positionInNodes);
                var r = new Triple(Subjects.ElementAt(cell.Item1), Predicates.ElementAt(positionInTypes), Objects.ElementAt(cell.Item2));
                result.Add(r);
            }
            return result.ToArray();
        }

        public Triple[] Connections(INode s, INode o)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       where subj.i == obj.i
                                       select ((int?)subj.v, (int?)obj.v)).ToList();

            var result = findNodesRec(0, path, null, new List<(int, int)>());
            return result;
        }

        public Triple[] Decomp()
        {
            (int?, int?)[] path = new (int?, int?)[size.ToBase(k).Length];
            Array.Fill(path, (null, null));
            Triple[] result = findNodesRec(0, path.ToList(), null, new List<(int, int)>());
            return result;
        }

        public bool Exists(INode s, INode p, INode o)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       where subj.i == obj.i
                                       select ((int?)subj.v, (int?)obj.v)).ToList();

            var result = findNodesRec(0, path, p, new List<(int, int)>());
            return result.Any();
        }

        public Triple[] Prec(INode o)
        {
            List<(int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       select ((int?)null, (int?)obj.v)).ToList();
            Triple[] result = findNodesRec(0, path, null, new List<(int, int)>());
            return result;
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            List<(int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       select ((int?)null, (int?)obj.v)).ToList();
            Triple[] result = findNodesRec(0, path, p, new List<(int, int)>());
            return result;
        }

        public Triple[] Succ(INode s)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       select ((int?)subj.v, (int?)null)).ToList();

            Triple[] result = findNodesRec(0, path, null, new List<(int, int)>());
            return result;
        }

        public Triple[] SuccOfType(INode s, INode p)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       select ((int?)subj.v, (int?)null)).ToList();

            Triple[] result = findNodesRec(0, path, p, new List<(int, int)>());
            return result;
        }

        private TreeNode buildK2Tree(IGraph g, int h)
        {
            TreeNode root = new TreeNode(k * k);

            var paths = from t in g.Triples
                        group t by new
                        {
                            s = Array.IndexOf(Subjects.ToArray(), t.Subject),
                            o = Array.IndexOf(Objects.ToArray(), t.Object),
                        } into subjObjGroup
                        select (Enumerable.Zip(subjObjGroup.Key.s.ToBase(k, h), subjObjGroup.Key.o.ToBase(k, h)), subjObjGroup.Select(x => x.Predicate));

            foreach ((IEnumerable<(int, int)>, IEnumerable<INode>) path in paths)
            {
                TreeNode currentNode = root;
                TreeNode child = new TreeNode(k * k);
                foreach ((int, int) level in path.Item1) //Loop through path
                {
                    int quadrant = level.Item1 * k + level.Item2;
                    child = new TreeNode(k * k);
                    currentNode = currentNode.SetChild(quadrant, child);
                }
                ulong label = 0;
                List<bool> labelTest = new List<bool>();
                for (int l = 0; l < Predicates.Count(); l++)
                {
                    if (path.Item2.Contains(Predicates[l]))
                    {
                        labelTest.Add(true);
                        label += (ulong)1 << (63 - l);
                    }
                    else
                    {
                        labelTest.Add(false);
                    }
                }
                var s = string.Join("",labelTest.Select(x => x ? "1" : "0")).PadRight(64, '0');
                Assert.AreEqual(s, Convert.ToString((long)label, 2).PadLeft(64,'0'));
                currentNode.SetLabel(label);
            }

            return root;
        }

        private void findPaths(TreeNode node, ref List<DynamicBitArray> dynT, ref List<List<bool>> dynTTest, ref DynamicBitArray dynLabels, int level)
        {
            if (level == dynT.Count)
            {
                dynLabels.AddRange(node.GetLabel(), Predicates.Length);
                return;
            }
            uint n = 0;
            for (int i = 0; i < k * k; i++)
            {
                TreeNode child = node.GetChild(i);
                if (child != null)
                {
                    dynTTest[level].Add(true);
                    n += (uint)1 << (31 - i);
                    findPaths(child, ref dynT, ref dynTTest, ref dynLabels, level + 1);
                }
                else
                {
                    dynTTest[level].Add(false);
                }
            }
            dynT[level].AddRange(n, k * k);
            var s = string.Join("", dynTTest[level].Select(x => x ? "1" : "0"));
            Assert.AreEqual(s, dynT[level].GetAsString());
        }

        private INode[] getLabelFormLeafPosition(int position)
        {
            int rankInLeaves = t.Rank1(position, startLeaves) - 1;
            ulong[] l = labels[(Predicates.Count() * rankInLeaves)..(Predicates.Count() * rankInLeaves + Predicates.Count())];
            var result = getPredicatesFromBitStream(l);
            return result.ToArray();
        }

        private INode[] getPredicatesFromBitStream(ulong[] stream)
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
                    if (position >= Predicates.Count())
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
        private Tuple<int, int> getCell(int position)
        {
            int power = 1;
            int col = 0;
            int row = 0;
            while (position > 0)
            {
                int submatrixPos = position % (k*k);
                int submatrixColumn = submatrixPos % k;
                int submatrixRow = submatrixPos / k;
                col += submatrixColumn * power;
                row += submatrixRow * power;
                int numberOf1Bits = position / (k*k);
                position = t.Select1(numberOf1Bits);
                power *= k;
            }
            return new Tuple<int, int>(row, col);
        }

        private bool compressRec(ref List<DynamicBitArray> levels, ref DynamicBitArray labels, int level, IGraph graph, int row, int col, int N, int k)
        {
            while (levels.Count() <= level) levels.Add(new DynamicBitArray());
            uint submatrix = 0;
            if (N == k)
            {
                int index = 0;
                DynamicBitArray additionalLabels = new DynamicBitArray();
                for (int i = 0; i < k; i++)
                {
                    for (int j = 0; j < k; j++)
                    {
                        INode[] triples = new INode[0];
                        if (row + i < Subjects.Count() && col + j < Objects.Count())
                            triples = graph.GetTriplesWithSubjectObject(Subjects[row + i], Objects[col + j]).Select(x => x.Predicate).ToArray();

                        ulong label = 0;
                        bool containsLabel = false;
                        for (int l = 0; l < Predicates.Count(); l++)
                        {
                            if (triples.Contains(Predicates[l]))
                            {
                                label += (ulong)1 << (63 - l);
                                containsLabel = true;
                            }
                        }

                        if (containsLabel)
                        {
                            submatrix += (uint)1 << (31 - index);
                            additionalLabels.AddRange(label, Predicates.Count());
                        }
                        index++;
                    }
                }
                labels.AddRange(additionalLabels);
            }
            else
            {
                int NextN = N / k;
                int index = 0;
                for (int i = 0; i < k; i++)
                {
                    for (int j = 0; j < k; j++)
                    {
                        if (compressRec(ref levels, ref labels, level + 1, graph, row + i * NextN, col + j * NextN, NextN, k))
                            submatrix += (uint)1 << (31 - index);
                        index++;
                    }
                }
            }
            if (submatrix != 0)
            {
                levels[level].AddRange(submatrix, k * k);
                return true;
            }
            else
            {
                return false;
            }
        }

        private Triple[] findNodesRec(int positionInNodes, List<(int?, int?)> searchPath, INode? predicate, List<(int, int)> parentPath)
        {
            List<Triple> result = new List<Triple>();
            (int?, int?) position = searchPath[0];
            searchPath = searchPath.Skip(1).ToList();
            for (int s = position.Item1 ?? 0; s < (position.Item1 + 1 ?? k); s++)
            {
                for (int o = position.Item2 ?? 0; o < (position.Item2 + 1 ?? k); o++)
                {
                    int relativePosition = s * k + o;
                    int pos = positionInNodes + relativePosition;
                    List<(int, int)> parent = parentPath.Append((s, o)).ToList();
                    if (searchPath.Count() == 0 && t[pos])
                    {
                        //pos = t.Rank1(pos) * k * k;
                        int posS = parent.Select(x => x.Item1).FromBase(k);
                        int posO = parent.Select(x => x.Item2).FromBase(k);
                        INode[] preds = getLabelFormLeafPosition(pos);
                        if (predicate != null) preds = preds.Where(x => x.Equals(predicate)).ToArray();
                        if (preds.Length != 0) result.AddRange(preds.Select(x => new Triple(Subjects.ElementAt(posS), x, Objects.ElementAt(posO))));
                    }
                    else if (t[pos])
                    {
                        pos = t.Rank1(pos) * k * k;
                        result.AddRange(findNodesRec(pos, searchPath, predicate, parent));
                    }
                }
            }
            return result.ToArray();
        }

        public void Store(string filename)
        {
            //using (var s = File.Open(filename, FileMode.Create))
            //{
            //    using (var bw = new BinaryWriter(s,Encoding.UTF8))
            //    {
            //        bw.Write(startLeaves);
            //        bw.Write(startLeaves);
            //        bw.Write(nodes.GetDataAsString());
            //        bw.Write(labels.GetDataAsString());
            //        bw.Write(string.Join(" ", Predicates));
            //    }
            //}
            using (var sw = File.CreateText(filename))
            {
                sw.WriteLine(startLeaves);
                sw.WriteLine(t.GetDataAsString());
                sw.WriteLine(labels.GetDataAsString());
                sw.WriteLine(string.Join(" ", Predicates.ToList()));
                if (useK2Triples)
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
        }

        public void Load(string filename, bool useK2Triple)
        {
            using (var sr = new StreamReader(filename))
            {
                string line = sr.ReadLine() ?? "";
                NodeFactory nf = new NodeFactory(new NodeFactoryOptions());
                startLeaves = int.Parse(line);
                line = sr.ReadLine() ?? "";
                t.Store(line);
                line = sr.ReadLine() ?? "";
                labels.Store(line);
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
        }
    }

    public class K3 : IK2Extension
    {
        public INode[] Subjects { get; set; }
        public INode[] Objects { get; set; }
        public INode[] Predicates { get; set; }

        FastRankBitArray t { get; set; }
        bool useK2Triples { get; set; }
        int k;
        int size
        {
            get
            {
                return Math.Max(Subjects.Count(), Math.Max(Objects.Count(), Predicates.Count()));
            }
        }


        public K3(int k)
        {
            this.k = k;
            t = new FastRankBitArray();
            Subjects = new INode[0];
            Predicates = new INode[0];
            Objects = new INode[0];
            useK2Triples = false;
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            List<(int?, int?, int?)> path = Array.IndexOf(Predicates.ToArray(), p).ToBase(k, size.ToBase(k).Length)
                .Select<int, (int?, int?, int?)>(x => (null, x, null)).ToList();
            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public void Compress(IGraph graph, bool useK2Triples)
        {
            this.useK2Triples = useK2Triples;
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

            int size = Math.Max(Math.Max(Subjects.Count(), Objects.Count()), Predicates.Count());
            int N = k;
            int h = 1;
            while (N < size)
            {
                N *= k;
                h++;
            }

            var dict = graph.Triples.GroupBy(t => (t.Subject, t.Object), e => e.Predicate).ToDictionary(k => k.Key, n => n.ToArray());

            var root = buildK3(graph, h);
            List<DynamicBitArray> dynT = new List<DynamicBitArray>();
            List<List<bool>> dynTTest = new List<List<bool>>();
            for (int i = 0; i < h; i++)
            {
                dynT.Add(new DynamicBitArray());
                dynTTest.Add(new List<bool>());
            }
            findPaths(root, ref dynT, ref dynTTest, 0);

            DynamicBitArray n = new DynamicBitArray();
            foreach (var l in dynT)
            {
                n.AddRange(l);
            }
            t = new FastRankBitArray(n);
        }

        public Triple[] Connections(INode s, INode o)
        {
            List<(int?, int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             where subj.i == obj.i
                                             select ((int?)subj.v, (int?)null, (int?)obj.v)).ToList();


            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public Triple[] Decomp()
        {
            (int?, int?, int?)[] path = new (int?, int?, int?)[size.ToBase(k).Length];
            Array.Fill(path, (null, null, null));
            Triple[] result = findNodesRec(0, path.ToList(), new List<(int, int, int)>());
            return result;
        }

        public bool Exists(INode s, INode p, INode o)
        {
            List<(int?, int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             from pred in Array.IndexOf(Predicates.ToArray(), p).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             where subj.i == obj.i && obj.i == pred.i
                                             select ((int?)subj.v, (int?)pred.v, (int?)obj.v)).ToList();
            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result.Any();
        }

        public void Load(string filename, bool useK2Triple)
        {
            using (var sr = new StreamReader(filename))
            {
                string line = sr.ReadLine() ?? "";
                NodeFactory nf = new NodeFactory(new NodeFactoryOptions());
                t.Store(line);
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
        }

        public Triple[] Prec(INode o)
        {
            List<(int?, int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             select ((int?)null, (int?)null, (int?)obj.v)).ToList();


            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            List<(int?, int?, int?)> path = (from pred in Array.IndexOf(Predicates.ToArray(), p).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             where pred.i == obj.i
                                             select ((int?)null, (int?)pred.v, (int?)obj.v)).ToList();


            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public void Store(string filename)
        {
            using (var sw = File.CreateText(filename))
            {
                sw.WriteLine(t.GetDataAsString());
                sw.WriteLine(string.Join(" ", Predicates.ToList()));
                if (useK2Triples)
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
        }

        public Triple[] Succ(INode s)
        {
            List<(int?, int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             select ((int?)subj.v, (int?)null, (int?)null)).ToList();
            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public Triple[] SuccOfType(INode s, INode p)
        {

            List<(int?, int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             from pred in Array.IndexOf(Predicates.ToArray(), p).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                             where pred.i == subj.i
                                             select ((int?)subj.v, (int?)pred.v, (int?)null)).ToList();

            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        private TreeNode buildK3(IGraph g, int h)
        {
            TreeNode root = new TreeNode(k * k * k);

            var paths = from t in g.Triples
                        select Enumerable.Zip(Array.IndexOf(Subjects.ToArray(), t.Subject).ToBase(k, h), Array.IndexOf(Predicates.ToArray(), t.Predicate).ToBase(k, h), Array.IndexOf(Objects.ToArray(), t.Object).ToBase(k, h));

            foreach (IEnumerable<(int, int, int)> path in paths)
            {
                TreeNode currentNode = root;
                foreach ((int, int, int) p in path)
                {
                    int quadrant = p.Item1 * k * k + p.Item2 * k + p.Item3;
                    TreeNode child = new TreeNode(k * k * k);
                    currentNode = currentNode.SetChild(quadrant, child);
                }

            }

            return root;
        }

        private void findPaths(TreeNode node, ref List<DynamicBitArray> dynT, ref List<List<bool>> dynTTest, int level)
        {
            if (level == dynT.Count)
            {
                return;
            }
            uint n = 0;
            for (int i = 0; i < k * k * k; i++)
            {
                TreeNode child = node.GetChild(i);
                if (child != null)
                {
                    dynTTest[level].Add(true);
                    n += (uint)1 << (31 - i);
                    findPaths(child, ref dynT, ref dynTTest, level + 1);
                }
                else
                {
                    dynTTest[level].Add(false);
                }
            }
            dynT[level].AddRange(n, k * k * k);
            var s = string.Join("", dynTTest[level].Select(x => x ? "1" : "0"));
            Assert.AreEqual(s, dynT[level].GetAsString());
        }

        private Triple[] findNodesRec(int positionInNodes, List<(int?, int?, int?)> searchPath, List<(int, int, int)> parentPath)
        {
            List<Triple> result = new List<Triple>();
            (int?, int?, int?) position = searchPath[0];
            searchPath = searchPath.Skip(1).ToList();
            for (int s = position.Item1 ?? 0; s < (position.Item1 + 1 ?? k); s++)
            {
                for (int p = position.Item2 ?? 0; p < (position.Item2 + 1 ?? k); p++)
                {
                    for (int o = position.Item3 ?? 0; o < (position.Item3 + 1 ?? k); o++)
                    {
                        int relativePosition = s * k * k + p * k + o;
                        int pos = positionInNodes + relativePosition;
                        List<(int, int, int)> parent = parentPath.Append((s, p, o)).ToList();
                        if (searchPath.Count() == 0 && t[pos])
                        {
                            int posS = parent.Select(x => x.Item1).FromBase(k);
                            int posP = parent.Select(x => x.Item2).FromBase(k);
                            int posO = parent.Select(x => x.Item3).FromBase(k);
                            result.Add(new Triple(Subjects.ElementAt(posS), Predicates.ElementAt(posP), Objects.ElementAt(posO)));
                        }
                        else if (t[pos])
                        {
                            pos = t.Rank1(pos) * k * k * k;
                            result.AddRange(findNodesRec(pos, searchPath, parent));
                        }
                    }
                }
            }
            return result.ToArray();
        }
    }

    public class MK2 : IK2Extension
    {
        public INode[] Subjects { get; set; }
        public INode[] Objects { get; set; }
        public INode[] Predicates { get; set; }

        int size
        {
            get
            {
                return Math.Max(Subjects.Count(), Objects.Count());
            }
        }
        Dictionary<INode, FlatPopcount> t { get; set; }
        Dictionary<INode, FastRankBitArray> t_Test { get; set; }
        bool useK2Triples { get; set; }
        int k { get; set; }

        public MK2(int k)
        {
            this.k = k;
            t = new Dictionary<INode, FlatPopcount>();
            t_Test = new Dictionary<INode, FastRankBitArray>();
            Subjects = new INode[0];
            Predicates = new INode[0];
            Objects = new INode[0];
            useK2Triples = false;
        }

        public void Compress(IGraph graph, bool useK2Triples)
        {
            DynamicBitArray[] dynT = new DynamicBitArray[0];
            this.useK2Triples = useK2Triples;
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

            int size = Math.Max(Subjects.Count(), Objects.Count());
            int N = k;
            int h = 1;
            while (N < size)
            {
                N *= k;
                h++;
            }

            foreach (var pred in Predicates)
            {
                List<DynamicBitArray> dynTForPred = new List<DynamicBitArray>();
                List<List<bool>> dynTForPredTest = new List<List<bool>>();
                for (int i = 0; i < h; i++)
                {
                    dynTForPred.Add(new DynamicBitArray());
                    dynTForPredTest.Add(new List<bool>());
                }

                var root = buildK2Tree(graph, pred, h);
                findPaths(root, ref dynTForPred, ref dynTForPredTest, 0);

                for (int i = 0; i < h; i++)
                {
                    var s = string.Join("", dynTForPredTest[i].Select(x => x ? "1" : "0"));
                    Assert.AreEqual(s, dynTForPred[i].GetAsString());
                }
                DynamicBitArray flatT = new DynamicBitArray();
                List<bool> flatTTest = new List<bool>();
                for (int i = 0; i < dynTForPred.Count(); i++)
                {
                    flatT.AddRange(dynTForPred[i]);
                    flatTTest.AddRange(dynTForPredTest[i]);
                    var l = string.Join("", flatTTest.Select(y => y ? "1" : "0"));
                    Assert.AreEqual(l, flatT.GetAsString());
                }

                var f = string.Join("", dynTForPredTest.SelectMany(x => x.Select(y => y ? "1" : "0")));

                var frba = new FlatPopcount(flatT);
                var frba_test = new FastRankBitArray(flatT);

                Assert.AreEqual(f.TrimEnd('0'), frba.GetAsBitstream().TrimEnd('0'));
                t.Add(pred, frba);
                t_Test.Add(pred, frba_test);
            }
        }
        private TreeNode buildK2Tree(IGraph g, INode pred, int h)
        {
            TreeNode root = new TreeNode(k * k);

            var paths = from t in g.GetTriplesWithPredicate(pred)
                        select Enumerable.Zip(Array.IndexOf(Subjects.ToArray(), t.Subject).ToBase(k, h), Array.IndexOf(Objects.ToArray(), t.Object).ToBase(k, h));

            foreach (IEnumerable<(int, int)> path in paths)
            {
                TreeNode currentNode = root;
                foreach ((int, int) p in path)
                {
                    int quadrant = p.Item1 * k + p.Item2;
                    TreeNode child = new TreeNode(k * k);
                    currentNode = currentNode.SetChild(quadrant, child);
                }

            }

            return root;
        }

        private void findPaths(TreeNode node, ref List<DynamicBitArray> dynT, ref List<List<bool>> dynTTest, int level)
        {
            if (level == dynT.Count)
            {
                return;
            }
            uint n = 0;
            for (int i = 0; i < k * k; i++)
            {
                TreeNode child = node.GetChild(i);
                if (child != null)
                {
                    dynTTest[level].Add(true);
                    n += (uint)1 << (31 - i);
                    findPaths(child, ref dynT, ref dynTTest, level + 1);
                }
                else
                {
                    dynTTest[level].Add(false);
                }
            }
            dynT[level].AddRange(n, k * k);
            var s = string.Join("", dynTTest[level].Select(x => x ? "1" : "0"));
            Assert.AreEqual(s, dynT[level].GetAsString());
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            (int?, int?)[] path = new (int?, int?)[size.ToBase(k).Length];
            Array.Fill(path, (null, null));
            Triple[] result = findNodesRec(p, 0, path.ToList(), new List<(int, int)>());
            return result;
        }

        public Triple[] Connections(INode s, INode o)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       where subj.i == obj.i
                                       select ((int?)subj.v, (int?)obj.v)).ToList();
            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(findNodesRec(p, 0, path, new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public Triple[] Decomp()
        {
            (int?, int?)[] path = new (int?, int?)[size.ToBase(k).Length];
            Array.Fill(path, (null, null));
            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(findNodesRec(p, 0, path.ToList(), new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public bool Exists(INode s, INode p, INode o)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       where subj.i == obj.i
                                       select ((int?)subj.v, (int?)obj.v)).ToList();
            Triple[] result = findNodesRec(p, 0, path, new List<(int, int)>());
            return result.Any();
        }

        public Triple[] Prec(INode o)
        {
            List<(int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       select ((int?)null, (int?)obj.v)).ToList();
            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(findNodesRec(p, 0, path, new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            List<(int?, int?)> path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       select ((int?)null, (int?)obj.v)).ToList();
            Triple[] result = findNodesRec(p, 0, path, new List<(int, int)>());
            return result;
        }

        public Triple[] Succ(INode s)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       select ((int?)subj.v, (int?)null)).ToList();

            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(findNodesRec(p, 0, path, new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public Triple[] SuccOfType(INode s, INode p)
        {
            List<(int?, int?)> path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                       select ((int?)subj.v, (int?)null)).ToList();

            Triple[] result = findNodesRec(p, 0, path, new List<(int, int)>());
            return result;
        }

        public void Load(string filename, bool useK2Triple)
        {
            using (var sr = new StreamReader(filename))
            {
                string line = sr.ReadLine() ?? "";
                NodeFactory nf = new NodeFactory(new NodeFactoryOptions());
                List<FlatPopcount> l = new List<FlatPopcount>();
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
                    t.Add(pred, l[index]);
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
        }

        public void Store(string filename)
        {
            using (var sw = File.CreateText(filename))
            {
                foreach (var (k, v) in t)
                {
                    sw.WriteLine(v.GetDataAsString());
                }
                sw.WriteLine("Tree stop");
                sw.WriteLine(string.Join(" ", Predicates.ToList()));
                if (useK2Triples)
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
        }

        private Triple[] findNodesRec(INode predicate, int positionInNodes, List<(int?, int?)> searchPath, List<(int, int)> parentPath)
        {
            List<Triple> result = new List<Triple>();
            (int?, int?) position = searchPath[0];
            searchPath = searchPath.Skip(1).ToList();
            for (int s = position.Item1 ?? 0; s < (position.Item1 + 1 ?? k); s++)
            {
                for (int o = position.Item2 ?? 0; o < (position.Item2 + 1 ?? k); o++)
                {
                    int relativePosition = s * k + o;
                    int pos = positionInNodes + relativePosition;
                    List<(int, int)> parent = parentPath.Append((s, o)).ToList();
                    Assert.AreEqual(t_Test[predicate][pos], t[predicate][pos]);
                    if (searchPath.Count() == 0 && t[predicate][pos])
                    {
                        int posS = parent.Select(x => x.Item1).FromBase(k);
                        int posO = parent.Select(x => x.Item2).FromBase(k);
                        result.Add(new Triple(Subjects.ElementAt(posS), predicate, Objects.ElementAt(posO)));
                    }
                    else if (t[predicate][pos])
                    {
                        var x = t[predicate].Rank1(pos);
                        var y = t_Test[predicate].Rank1(pos);
                        if(x != y)
                        {

                        }
                        //Assert.AreEqual(y,x );

                        pos = t[predicate].Rank1(pos) * k * k;

                        result.AddRange(findNodesRec(predicate, pos, searchPath, parent));

                    }
                }
            }
            return result.ToArray();
        }
    }
}
