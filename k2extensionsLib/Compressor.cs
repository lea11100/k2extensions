using AngleSharp.Common;
using J2N.Numerics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;
using VDS.RDF.Shacl.Validation;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        public IEnumerable<INode> Subjects { get; set; }
        public IEnumerable<INode> Objects { get; set; }
        public IEnumerable<INode> Predicates { get; set; }

        public K2ArrayIndex(int k)
        {
            this.k = k;
            useK2Triples = false;
            t = new FastRankBitArray();
            labels = new FastRankBitArray();
            Subjects = new List<INode>();
            Predicates = new List<INode>();
            Objects = new List<INode>();
        }

        public void Compress(IGraph graph, bool useK2Triples)
        {
            DynamicBitArray[] levels = new DynamicBitArray[0];
            DynamicBitArray labels = new DynamicBitArray();
            this.useK2Triples = useK2Triples;
            Subjects = graph.Triples.Select(x => x.Subject).Distinct();
            Objects = graph.Triples.Select(x => x.Object).Distinct();
            if (useK2Triples)
            {
                var so = Subjects.Intersect(Objects);
                Subjects = so.Concat(Subjects.Where(x => !so.Contains(x))).ToList();
                Objects = so.Concat(Objects.Where(x => !so.Contains(x))).ToList();
            }
            else
            {
                Subjects = Subjects.Concat(Objects).Distinct();
                Objects = Subjects;
            }
            Predicates = graph.Triples.Select(x => x.Predicate).Distinct();

            int size = Math.Max(Subjects.Count(), Objects.Count());
            int N = k;
            while (N < size) N *= k;

            compressRec(ref levels, ref labels, 0, graph, 0, 0, N, k);

            this.labels = new FastRankBitArray(labels.GetFittedArray());
            DynamicBitArray n = new DynamicBitArray();
            foreach (var l in levels.Take(levels.Length - 1))
            {
                n.AddRange(l.GetFittedArray());
            }
            startLeaves = n.GetFittedArray().Length;
            n.AddRange(levels.Last().GetFittedArray());
            t = new FastRankBitArray(n.GetFittedArray());
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
            (int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   where subj.i == obj.i
                                   select ((int?)subj.v, (int?)obj.v)).ToArray();

            var result = findNodesRec(0, path, null, new List<(int, int)>());
            return result;
        }

        public Triple[] Decomp()
        {
            (int?, int?)[] path = new (int?, int?)[size.ToBase(k).Length];
            Array.Fill(path, (null, null));
            Triple[] result = findNodesRec(0, path, null, new List<(int, int)>());
            return result;
        }

        public bool Exists(INode s, INode p, INode o)
        {
            (int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   where subj.i == obj.i
                                   select ((int?)subj.v, (int?)obj.v)).ToArray();

            var result = findNodesRec(0, path, p, new List<(int, int)>());
            return result.Any();
        }

        public Triple[] Prec(INode o)
        {
            (int?, int?)[] path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   select ((int?)null, (int?)obj.v)).ToArray();
            Triple[] result = findNodesRec(0, path, null, new List<(int, int)>());
            return result;
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            (int?, int?)[] path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   select ((int?)null, (int?)obj.v)).ToArray();
            Triple[] result = findNodesRec(0, path, p, new List<(int, int)>());
            return result;
        }

        public Triple[] Succ(INode s)
        {
            (int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   select ((int?)subj.v, (int?)null)).ToArray();

            Triple[] result = findNodesRec(0, path, null, new List<(int, int)>());
            return result;
        }

        public Triple[] SuccOfType(INode s, INode p)
        {
            (int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   select ((int?)subj.v, (int?)null)).ToArray();

            Triple[] result = findNodesRec(0, path, p, new List<(int, int)>());
            return result;
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
                int submatrixPos = position % (int)Math.Pow(k, 2);
                int submatrixColumn = submatrixPos % k;
                int submatrixRow = submatrixPos / k;
                col += submatrixColumn * power;
                row += submatrixRow * power;
                int numberOf1Bits = position / (int)Math.Pow(k, 2);
                position = t.Select1(numberOf1Bits);
                power *= k;
            }
            return new Tuple<int, int>(row, col);
        }

        private bool compressRec(ref DynamicBitArray[] levels, ref DynamicBitArray labels, int level, IGraph graph, int row, int col, int N, int k)
        {
            while (levels.Length <= level) levels = levels.Append(new DynamicBitArray()).ToArray();
            var submatrix = new BitArray((int)Math.Pow(k, 2));
            if (N == k)
            {
                int index = 0;
                DynamicBitArray additionalLabels = new DynamicBitArray();
                for (int i = 0; i < k; i++)
                {
                    for (int j = 0; j < k; j++)
                    {
                        IEnumerable<INode> triples = new List<INode>();
                        if (row + i < Subjects.Count() && col + j < Objects.Count())
                            triples = graph.GetTriplesWithSubjectObject(Subjects.ElementAt(row + i), Objects.ElementAt(col + j)).Select(x => x.Predicate);

                        BitArray label = new BitArray(Predicates.Count(), false);
                        for (int l = 0; l < Predicates.Count(); l++)
                        {
                            if (triples.Contains(Predicates.ElementAt(l))) label[l] = true;
                        }

                        if (label.Cast<bool>().Any(x => x == true))
                        {
                            submatrix[index] = true;
                            additionalLabels.AddRange(label);
                        }
                        else
                        {
                            submatrix[index] = false;
                        }
                        index++;
                    }
                }
                labels.AddRange(additionalLabels.GetFittedArray());
            }
            else
            {
                int NextN = N / k;
                int index = 0;
                for (int i = 0; i < k; i++)
                {
                    for (int j = 0; j < k; j++)
                    {
                        submatrix[index] = compressRec(ref levels, ref labels, level + 1, graph, row + i * NextN, col + j * NextN, NextN, k);
                        index++;
                    }
                }
            }
            if (submatrix.Cast<bool>().Any(x => x == true))
            {
                levels[level].AddRange(submatrix);
                return true;
            }
            else
            {
                return false;
            }
        }

        private Triple[] findNodesRec(int positionInNodes, (int?, int?)[] searchPath, INode? predicate, List<(int, int)> parentPath)
        {
            List<Triple> result = new List<Triple>();
            (int?, int?) position = searchPath[0];
            searchPath = searchPath.Skip(1).ToArray();
            for (int s = position.Item1 ?? 0; s < (position.Item1 + 1 ?? k); s++)
            {
                for (int o = position.Item2 ?? 0; o < (position.Item2 + 1 ?? k); o++)
                {
                    int relativePosition = s * k + o;
                    int pos = positionInNodes + relativePosition;
                    List<(int, int)> parent = parentPath.Append((s, o)).ToList();
                    if (searchPath.Length == 0 && t[pos])
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
                sw.WriteLine(string.Join(" ", Predicates));
                if (useK2Triples)
                {
                    var so = Subjects.Intersect(Objects);
                    sw.WriteLine(string.Join(" ", so));
                    sw.WriteLine(string.Join(" ", Subjects.Where(x => !so.Contains(x))));
                    sw.WriteLine(string.Join(" ", Objects.Where(x => !so.Contains(x))));
                }
                else
                {
                    sw.WriteLine(string.Join(" ", Subjects));
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
                Predicates = line.Split(" ").Select(x => nf.CreateLiteralNode(x));
                if (useK2Triple)
                {
                    line = sr.ReadLine() ?? "";
                    var so = line.Split(" ").Select(x => nf.CreateLiteralNode(x));
                    line = sr.ReadLine() ?? "";
                    Subjects = so.Concat(line.Split(" ").Select(x => nf.CreateLiteralNode(x)));
                    line = sr.ReadLine() ?? "";
                    Objects = so.Concat(line.Split(" ").Select(x => nf.CreateLiteralNode(x)));
                }
                else
                {
                    line = sr.ReadLine() ?? "";
                    Subjects = line.Split(" ").Select(x => nf.CreateLiteralNode(x));
                    Objects = Subjects;
                }
            }
        }
    }

    public class K3 : IK2Extension
    {
        public IEnumerable<INode> Subjects { get; set; }
        public IEnumerable<INode> Objects { get; set; }
        public IEnumerable<INode> Predicates { get; set; }

        FastRankBitArray t { get; set; }
        bool useK2Triples { get; set; }
        int k { get; set; }
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
            Subjects = new List<INode>();
            Predicates = new List<INode>();
            Objects = new List<INode>();
            useK2Triples = false;
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            (int?, int?, int?)[] path = Array.IndexOf(Predicates.ToArray(), p).ToBase(k, size.ToBase(k).Length)
                .Select<int, (int?, int?, int?)>(x => (null, x, null)).ToArray();
            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public void Compress(IGraph graph, bool useK2Triples)
        {
            DynamicBitArray[] dynT = new DynamicBitArray[0];
            this.useK2Triples = useK2Triples;
            Subjects = graph.Triples.Select(x => x.Subject).Distinct();
            Objects = graph.Triples.Select(x => x.Object).Distinct();
            if (useK2Triples)
            {
                var so = Subjects.Intersect(Objects);
                Subjects = so.Concat(Subjects.Where(x => !so.Contains(x))).ToList();
                Objects = so.Concat(Objects.Where(x => !so.Contains(x))).ToList();
            }
            else
            {
                Subjects = Subjects.Concat(Objects).Distinct();
                Objects = Subjects;
            }
            Predicates = graph.Triples.Select(x => x.Predicate).Distinct();

            int size = Math.Max(Math.Max(Subjects.Count(), Objects.Count()), Predicates.Count());
            int N = k;
            while (N < size) N *= k;

            compressRec(ref dynT, 0, graph, 0, 0, 0, N);

            DynamicBitArray n = new DynamicBitArray();
            foreach (var l in dynT)
            {
                n.AddRange(l.GetFittedArray());
            }
            t = new FastRankBitArray(n.GetFittedArray());
        }

        public Triple[] Connections(INode s, INode o)
        {
            (int?, int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         where subj.i == obj.i
                                         select ((int?)subj.v, (int?)null, (int?)obj.v)).ToArray();


            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public Triple[] Decomp()
        {
            (int?, int?, int?)[] path = new (int?, int?, int?)[size.ToBase(k).Length];
            Array.Fill(path, (null, null, null));
            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public bool Exists(INode s, INode p, INode o)
        {
            (int?, int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         from pred in Array.IndexOf(Predicates.ToArray(), p).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         where subj.i == obj.i && obj.i == pred.i
                                         select ((int?)subj.v, (int?)pred.v, (int?)obj.v)).ToArray();
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
                Predicates = line.Split(" ").Select(x => nf.CreateLiteralNode(x));
                if (useK2Triple)
                {
                    line = sr.ReadLine() ?? "";
                    var so = line.Split(" ").Select(x => nf.CreateLiteralNode(x));
                    line = sr.ReadLine() ?? "";
                    Subjects = so.Concat(line.Split(" ").Select(x => nf.CreateLiteralNode(x)));
                    line = sr.ReadLine() ?? "";
                    Objects = so.Concat(line.Split(" ").Select(x => nf.CreateLiteralNode(x)));
                }
                else
                {
                    line = sr.ReadLine() ?? "";
                    Subjects = line.Split(" ").Select(x => nf.CreateLiteralNode(x));
                    Objects = Subjects;
                }
            }
        }

        public Triple[] Prec(INode o)
        {
            (int?, int?, int?)[] path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         select ((int?)null, (int?)null, (int?)obj.v)).ToArray();


            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            (int?, int?, int?)[] path = (from pred in Array.IndexOf(Predicates.ToArray(), p).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         where pred.i == obj.i
                                         select ((int?)null, (int?)pred.v, (int?)obj.v)).ToArray();


            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public void Store(string filename)
        {
            using (var sw = File.CreateText(filename))
            {
                sw.WriteLine(t.GetDataAsString());
                sw.WriteLine(string.Join(" ", Predicates));
                if (useK2Triples)
                {
                    var so = Subjects.Intersect(Objects);
                    sw.WriteLine(string.Join(" ", so));
                    sw.WriteLine(string.Join(" ", Subjects.Where(x => !so.Contains(x))));
                    sw.WriteLine(string.Join(" ", Objects.Where(x => !so.Contains(x))));
                }
                else
                {
                    sw.WriteLine(string.Join(" ", Subjects));
                }
            }
        }

        public Triple[] Succ(INode s)
        {
            (int?, int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         select ((int?)subj.v, (int?)null, (int?)null)).ToArray();
            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        public Triple[] SuccOfType(INode s, INode p)
        {

            (int?, int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         from pred in Array.IndexOf(Predicates.ToArray(), p).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                         where pred.i == subj.i
                                         select ((int?)subj.v, (int?)pred.v, (int?)null)).ToArray();

            Triple[] result = findNodesRec(0, path, new List<(int, int, int)>());
            return result;
        }

        private bool compressRec(ref DynamicBitArray[] levels, int level, IGraph graph, int posSubj, int posPred, int posObj, int N)
        {
            while (levels.Length <= level) levels = levels.Append(new DynamicBitArray()).ToArray();
            var submatrix = new BitArray((int)Math.Pow(k, 3));
            if (N == k)
            {
                int index = 0;
                for (int s = 0; s < k; s++)
                {
                    for (int p = 0; p < k; p++)
                    {
                        for (int o = 0; o < k; o++)
                        {
                            if (posSubj + s >= Subjects.Count() || posPred + p >= Predicates.Count() || posObj + o >= Objects.Count())
                                submatrix[index] = false;
                            else
                                submatrix[index] = graph.Triples.Any(x => x.Subject.Equals(Subjects.ElementAt(posSubj + s)) && x.Predicate.Equals(Predicates.ElementAt(posPred + p)) && x.Object.Equals(Objects.ElementAt(posObj + o)));
                            index++;
                        }
                    }
                }
            }
            else
            {
                int NextN = N / k;
                int index = 0;
                for (int s = 0; s < k; s++)
                {
                    for (int p = 0; p < k; p++)
                    {
                        for (int o = 0; o < k; o++)
                        {
                            submatrix[index] = compressRec(ref levels, level + 1, graph, posSubj + s * NextN, posPred + p * NextN, posObj + o * NextN, NextN);
                            index++;
                        }
                    }
                }
            }
            if (submatrix.Cast<bool>().Any(x => x == true))
            {
                levels[level].AddRange(submatrix);
                return true;
            }
            else
            {
                return false;
            }
        }

        private Triple[] findNodesRec(int positionInNodes, (int?, int?, int?)[] searchPath, List<(int, int, int)> parentPath)
        {
            List<Triple> result = new List<Triple>();
            (int?, int?, int?) position = searchPath[0];
            searchPath = searchPath.Skip(1).ToArray();
            for (int s = position.Item1 ?? 0; s < (position.Item1 + 1 ?? k); s++)
            {
                for (int p = position.Item2 ?? 0; p < (position.Item2 + 1 ?? k); p++)
                {
                    for (int o = position.Item3 ?? 0; o < (position.Item3 + 1 ?? k); o++)
                    {
                        int relativePosition = s * k * k + p * k + o;
                        int pos = positionInNodes + relativePosition;
                        List<(int, int, int)> parent = parentPath.Append((s, p, o)).ToList();
                        if (searchPath.Length == 0 && t[pos])
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
        public IEnumerable<INode> Subjects { get; set; }
        public IEnumerable<INode> Objects { get; set; }
        public IEnumerable<INode> Predicates { get; set; }

        int size
        {
            get
            {
                return Math.Max(Subjects.Count(), Objects.Count());
            }
        }
        Dictionary<INode, FastRankBitArray> t { get; set; }
        bool useK2Triples { get; set; }
        int k { get; set; }

        public MK2(int k)
        {
            this.k = k;
            t = new Dictionary<INode, FastRankBitArray>();
            Subjects = new List<INode>();
            Predicates = new List<INode>();
            Objects = new List<INode>();
            useK2Triples = false;
        }

        public void Compress(IGraph graph, bool useK2Triples)
        {
            DynamicBitArray[] dynT = new DynamicBitArray[0];
            this.useK2Triples = useK2Triples;
            Subjects = graph.Triples.Select(x => x.Subject).Distinct();
            Objects = graph.Triples.Select(x => x.Object).Distinct();
            if (useK2Triples)
            {
                var so = Subjects.Intersect(Objects);
                Subjects = so.Concat(Subjects.Where(x => !so.Contains(x))).ToList();
                Objects = so.Concat(Objects.Where(x => !so.Contains(x))).ToList();
            }
            else
            {
                Subjects = Subjects.Concat(Objects).Distinct();
                Objects = Subjects;
            }
            Predicates = graph.Triples.Select(x => x.Predicate).Distinct();

            int size = Math.Max(Math.Max(Subjects.Count(), Objects.Count()), Predicates.Count());
            int N = k;
            while (N < size) N *= k;

            foreach (var pred in Predicates)
            {
                DynamicBitArray[] dynTForPred = new DynamicBitArray[0];
                compressForPredicateRec(ref dynTForPred, pred, 0, graph, 0, 0, N);
                DynamicBitArray flatT = new DynamicBitArray();
                for (int i = 0; i < dynTForPred.Length; i++)
                {
                    flatT.AddRange(dynTForPred[i].GetFittedArray());
                }
                t.Add(pred, new FastRankBitArray(flatT.GetFittedArray()));
            }
        }

        private bool compressForPredicateRec(ref DynamicBitArray[] levels, INode predicate, int level, IGraph graph, int posSubj, int posObj, int N)
        {
            while (levels.Length <= level) levels = levels.Append(new DynamicBitArray()).ToArray();
            var submatrix = new BitArray(k * k);
            if (N == k)
            {
                int index = 0;
                for (int s = 0; s < k; s++)
                {
                    for (int o = 0; o < k; o++)
                    {
                        if (posSubj + s >= Subjects.Count() || posObj + o >= Objects.Count())
                            submatrix[index] = false;
                        else
                            submatrix[index] = graph.Triples.Any(x => x.Subject.Equals(Subjects.ElementAt(posSubj + s)) && x.Predicate.Equals(predicate) && x.Object.Equals(Objects.ElementAt(posObj + o)));
                        index++;
                    }
                }
            }
            else
            {
                int NextN = N / k;
                int index = 0;
                for (int o = 0; o < k; o++)
                {
                    for (int p = 0; p < k; p++)
                    {
                        for (int s = 0; s < k; s++)
                        {
                            submatrix[index] = compressForPredicateRec(ref levels, predicate, level + 1, graph, posSubj + s * NextN, posObj + o * NextN, NextN);
                            index++;
                        }
                    }
                }
            }
            if (submatrix.Cast<bool>().Any(x => x == true))
            {
                levels[level].AddRange(submatrix);
                return true;
            }
            else
            {
                return false;
            }
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            (int?, int?)[] path = new (int?, int?)[size.ToBase(k).Length];
            Array.Fill(path, (null, null));
            Triple[] result = findNodesRec(p, 0, path, new List<(int, int)>());
            return result;
        }

        public Triple[] Connections(INode s, INode o)
        {
            (int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   where subj.i == obj.i
                                   select ((int?)subj.v, (int?)obj.v)).ToArray();
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
                result.AddRange(findNodesRec(p, 0, path, new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public bool Exists(INode s, INode p, INode o)
        {
            (int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   where subj.i == obj.i
                                   select ((int?)subj.v, (int?)obj.v)).ToArray();
            Triple[] result = findNodesRec(p, 0, path, new List<(int, int)>());
            return result.Any();
        }

        public Triple[] Prec(INode o)
        {
            (int?, int?)[] path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   select ((int?)null, (int?)obj.v)).ToArray();
            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(findNodesRec(p, 0, path, new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            (int?, int?)[] path = (from obj in Array.IndexOf(Objects.ToArray(), o).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   select ((int?)null, (int?)obj.v)).ToArray();
            Triple[] result = findNodesRec(p, 0, path, new List<(int, int)>());
            return result;
        }

        public Triple[] Succ(INode s)
        {
            (int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   select ((int?)subj.v, (int?)null)).ToArray();

            var result = new List<Triple>();
            foreach (var p in Predicates)
            {
                result.AddRange(findNodesRec(p, 0, path, new List<(int, int)>()));
            }
            return result.ToArray();
        }

        public Triple[] SuccOfType(INode s, INode p)
        {
            (int?, int?)[] path = (from subj in Array.IndexOf(Subjects.ToArray(), s).ToBase(k, size.ToBase(k).Length).Select((v, i) => (v, i))
                                   select ((int?)subj.v, (int?)null)).ToArray();

            Triple[] result = findNodesRec(p, 0, path, new List<(int, int)>());
            return result;
        }

        public void Load(string filename, bool useK2Triple)
        {
            using (var sr = new StreamReader(filename))
            {
                string line = sr.ReadLine() ?? "";
                NodeFactory nf = new NodeFactory(new NodeFactoryOptions());
                List<FastRankBitArray> l = new List<FastRankBitArray>();
                while (line != "Tree stop")
                {
                    l.Add(new FastRankBitArray());
                    l[^1].Store(line);
                    line = sr.ReadLine() ?? "";
                }
                line = sr.ReadLine() ?? "";
                Predicates = line.Split(" ").Select(x => nf.CreateLiteralNode(x));
                foreach (var (pred, index) in Predicates.Select((v, i) => (v, i)))
                {
                    t.Add(pred, l[index]);
                }
                if (useK2Triple)
                {
                    line = sr.ReadLine() ?? "";
                    var so = line.Split(" ").Select(x => nf.CreateLiteralNode(x));
                    line = sr.ReadLine() ?? "";
                    Subjects = so.Concat(line.Split(" ").Select(x => nf.CreateLiteralNode(x)));
                    line = sr.ReadLine() ?? "";
                    Objects = so.Concat(line.Split(" ").Select(x => nf.CreateLiteralNode(x)));
                }
                else
                {
                    line = sr.ReadLine() ?? "";
                    Subjects = line.Split(" ").Select(x => nf.CreateLiteralNode(x));
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
                sw.WriteLine(string.Join(" ", Predicates));
                if (useK2Triples)
                {
                    var so = Subjects.Intersect(Objects);
                    sw.WriteLine(string.Join(" ", so));
                    sw.WriteLine(string.Join(" ", Subjects.Where(x => !so.Contains(x))));
                    sw.WriteLine(string.Join(" ", Objects.Where(x => !so.Contains(x))));
                }
                else
                {
                    sw.WriteLine(string.Join(" ", Subjects));
                }
            }
        }

        private Triple[] findNodesRec(INode predicate, int positionInNodes, (int?, int?)[] searchPath, List<(int, int)> parentPath)
        {
            List<Triple> result = new List<Triple>();
            (int?, int?) position = searchPath[0];
            searchPath = searchPath.Skip(1).ToArray();
            for (int s = position.Item1 ?? 0; s < (position.Item1 + 1 ?? k); s++)
            {
                for (int o = position.Item2 ?? 0; o < (position.Item2 + 1 ?? k); o++)
                {
                    int relativePosition = s * k * k + o;
                    int pos = positionInNodes + relativePosition;
                    List<(int, int)> parent = parentPath.Append((s, o)).ToList();
                    if (searchPath.Length == 0 && t[predicate][pos])
                    {
                        int posS = parent.Select(x => x.Item1).FromBase(k);
                        int posO = parent.Select(x => x.Item2).FromBase(k);
                        result.Add(new Triple(Subjects.ElementAt(posS), predicate, Objects.ElementAt(posO)));
                    }
                    else if (t[predicate][pos])
                    {
                        pos = t[predicate].Rank1(pos) * k * k;

                        result.AddRange(findNodesRec(predicate, pos, searchPath, parent));

                    }
                }
            }        
            return result.ToArray();
        }
    }
}
