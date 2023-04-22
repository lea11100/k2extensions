﻿using AngleSharp.Common;
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

namespace k2extensionsLib
{
    public class k2ArrayIndex : IK2Extension
    {
        FastRankBitArray nodes { get; set; }
        FastRankBitArray labels { get; set; }
        int startLeaves { get; set; }
        int k { get; set; }
        private bool useK2Triples { get;set;}
        public IEnumerable<INode> Subjects { get; set; }
        public IEnumerable<INode> Objects { get; set; }
        public IEnumerable<INode> Predicates { get; set; }

        public k2ArrayIndex(int k)
        {
            this.k = k;
            useK2Triples = false;
            nodes = new FastRankBitArray();
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
            Subjects = graph.Triples.Select(x=>x.Subject).Distinct();
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
            foreach (var l in levels.Take(levels.Length -1))
            {
                n.AddRange(l.GetFittedArray());
            }
            startLeaves = n.GetFittedArray().Length;
            n.AddRange(levels.Last().GetFittedArray());
            nodes = new FastRankBitArray(n.GetFittedArray());
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
                int positionInNodes = nodes.Select1(nodes.Rank1(startLeaves) + n + 1);
                Tuple<int, int> cell = getCell(positionInNodes);
                var r = new Triple(Subjects.ElementAt(cell.Item1), Predicates.ElementAt(positionInTypes), Objects.ElementAt(cell.Item2));
                result.Add(r);
            }
            return result.ToArray();  
        }

        public Triple[] Connections(INode s, INode o)
        {
            (int[] positionInSubjects, int[] positionInObjects) = getKBasedPosition(s, o);
            int position = 0;
            for (int i = 0; i < positionInSubjects.Length; i++)
            {
                int relativePosition = k * positionInSubjects[i] + positionInObjects[i];
                position += relativePosition;
                if (!nodes[position])
                {
                    return new Triple[0];
                }
                else if(i!=positionInSubjects.Length-1)
                {
                    position = nodes.Rank1(position) * k * k;
                }
            }
            var result = getLabelFormLeafPosition(position).Select(x=>new Triple(s,x,o));         
            return result.ToArray();
        }


