using AngleSharp.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using VDS.RDF;

namespace k2extensionsLib
{
    internal class FileReader
    {
        public static AdjacencyMatrixWithLabels Convert(IGraph graph)
        {
            var nodes = graph.Triples.Select(x=>x.Subject.ToString()).Concat(graph.Triples.Select(x => x.Object.ToString())).Distinct().ToList();
            var predicates = graph.Triples.Select(x => x.Predicate.ToString()).Distinct().ToList();

            BitArray[][] matrix = Enumerable.Repeat(Enumerable.Repeat(new BitArray(predicates.Count), nodes.Count).ToArray(), nodes.Count).ToArray();

            foreach (var e in graph.Triples)
            {
                int r = nodes.IndexOf(e.Subject.ToString());
                int c = nodes.IndexOf(e.Object.ToString());
                int p = predicates.IndexOf(e.Predicate .ToString());
                matrix[r][c][p] = true;
            }

            return new AdjacencyMatrixWithLabels(matrix);
        }

        public static AdjacencyMatrixWithLabels ConvertUsingK2Triples(IGraph graph)
        {
            var so = graph.Triples.Select(x => x.Subject.ToString()).Intersect(graph.Triples.Select(x => x.Object.ToString()));
            var preds = graph.Triples.Select(x => x.Predicate.ToString()).Distinct().ToList();

            var rows = so.Concat(graph.Triples.Select(x => x.Subject.ToString()).Except(graph.Triples.Select(x => x.Object.ToString()))).ToList();
            var cols = so.Concat(graph.Triples.Select(x => x.Object.ToString()).Except(graph.Triples.Select(x => x.Subject.ToString()))).ToList();
            so = null;
            BitArray[][] matrix = Enumerable.Repeat(Enumerable.Repeat(new BitArray(preds.Count()), rows.Count).ToArray(), cols.Count).ToArray();

            foreach (var e in graph.Triples)
            {
                int r = rows.IndexOf(e.Subject.ToString());
                int c = cols.IndexOf(e.Object.ToString());
                int p = preds.IndexOf(e.Predicate.ToString());

                matrix[r][c][p] = true;
            }

            return new AdjacencyMatrixWithLabels(matrix);
        }
    }
}
