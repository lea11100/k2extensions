using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace k2extensions
{
    internal class Converter
    {
        public static AdjacencyMatrixWithLabels ConvertFromFile(string filename)
        {
            List<string> nodes = new List<string>();
            List<string> predicates = new List<string>();
            int counter = 0;
            using (var sr = new StreamReader(filename))
            {
                string? line = sr.ReadLine();
                while (line != null)
                {
                    string[] split = line.Split(' ');
                    var s = split[0]; var p = split[1]; var o = split[2];
                    nodes.Add(s);
                    nodes.Add(o);
                    predicates.Add(p);
                    if (counter == 1000) //shrink lists every 1000th entry
                    {
                        nodes = nodes.Distinct().ToList();
                        predicates = predicates.Distinct().ToList();
                    }
                }
                nodes = nodes.Distinct().ToList();
                predicates = predicates.Distinct().ToList();
            }
            BitArray[][] matrix = Enumerable.Repeat(Enumerable.Repeat(new BitArray(predicates.Count), nodes.Count).ToArray(), nodes.Count).ToArray();

            using (var sr = new StreamReader(filename))
            {
                string? line = sr.ReadLine();
                while (line != null)
                {             
                    string[] split = line.Split(' ');
                    var s = split[0]; var p = split[1]; var o = split[2];
                    matrix[nodes.IndexOf(s)][nodes.IndexOf(o)][predicates.IndexOf(p)] = true;                    
                }
            }

            return new AdjacencyMatrixWithLabels(matrix);
        }
    }
}
