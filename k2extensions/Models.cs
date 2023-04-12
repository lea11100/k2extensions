using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace k2extensions
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
            if(size != 1)
            {
                bool[] result = new bool[(int)Math.Pow(size, 2)];
                int halfSize = size / 2;
                GetForK2Rec(row, column, halfSize).CopyTo(result,0);
                GetForK2Rec(row, column+ halfSize, halfSize).CopyTo(result, halfSize);
                GetForK2Rec(row+halfSize, column, halfSize).CopyTo(result, halfSize * 2);
                GetForK2Rec(row+halfSize, column+halfSize, halfSize).CopyTo(result, halfSize * 3);
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
        private int numberRows { get; }
        private int numberCols { get; }
        private int labelLength { get; }

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
}
