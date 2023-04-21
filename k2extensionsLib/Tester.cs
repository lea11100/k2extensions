using J2N.Numerics;
using Lucene.Net.Diagnostics;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;
using VDS.RDF.Shacl.Validation;

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
            var s = g.Triples.Select(x => x.Subject).Distinct();
            var o = g.Triples.Select(x => x.Object).Distinct();
            var p = g.Triples.Select(x => x.Predicate).Distinct();
            for (int i = 0; i < 20; i++)
            {
                testValues.Add(g.Triples.ElementAt(r.Next(g.Triples.Count())));
            }
            for (int i = 0; i < 80; i++)
            {
                testValues.Add(new Triple(
                    s.ElementAt(r.Next(s.Count())),
                    p.ElementAt(r.Next(p.Count())),
                    o.ElementAt(r.Next(o.Count()))
                    ));
            }
            s = null; p = null; o = null;
            var correctValues = getCorrectValues(testValues, g);
            var testResExists = correctValues.Item1;
            var testRes = correctValues.Item2;
            var uncompressedSize = new FileInfo(fileName).Length;
            string result = "Name,Compression,SPO,SP?O,SP?O?,S?P?O,S?PO?,S?PO,SPO?,Compression ratio\r\n";
            List<List<double>> timeResults = new List<List<double>>();
            for (int i = 0; i < 8; i++)
            {
                timeResults.Add(new List<double>());
            }
            foreach (var ext in extensionsUnderTest)
            {
                result += ext.GetType().Name + ",";
                result += GetExecutionTime(()=>ext.Compress(g, useK2Triples)) + ",";
                for(int i =0; i < testValues.Count; i++)
                {
                    Triple t = testValues[i];
                    bool resExists = false;
                    timeResults[0].Add(GetExecutionTime(ref resExists, () => ext.Exists(t.Subject, t.Predicate, t.Object)));
                    Assert.AreEqual(testResExists[i], resExists);
                    IEnumerable<Triple> res = new List<Triple>();
                    timeResults[1].Add(GetExecutionTime(ref res, () => ext.Connections(t.Subject, t.Object)));
                    Assert.AreEqual(testRes[i][0], res.Sort());
                    timeResults[2].Add(GetExecutionTime(ref res, () => ext.Succ(t.Subject)));
                    Assert.AreEqual(testRes[i][1], res.Sort());
                    timeResults[3].Add(GetExecutionTime(ref res, () => ext.Prec(t.Object)));
                    Assert.AreEqual(testRes[i][2], res.Sort());
                    timeResults[4].Add(GetExecutionTime(ref res, () => ext.AllEdgesOfType(t.Predicate)));
                    Assert.AreEqual(testRes[i][3], res.Sort());
                    timeResults[5].Add(GetExecutionTime(ref res, () => ext.PrecOfType(t.Object, t.Predicate)));
                    Assert.AreEqual(testRes[i][4], res.Sort());
                    timeResults[6].Add(GetExecutionTime(ref res, () => ext.SuccOfType(t.Subject, t.Predicate)));
                    Assert.AreEqual(testRes[i][5], res.Sort());
                    timeResults[7].Add(GetExecutionTime(ref res, ext.Decomp));
                    Assert.AreEqual(testRes[i][6], res.Sort());
                }
                timeResults.ForEach(x => result += x.Average() + ",");
                result += GetExecutionTime(() => ext.Decomp()) + ";";
                ext.Store(ext.GetType().Name + "_Compression.txt");
                long compressedSíze = new FileInfo(ext.GetType().Name + "_Compression.txt").Length;
                result += ((double) compressedSíze / uncompressedSize).ToString("P2") +"\r\n";
            }
            return result;
        }

        private Tuple<List<bool>, List<List<IEnumerable<Triple>>>> getCorrectValues(List<Triple> testValues, IGraph graph)
        {
            var existsResults = new List<bool>();
            var result = new List<List<IEnumerable<Triple>>>();
            foreach (var t in testValues)
            {
                var testResults = new List<IEnumerable<Triple>>(7);
                existsResults.Add(graph.Triples.FirstOrDefault(t) != null);
                testResults.Add(graph.GetTriplesWithSubjectObject(t.Subject, t.Object).Sort());
                testResults.Add(graph.GetTriplesWithSubject(t.Subject).Sort());
                testResults.Add(graph.GetTriplesWithObject(t.Object).Sort());
                testResults.Add(graph.GetTriplesWithPredicate(t.Predicate).Sort());
                testResults.Add(graph.GetTriplesWithPredicateObject(t.Predicate, t.Object).Sort());
                testResults.Add(graph.GetTriplesWithSubjectPredicate(t.Subject, t.Predicate).Sort());
                testResults.Add(graph.Triples.Sort());
                result.Add(testResults);             
            }
            return new Tuple<List<bool>, List<List<IEnumerable<Triple>>>>(existsResults, result);
        }

        private double GetExecutionTime<T>(ref T result, Func<T> method)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            result = method();
            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            return elapsedMs;
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
