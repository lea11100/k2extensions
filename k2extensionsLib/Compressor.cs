using J2N.Numerics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;

namespace k2extensionsLib
{
    internal class k2ArrayIndex : IK2Extension
    {
        FastRankBitArray nodes { get; set; }
        FastRankBitArray labels { get; set; }
        int startLeaves { get; set; }
        int k { get; set; }
        public IEnumerable<INode> Subjects { get; set; }
        public IEnumerable<INode> Objects { get; set; }
        public IEnumerable<INode> Predicates { get; set; }

        public k2ArrayIndex(int k)
        {
            this.k = k;
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

            Subjects = graph.Triples.Select(x=>x.Subject).Distinct();
            Objects = graph.Triples.Select(x => x.Object).Distinct();
            if (useK2Triples) 
            {
                Subjects = Subjects.OrderBy(x => Objects.Contains(x));
                Objects = Objects.OrderBy(x => Subjects.Contains(x));
            }
            else
            {
                Subjects = Subjects.Concat(Objects);
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
            while (counter < labels.Length())
            {
                if (labels[counter]) nodesWithType.Add(counter);
                counter += Predicates.Count();
            }
            foreach (var n in nodesWithType)
            {
                Tuple<int, int> cell = getCell(startLeaves + n);
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
                else
                {
                    position = nodes.Rank1(position) * k * k;
                }
            }
            var result = getLabelFormLeafPosition(position).Select(x=>new Triple(s,x,o));         
            return result.ToArray();
        }


        public Triple[] Decomp()
        {
            return decompRec(0, new List<int>(), new List<int>());
        }

        public bool Exists(INode s, INode p, INode o)
        {
            Triple[] cons = Connections(s, o);
            return cons.Where(x => x.Predicate == p).Count() != 0;
        }

        public Triple[] Prec(INode o)
        {
            int[] position = getKBasedPosition(Array.IndexOf(Objects.ToArray(), o));
            Triple[] result = precOrSuccRec(o, 0, position, new List<int>(), false);
            return result;
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            return Prec(o).Where(x => x.Predicate == p).ToArray();
        }

        public Triple[] Succ(INode s)
        {
            int[] position = getKBasedPosition(Array.IndexOf(Objects.ToArray(), o));
            Triple[] result = precOrSuccRec(o, 0, position, new List<int>(), true);
            return result;
        }

        public Triple[] SuccOfType(INode s, INode p)
        {
            return Succ(s).Where(x => x.Predicate == p).ToArray();
        }

        private Triple[] precOrSuccRec(INode n, int positionInNodes, int[] searchPath, List<int> parentPath, bool searchObj)
        {
            List<Triple> result = new List<Triple>();
            if (!nodes[positionInNodes])
            {
                return result.ToArray();
            }
            else if (searchPath.Length == 0)
            {
                result.AddRange(getLabelFormLeafPosition(positionInNodes).Select(
                    x => new Triple(
                        searchObj ? Objects.ElementAt(parentPath.FromBase(k)) : Subjects.ElementAt(parentPath.FromBase(k)),
                        x, n)));
            }
            else
            {
                int p = searchPath[0];
                searchPath = searchPath.Skip(1).ToArray();
                positionInNodes = nodes.Rank1(positionInNodes) * k * k;
                for (int i = 0; i < k; i++)
                {
                    int relativePosition = searchObj ? i*p+k : i * k + p;
                    result.AddRange(precOrSuccRec(n, positionInNodes + relativePosition, searchPath, parentPath.Prepend(i).ToList(), searchObj));
                }
            }
            return result.ToArray();
        }

        private (int[], int[]) getKBasedPosition(INode subj, INode obj) 
        {
            int[] positionInSubjects = Array.IndexOf(Subjects.ToArray(), subj).ToBase(k);
            int[] positionInObjects = Array.IndexOf(Objects.ToArray(), obj).ToBase(k);
            int numberOfDigits = Math.Max(Subjects.Count(), Objects.Count()).ToBase(k).Length;
            while (positionInSubjects.Length < numberOfDigits) positionInSubjects.Prepend(0);
            while (positionInObjects.Length < numberOfDigits) positionInObjects.Prepend(0);
            return (positionInSubjects, positionInObjects);
        }

        private int[] getKBasedPosition(int position)
        {
            int[] result = position.ToBase(k);
            int numberOfDigits = Math.Max(Subjects.Count(), Objects.Count()).ToBase(k).Length;
            while (result.Length < numberOfDigits) result.Prepend(0);
            return result;
        }

        private Triple[] decompRec(int position, IEnumerable<int> row, IEnumerable<int> column)
        {
            var result = new List<Triple>();
            if (position >= startLeaves)
            {
                var edges = getLabelFormLeafPosition(position).Select(p =>
                    new Triple(Subjects.ElementAt(row.FromBase(k)), p, Objects.ElementAt(column.FromBase(k))));
                result.AddRange(edges);
            }
            else if (!nodes[position])
            {
                return result.ToArray();
            }
            for (int i = 0; i < k; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    int relativePosition = k * i + j;
                    result.AddRange(decompRec(position + relativePosition, row.Prepend(i), column.Prepend(j)));

                }
            }
            return result.ToArray();
        }

        private INode[] getLabelFormLeafPosition(int position)
        {
            int rankInLeaves = nodes.Rank1(position, startLeaves);
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
                for (int j = 0; j < 64; j++)
                {
                    if((block & (ulong)(1 << j - 1)) != 0)
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
            while (levels.Length <= level) levels.Append(new DynamicBitArray());
            var submatrix = new BitArray((int)Math.Pow(k, 2));
            if (N == k)
            {
                int index = 0;
                DynamicBitArray additionalLabels = new DynamicBitArray();
                for (int i = 0; i < k; i++)
                {
                    for (int j = 0; j < k; j++)
                    {
                        IEnumerable<INode> triples = graph.GetTriplesWithSubjectObject(Subjects.ElementAt(row+i), Objects.ElementAt(col+j)).Select(x=>x.Predicate);
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


    }
}
