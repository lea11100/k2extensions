using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;

namespace k2extensionsLib
{
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

    public interface IK2Extension
    {
        IEnumerable<INode> Subjects { get; set; }
        IEnumerable<INode> Objects { get; set; }
        IEnumerable<INode> Predicates { get; set; }
        void Compress(IGraph graph, bool useK2Triples);
        Triple[] Decomp();
        Triple[] Prec(INode o);
        Triple[] Succ(INode s);
        Triple[] AllEdgesOfType(INode p);
        Triple[] Connections(INode s, INode o);
        Triple[] PrecOfType(INode o, INode p);
        Triple[] SuccOfType(INode s, INode p);
        bool Exists(INode s, INode p, INode o);
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
