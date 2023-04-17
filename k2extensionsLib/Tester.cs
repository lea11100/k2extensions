using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;

namespace k2extensionsLib
{
    public class Tester
    {
        public string TestExtensions(List<IK2Extension> extensionsUnderTest, string fileName, bool useK2Triples)
        {
            IGraph g = new Graph();
            g.LoadFromFile(fileName);

            AdjacencyMatrixWithLabels matrix;
            if (useK2Triples) 
                matrix = FileReader.ConvertUsingK2Triples(g);   
            else           
                matrix = FileReader.Convert(g);

            List<Tuple<string, string, string>> testValues = new List<Tuple<string, string, string>>();
            Random r = new Random();
            for(int i = 0; i < 100; i++)
            {
                testValues.Add(new Tuple<string, string, string>(
                    extensionsUnderTest[0].Subjects[r.Next(matrix.numberRows)],
                    extensionsUnderTest[0].Predicates[r.Next(matrix.labelLength)],
                    extensionsUnderTest[0].Objects[r.Next(matrix.numberCols)]
                    ));
            }
            string result = "Name,Compression,SPO,SP?O,SP?O?,S?P?O,S?PO?,S?PO,SPO?\r\n";
            List<List<double>> timeResults = new List<List<double>>(7);
            foreach (var ext in extensionsUnderTest)
            {
                result += ext.GetType().Name + ",";
                result += GetExecutionTime(()=>ext.Compress(matrix)) + ",";
                foreach (var t in testValues)
                {
                    timeResults[0].Add(GetExecutionTime(() => ext.Exists(t.Item1, t.Item2, t.Item3)));
                    timeResults[1].Add(GetExecutionTime(() => ext.Connections(t.Item1, t.Item3)));
                    timeResults[2].Add(GetExecutionTime(() => ext.Succ(t.Item1)));
                    timeResults[3].Add(GetExecutionTime(() => ext.Prec(t.Item3)));
                    timeResults[4].Add(GetExecutionTime(() => ext.AllEdgesOfType(t.Item2)));
                    timeResults[5].Add(GetExecutionTime(() => ext.PrecOfType(t.Item3, t.Item2)));
                    timeResults[6].Add(GetExecutionTime(() => ext.SuccOfType(t.Item1, t.Item2)));
                }
                timeResults.ForEach(x => result += x.Average() + ",");
                result += GetExecutionTime(() => ext.Decomp()) + "\r\n";
            }
            return result;
        }

        private double GetExecutionTime(Action method)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            method();
            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            return elapsedMs;
        }
    }
}
