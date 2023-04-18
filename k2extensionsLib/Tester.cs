using J2N.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Intrinsics.X86;
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

            List<Triple> testValues = new List<Triple>();
            Random r = new Random();
            for(int i = 0; i < 100; i++)
            {
                testValues.Add(new Triple(
                    extensionsUnderTest[0].Subjects.ElementAt(r.Next(extensionsUnderTest[0].Subjects.Count())),
                    extensionsUnderTest[0].Predicates.ElementAt(r.Next(extensionsUnderTest[0].Predicates.Count())),
                    extensionsUnderTest[0].Objects.ElementAt(r.Next(extensionsUnderTest[0].Objects.Count()))
                    ));
            }
            string result = "Name,Compression,SPO,SP?O,SP?O?,S?P?O,S?PO?,S?PO,SPO?\r\n";
            List<List<double>> timeResults = new List<List<double>>(7);
            foreach (var ext in extensionsUnderTest)
            {
                result += ext.GetType().Name + ",";
                result += GetExecutionTime(()=>ext.Compress(g, useK2Triples)) + ",";
                foreach (var t in testValues)
                {
                    timeResults[0].Add(GetExecutionTime(() => ext.Exists(t.Subject, t.Predicate, t.Object)));
                    timeResults[1].Add(GetExecutionTime(() => ext.Connections(t.Subject, t.Object)));
                    timeResults[2].Add(GetExecutionTime(() => ext.Succ(t.Subject)));
                    timeResults[3].Add(GetExecutionTime(() => ext.Prec(t.Object)));
                    timeResults[4].Add(GetExecutionTime(() => ext.AllEdgesOfType(t.Predicate)));
                    timeResults[5].Add(GetExecutionTime(() => ext.PrecOfType(t.Object, t.Predicate)));
                    timeResults[6].Add(GetExecutionTime(() => ext.SuccOfType(t.Subject, t.Predicate)));
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
