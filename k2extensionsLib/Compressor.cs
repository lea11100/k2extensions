using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace k2extensionsLib
{
    internal class k2ArrayIndex : k2Extension
    {
        BitArray nodes { get; set; }
        BitArray labels { get; set; }
        int startLeaves { get; set; }
        int k { get; set; }
        string[] subjects { get; set; }
        string[] objects { get; set; }
        string[] predicates { get; set; }

        public k2ArrayIndex(int k, string[] subjects, string[] objects, string[] predicates)
        {
            this.k = k;
            this.subjects = subjects;
            this.objects = objects;
            this.predicates = predicates;
            nodes = new BitArray(0);
            labels = new BitArray(0);
        }

        public void Compress(AdjacencyMatrixWithLabels matrix)
        {
            DynamicBitArray[] levels = new DynamicBitArray[0];
            DynamicBitArray labels = new DynamicBitArray();

            int size = Math.Max(matrix.numberCols, matrix.numberRows);
            int N = k;
            while (N < size) N *= k;

            build(ref levels, ref labels, 0, matrix, 0, 0, N, k);

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

        public RdfEntry[] allEdgesOfType(string p)
        {
            List<RdfEntry> result = new List<RdfEntry>();
            int positionInTypes = Array.IndexOf(predicates, p);
            List<int> nodesWithType = new List<int>();
            int counter = positionInTypes;
            while (counter < labels.Length)
            {
                if (labels[counter]) nodesWithType.Add(counter);
                counter += predicates.Length;
            }
            foreach (var n in nodesWithType)
            {
                Tuple<int, int> cell = getCell(startLeaves + n);
                var r = new RdfEntry(subjects[cell.Item1], predicates[positionInTypes], objects[cell.Item2]);
                result.Add(r);
            }
            return result.ToArray();  
        }

        public RdfEntry[] connections(string s, string o)
        {
            int positionInSubjects = Array.IndexOf(subjects, s);
            int positionInObjects = Array.IndexOf(objects, o);
            int position = 0;
            while (position < startLeaves)
            {

            }
            throw new NotImplementedException();
        }

        public RdfEntry[] decomp()
        {
            throw new NotImplementedException();
        }

        public bool exists(string s, string p, string o)
        {
            throw new NotImplementedException();
        }

        public RdfEntry[] prec(string o)
        {
            throw new NotImplementedException();
        }

        public RdfEntry[] precOfType(string o, string p)
        {
            throw new NotImplementedException();
        }

        public RdfEntry[] succ(string s)
        {
            throw new NotImplementedException();
        }

        public RdfEntry[] succOfType(string s, string p)
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

        private bool build(ref DynamicBitArray[] levels, ref DynamicBitArray labels, int level, AdjacencyMatrixWithLabels matrix, int row, int col, int N, int k)
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
                        BitArray label = matrix.GetLabelOrZero(row + i, col + j);
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
                        submatrix[index] = build(ref levels, ref labels, level + 1, matrix, row + i * NextN, col + j * NextN, NextN, k);
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
