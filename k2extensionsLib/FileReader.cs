using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace k2extensionsLib
{
    internal class FileReader
    {
        public static AdjacencyMatrixWithLabels ConvertFromFile(string filename)
        {
            var entries = from l in File.ReadLines(filename)
                          let entry = l.Split(' ')
                          select new RdfEntry(entry[0], entry[1], entry[2]);

            var nodes = entries.Select(x=>x.Subject).Concat(entries.Select(x => x.Object)).Distinct().ToList();
            var predicates = entries.Select(x => x.Predicate).Distinct().ToList();

            BitArray[][] matrix = Enumerable.Repeat(Enumerable.Repeat(new BitArray(predicates.Count), nodes.Count).ToArray(), nodes.Count).ToArray();

            foreach (var e in entries)
            {
                int r = nodes.IndexOf(e.Subject);
                int c = nodes.IndexOf(e.Object);
                int p = predicates.IndexOf(e.Predicate);
                matrix[r][c][p] = true;
            }

            return new AdjacencyMatrixWithLabels(matrix);
        }

        public static AdjacencyMatrixWithLabels ConvertFromFileUsingK2Triples(string filename)
        {
            var entries = from l in File.ReadLines(filename)
                    let entry = l.Split(' ')
                    select new RdfEntry(entry[0], entry[1], entry[2]);
            var so = entries.Select(x => x.Subject).Intersect(entries.Select(x => x.Object));
            var preds = entries.Select(x => x.Predicate).Distinct().ToList();

            var rows = so.Concat(entries.Select(x => x.Subject).Except(entries.Select(x => x.Object))).ToList();
            var cols = so.Concat(entries.Select(x => x.Object).Except(entries.Select(x => x.Subject))).ToList();
            so = null;
            BitArray[][] matrix = Enumerable.Repeat(Enumerable.Repeat(new BitArray(preds.Count()), rows.Count).ToArray(), cols.Count).ToArray();

            foreach (var e in entries)
            {
                int r = rows.IndexOf(e.Subject);
                int c = cols.IndexOf(e.Object);
                int p = preds.IndexOf(e.Predicate);

                matrix[r][c][p] = true;
            }

            return new AdjacencyMatrixWithLabels(matrix);
        }
    }
}
