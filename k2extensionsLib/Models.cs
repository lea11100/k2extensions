using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace k2extensionsLib
{
    internal class AdjacencyMatrix
    {
        /// <summary>
        /// Row by row
        /// </summary>
        private BitArray data { get; }

        private int numberRows { get; }
        private int numberCols { get; }

        public AdjacencyMatrix(BitArray data, int numberRows, int numberCols)
        {
            this.data = data;
            this.numberRows = numberRows;
            this.numberCols = numberCols;
        }

        internal bool GetBitOrZero(int row, int column)
        {
            int index = row * numberCols + column;
            if (index >= data.Length) return false;
            return data.Get(index);
        }

        internal BitArray GetForK2()
        {
            int size = 1 << (sizeof(uint) * 8 - BitOperations.LeadingZeroCount((uint)numberCols - 1)); //Round up to next power of 2
            return GetForK2Rec(0, 0, size);
        }

        private BitArray GetForK2Rec(int row, int column, int size)
        {
            if (size != 1)
            {
                bool[] result = new bool[(int)Math.Pow(size, 2)];
                int halfSize = size / 2;
                GetForK2Rec(row, column, halfSize).CopyTo(result, 0);
                GetForK2Rec(row, column + halfSize, halfSize).CopyTo(result, halfSize);
                GetForK2Rec(row + halfSize, column, halfSize).CopyTo(result, halfSize * 2);
                GetForK2Rec(row + halfSize, column + halfSize, halfSize).CopyTo(result, halfSize * 3);
                return new BitArray(result);
            }
            else
            {
                return new BitArray(new bool[] { GetBitOrZero(row, column) });
            }
        }
    }

    internal class AdjacencyMatrixWithLabels
    {
        /// <summary>
        /// Stores cells one by one. A cell is labelLength many bits wide
        /// </summary>
        private BitArray data { get; }
        public int numberRows { get; }
        public int numberCols { get; }
        public int labelLength { get; }

        public AdjacencyMatrixWithLabels(BitArray data, int numberRows, int numberCols, int labelLength)
        {
            this.data = data;
            this.numberRows = numberRows;
            this.numberCols = numberCols;
            this.labelLength = labelLength;
        }

        public AdjacencyMatrixWithLabels(BitArray[][] matrix)
        {
            numberRows = matrix.Length;
            numberCols = matrix[0].Length;
            labelLength = matrix[0][0].Length;
            var tempData = new bool[numberRows * numberCols * labelLength];
            int index = 0;
            for (int i = 0; i < matrix.Length; i++)
            {
                for (int j = 0; j < matrix[0].Length; j++)
                {
                    matrix[i][j].CopyTo(tempData, index);
                    index += labelLength;
                }
            }
            data = new BitArray(tempData);
        }

        internal BitArray GetLabelOrZero(int row, int column)
        {
            int index = row * numberCols * labelLength + column * labelLength;
            BitArray result = new BitArray(labelLength, false);
            if (index >= data.Length) return result;
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = data[index + i];
            }
            return result;
        }

        internal BitArray GetForK2()
        {
            int size = 1 << (sizeof(uint) * 8 - BitOperations.LeadingZeroCount((uint)numberCols - 1)); //Round up to next power of 2
            return GetForK2Rec(0, 0, size);
        }

        private BitArray GetForK2Rec(int row, int column, int size)
        {
            if (size != 1)
            {
                bool[] result = new bool[(int)Math.Pow(size, 2)];
                int halfSize = size / 2;
                GetForK2Rec(row, column, halfSize).CopyTo(result, 0);
                GetForK2Rec(row, column + halfSize, halfSize).CopyTo(result, halfSize);
                GetForK2Rec(row + halfSize, column, halfSize).CopyTo(result, halfSize * 2);
                GetForK2Rec(row + halfSize, column + halfSize, halfSize).CopyTo(result, halfSize * 3);
                return new BitArray(result);
            }
            else
            {
                return GetLabelOrZero(row, column);
            }
        }

    }

    internal class RdfEntry
    {
        public string Subject { get; set; }
        public string Object { get; set; }
        public string Predicate { get; set; }

        public RdfEntry(string s, string p, string o)
        {
            Subject = s;
            Object = o;
            Predicate = p;
        }
    }

    internal class DynamicBitArray : IList<bool>
    {
        BitArray data { get; set; } = new BitArray(128, false);
        int lastIndex { get; set; } = 0;

        public bool this[int index] { get => data[index]; set => data[index] = value; }

        public int Count => data.Count;

        public bool IsReadOnly => data.IsReadOnly;

        public void Add(bool item)
        {
            if (lastIndex >= data.Count)
                data.Length += 128;
            data[lastIndex] = item;
            lastIndex++;
        }

        public void AddRange(BitArray array)
        {
            for (int i = 0; i < array.Length; i++)
            {
                Add(array[i]);
            }
        }

        public void Clear()
        {
            data = new BitArray(128, false);
            lastIndex = 0;
        }

        public bool Contains(bool item)
        {
            return data.Cast<bool>().Contains(item);
        }

        public void CopyTo(bool[] array, int arrayIndex)
        {
            data.CopyTo(array, arrayIndex);
        }

        public IEnumerator<bool> GetEnumerator()
        {
            return data.Cast<bool>().GetEnumerator();
        }

        public int IndexOf(bool item)
        {
            return data.Cast<bool>().ToList().IndexOf(item);
        }

        public void Insert(int index, bool item)
        {
            throw new NotImplementedException();
        }

        public bool Remove(bool item)
        {
            throw new NotImplementedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return data.GetEnumerator();
        }

        public BitArray GetFittedArray()
        {
            shrink();
            return data;
        }

        private void shrink()
        {
            data.Length = lastIndex;
        }
    }

    internal interface IK2Extension
    {
        string[] Subjects { get; set; }
        string[] Objects { get; set; }
        string[] Predicates { get; set; }
        void Compress(AdjacencyMatrixWithLabels matrix);
        RdfEntry[] Decomp();
        RdfEntry[] Prec(string o);
        RdfEntry[] Succ(string s);
        RdfEntry[] AllEdgesOfType(string p);
        RdfEntry[] Connections(string s, string o);
        RdfEntry[] PrecOfType(string o, string p);
        RdfEntry[] SuccOfType(string s, string p);
        bool Exists(string s, string p, string o);
    }

    internal static class GeneralExtensions
    {
        internal static int rank1(this BitArray array, int index)
        {
            int result = array.Cast<bool>().Take(index + 1).Count(x => x);
            return result;
        }

        internal static int select1(this BitArray array, int k, int start = 0)
        {
            int result = array.Cast<bool>().Select((v, i) => new { value = v, index = i })
                    .Where(item => item.value && item.index >= start)
                    .Skip(k - 1)
                    .FirstOrDefault()?.index ?? 0;
            return result;
        }

        internal static int[] ToBase(this int value, int baseSize)
        {
            Stack<int> digits = new Stack<int>();

            long tmp = value;
            while (tmp != 0)
            {
                digits.Push((int)(tmp % baseSize));
                tmp = (long)((tmp - digits.Peek()) / baseSize);
            }

            return digits.ToArray();
        }
    }
}
