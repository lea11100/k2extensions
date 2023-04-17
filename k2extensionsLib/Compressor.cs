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
        BitArray nodes { get; set; }
        BitArray labels { get; set; }
        int startLeaves { get; set; }
        int k { get; set; }
        public IEnumerable<INode> Subjects { get; set; }
        public IEnumerable<INode> Objects { get; set; }
        public IEnumerable<INode> Predicates { get; set; }

        public k2ArrayIndex(int k)
        {
            this.k = k;
            nodes = new BitArray(0);
            labels = new BitArray(0);
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

            build(ref levels, ref labels, 0, graph, 0, 0, N, k);

            this.labels = labels.GetFittedArray();
            DynamicBitArray n = new DynamicBitArray();
            foreach (var l in levels.Take(levels.Length -1))
            {
                n.AddRange(l.GetFittedArray());
            }
            startLeaves = n.GetFittedArray().Length;
            n.AddRange(levels.Last().GetFittedArray());
            nodes = n.GetFittedArray();
        }

        public Triple[] AllEdgesOfType(INode p)
        {
            List<Triple> result = new List<Triple>();
            int positionInTypes = Array.IndexOf(Predicates.ToArray(), p);
            List<int> nodesWithType = new List<int>();
            int counter = positionInTypes;
            while (counter < labels.Length)
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
            int[] positionInSubjects = Array.IndexOf(Subjects.ToArray(), s).ToBase(k);
            int[] positionInObjects = Array.IndexOf(Objects.ToArray(), o).ToBase(k);
            int numberOfDigits = Math.Max(Subjects.Count(), Objects.Count()).ToBase(k).Length;
            while (positionInSubjects.Length < numberOfDigits) positionInSubjects.Prepend(0);
            while (positionInObjects.Length < numberOfDigits) positionInObjects.Prepend(0);
            int position = 0;
            for (int i = 0; i < numberOfDigits; i++)
            {

            }
            throw new NotImplementedException();
        }

        public Triple[] Decomp()
        {
            throw new NotImplementedException();
        }

        public bool Exists(INode s, INode p, INode o)
        {
            throw new NotImplementedException();
        }

        public Triple[] Prec(INode o)
        {
            throw new NotImplementedException();
        }

        public Triple[] PrecOfType(INode o, INode p)
        {
            throw new NotImplementedException();
        }

        public Triple[] Succ(INode s)
        {
            throw new NotImplementedException();
        }

        public Triple[] SuccOfType(INode s, INode p)
        {
            throw new NotImplementedException();
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
            int result = nodes.select1(numberOf1Bits);
            return result;
        }

        private bool build(ref DynamicBitArray[] levels, ref DynamicBitArray labels, int level, IGraph graph, int row, int col, int N, int k)
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
                        submatrix[index] = build(ref levels, ref labels, level + 1, graph, row + i * NextN, col + j * NextN, NextN, k);
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