        public Triple[] Decomp()
        {
            var result = new List<Triple>();
            for (int i = 0; i < k; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    int relativePosition = k * i + j;
                    result.AddRange(decompRec(relativePosition, new List<int>() { i }, new List<int>() { j}));

                }
            }
            return result.ToArray();
        }

        public bool Exists(INode s, INode p, INode o)
        {
            Triple[] cons = Connections(s, o);
            return cons.Where(x => x.Predicate.Equals(p)).Count() != 0;
        }

        public Triple[] Prec(INode o)
        {
            int[] position = getKBasedPosition(Array.IndexOf(Objects.ToArray(), o));
            List<Triple> result = new List<Triple>();
            for (int i = 0; i < k; i++)
            {
                int relativePosition = i * k + position[0];
                result.AddRange(precOrSuccRec(o, relativePosition, position.Skip(1).ToArray(), new List<int>() { i }, false));

            }
            return result.ToArray();
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            return Prec(o).Where(x => x.Predicate.Equals(p)).ToArray();
        }

        public Triple[] Succ(INode s)
        {
            int[] position = getKBasedPosition(Array.IndexOf(Subjects.ToArray(), s));
            List<Triple> result = new List<Triple>();
            for (int i = 0; i < k; i++)
            {
                int relativePosition = position[0] * k + i;
                result.AddRange(precOrSuccRec(s, relativePosition, position.Skip(1).ToArray(), new List<int>() { i}, true));

            }
            return result.ToArray();
        }

        public Triple[] SuccOfType(INode s, INode p)
        {
            return Succ(s).Where(x => x.Predicate.Equals(p)).ToArray();
        }

        private Triple[] precOrSuccRec(INode n, int positionInNodes, int[] searchPath, List<int> parentPath, bool searchObj)
        {
            List<Triple> result = new List<Triple>();

            if(searchPath.Length == 0 && nodes[positionInNodes])
            {
                result.AddRange(getLabelFormLeafPosition(positionInNodes).Select(
                    x => searchObj ? new Triple( n, x, Objects.ElementAt(parentPath.FromBase(k))):
                        new Triple(Subjects.ElementAt(parentPath.FromBase(k)), x, n)));
            }
            else if (!nodes[positionInNodes])
            {
                return result.ToArray();
            }
            else
            {
                int p = searchPath[0];
                searchPath = searchPath.Skip(1).ToArray();
                positionInNodes = nodes.Rank1(positionInNodes) * k * k ;
                for (int i = 0; i < k; i++)
                {
                    int relativePosition = searchObj ? p*k+i : i * k + p;
                    result.AddRange(precOrSuccRec(n, positionInNodes + relativePosition, searchPath, parentPath.Append(i).ToList(), searchObj));
                }
                
            }
            return result.ToArray();
        }

        private (int[], int[]) getKBasedPosition(INode subj, INode obj) 
        {
            int[] positionInSubjects = Array.IndexOf(Subjects.ToArray(), subj).ToBase(k);
            int[] positionInObjects = Array.IndexOf(Objects.ToArray(), obj).ToBase(k);
            int numberOfDigits = Math.Max(Subjects.Count(), Objects.Count()).ToBase(k).Length;
            while (positionInSubjects.Length < numberOfDigits) positionInSubjects = positionInSubjects.Prepend(0).ToArray();
            while (positionInObjects.Length < numberOfDigits) positionInObjects = positionInObjects.Prepend(0).ToArray();
            return (positionInSubjects, positionInObjects);
        }

        private int[] getKBasedPosition(int position)
        {
            int[] result = position.ToBase(k);
            int numberOfDigits = Math.Max(Subjects.Count(), Objects.Count()).ToBase(k).Length;
            while (result.Length < numberOfDigits) result = result.Prepend(0).ToArray();
            return result;
        }

        private Triple[] decompRec(int position, IEnumerable<int> row, IEnumerable<int> column)
        {
            var result = new List<Triple>();
            if (position >= startLeaves && nodes[position])
            {
                var edges = getLabelFormLeafPosition(position).Select(p =>
                    new Triple(Subjects.ElementAt(row.FromBase(k)), p, Objects.ElementAt(column.FromBase(k))));
                result.AddRange(edges);
            }
            else if (!nodes[position])
            {
                return result.ToArray();
            }
            else
            {
                position = nodes.Rank1(position) * k * k;
                for (int i = 0; i < k; i++)
                {
                    for (int j = 0; j < k; j++)
                    {
                        int relativePosition = k * i + j;
                        result.AddRange(decompRec(position + relativePosition, row.Append(i), column.Append(j)));

                    }
                }
            }
            return result.ToArray();
        }

        private INode[] getLabelFormLeafPosition(int position)
        {
            int rankInLeaves = nodes.Rank1(position, startLeaves) - 1;
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
                    if((block & ((ulong)1 << j)) != 0)
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
        private Tuple<int,int> getCell(int position)
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
                position = getParentPosition(position);
                power *= k;
            }
            return new Tuple<int, int>(row, col);
        }

        private int getParentPosition(int position)
        {
            int numberOf1Bits = position / (int)Math.Pow(k, 2);
            int result = nodes.Select1(numberOf1Bits);
            return result;
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
                        if(row + i < Subjects.Count() && col+j<Objects.Count())
                           triples = graph.GetTriplesWithSubjectObject(Subjects.ElementAt(row+i), Objects.ElementAt(col+j)).Select(x=>x.Predicate);

                        BitArray label = new BitArray(Predicates.Count(), false);
                        for (int l = 0; l < Predicates.Count(); l++)
                        {
                            if (triples.Contains(Predicates.ElementAt(l))) label[l] = true;
                        }

                        if (label.Cast<bool>().Any(x => x == true)) {
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

        public void Store(string filename)
        {
            using(var sw = File.CreateText(filename))
            {
                sw.WriteLine(startLeaves);
                sw.WriteLine(nodes.GetDataAsString());
                sw.WriteLine(labels.GetDataAsString());
                sw.WriteLine(string.Join(" ", Predicates));
                if (useK2Triples)
                {
                    var so = Subjects.Intersect(Objects);
                    sw.WriteLine(string.Join(" ", so));
                    sw.WriteLine(string.Join(" ", Subjects.Where(x=>!so.Contains(x))));
                    sw.WriteLine(string.Join(" ", Objects.Where(x=>!so.Contains(x))));
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
                nodes.Store(line);
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
}
