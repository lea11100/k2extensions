using J2N.Numerics;
using Lucene.Net.Diagnostics;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Shacl.Validation;

namespace k2extensionsLib
{
    public class Tester
    {
        public static string TestExtensions(List<IK2Extension> extensionsUnderTest, string fileName, bool useK2Triples)
        {
            Console.WriteLine("Started");
            //IGraph g = new Graph();
            //g.LoadFromFile(fileName);
            TripleStore g = new ();
            g.LoadFromFile(fileName);
            Console.WriteLine("Graph loaded:");
            string graphInfo = "Size;Triples;Subjects;Predicates;Objects\r\n";
            graphInfo += new FileInfo(fileName).Length + " Bytes;" 
                + g.Triples.Count() + ";" 
                + g.Triples.Select(x=>x.Subject).Distinct().Count() + ";"
                + g.Triples.Select(x => x.Predicate).Distinct().Count() + ";"
                + g.Triples.Select(x => x.Object).Distinct().Count() + ";";

            PrintCSVTable(graphInfo);

            var testValues = new List<Triple>();
            var r = new Random();
            var s = g.Triples.Select(x => x.Subject).Distinct();
            var o = g.Triples.Select(x => x.Object).Distinct();
            var p = g.Triples.Select(x => x.Predicate).Distinct();
            for (int i = 0; i < 50; i++)
            {
                testValues.Add(g.Triples.ElementAt(r.Next(g.Triples.Count())));
            }
            for (int i = 0; i < 150; i++)
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
            string result = "Name;Compression;SPO;SP?O;SP?O?;S?P?O;S?PO?;S?PO;SPO?;S?P?O?;Compression size\r\n";
            var timeResults = new List<List<int>>();
            for (int i = 0; i < 7; i++)
            {
                timeResults.Add(new List<int>());
            }
            Console.WriteLine("Tests initialized");
            for (int i = 0; i < extensionsUnderTest.Count; i++)
            {
                int exceptionCounter = 0;
                var ext = extensionsUnderTest[i];
                result += ext.GetType().Name + ";";
                Console.WriteLine("Start testing " + ext.GetType().Name);
                result += GetExecutionTime(()=>ext.Compress(g, useK2Triples)) + " ms;";
                Console.WriteLine("Compression finished");
                IEnumerable<Triple> res = new List<Triple>();
                for (int j =0; j < testValues.Count; j++)
                {
                    try
                    {
                        Triple t = testValues[j];
                        bool resExists = false;
                        timeResults[0].Add(GetExecutionTime(ref resExists, () => ext.Exists(t.Subject, t.Predicate, t.Object)));
                        Assert.AreEqual(testResExists[j], resExists);
                        timeResults[1].Add(GetExecutionTime(ref res, () => ext.Connections(t.Subject, t.Object)));
                        Assert.AreEqual(testRes[j][0], res.Sort());
                        timeResults[2].Add(GetExecutionTime(ref res, () => ext.Succ(t.Subject)));
                        Assert.AreEqual(testRes[j][1], res.Sort());
                        timeResults[3].Add(GetExecutionTime(ref res, () => ext.Prec(t.Object)));
                        Assert.AreEqual(testRes[j][2], res.Sort());
                        timeResults[4].Add(GetExecutionTime(ref res, () => ext.AllEdgesOfType(t.Predicate)));
                        Assert.AreEqual(testRes[j][3], res.Sort());
                        timeResults[5].Add(GetExecutionTime(ref res, () => ext.PrecOfType(t.Object, t.Predicate)));
                        Assert.AreEqual(testRes[j][4], res.Sort());
                        timeResults[6].Add(GetExecutionTime(ref res, () => ext.SuccOfType(t.Subject, t.Predicate)));
                        Assert.AreEqual(testRes[j][5], res.Sort());
                        Console.Write($"\r{j + 1}/{testValues.Count} finished");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                        exceptionCounter++;
                        if(exceptionCounter == 10)
                        {
                            break;
                        }
                    }
                }
                Console.WriteLine($"\r\nQueries finished");
                timeResults.ForEach(x => result += string.Format("{0:0.000}", Math.Round(x.Average(), 3)) + " ms;");
                result += GetExecutionTime(ref res, ext.Decomp) + " ms;";
                Console.WriteLine($"Decompression finished");
                Assert.AreEqual(res.Sort(), g.Triples.Sort());
                result += ext.StorageSpace +"\r\n";
                ext = null;
            }
            return result;
        }

        public static void PrintCSVTable(string csvString)
        {
            string[] rows = csvString.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (rows.Length == 0)
            {
                Console.WriteLine("Empty CSV string.");
                return;
            }

            int columnCount = rows[0].Split(';').Length;

            // Calculate the maximum width for each column
            int[] columnWidths = new int[columnCount];
            for (int i = 0; i < rows.Length; i++)
            {
                string[] columns = rows[i].Split(';');
                for (int j = 0; j < columnCount; j++)
                {
                    if (j < columns.Length)
                    {
                        int columnLength = columns[j].Length;
                        if (columnLength > columnWidths[j])
                            columnWidths[j] = columnLength;
                    }
                }
            }

            // Print the table
            for (int i = 0; i < rows.Length; i++)
            {
                string[] columns = rows[i].Split(';');
                for (int j = 0; j < columnCount; j++)
                {
                    if (j < columns.Length)
                    {
                        string column = columns[j].PadRight(columnWidths[j]);
                        Console.Write(column);
                    }
                    Console.Write("  "); // Add some spacing between columns
                }
                Console.WriteLine();
            }
        }

        private static Tuple<List<bool>, List<List<IEnumerable<Triple>>>> getCorrectValues(List<Triple> testValues, TripleStore graph)
        {
            var existsResults = new List<bool>();
            var result = new List<List<IEnumerable<Triple>>>();
            foreach (var t in testValues)
            {
                var testResults = new List<IEnumerable<Triple>>(7);
                existsResults.Add(graph.Triples.FirstOrDefault(x=>x.Equals(t))!=null);
                testResults.Add(graph.GetTriplesWithSubjectObject(t.Subject, t.Object).Distinct().Sort());
                testResults.Add(graph.GetTriplesWithSubject(t.Subject).Distinct().Sort());
                testResults.Add(graph.GetTriplesWithObject(t.Object).Distinct().Sort());
                testResults.Add(graph.GetTriplesWithPredicate(t.Predicate).Distinct().Sort());
                testResults.Add(graph.GetTriplesWithPredicateObject(t.Predicate, t.Object).Distinct().Sort());
                testResults.Add(graph.GetTriplesWithSubjectPredicate(t.Subject, t.Predicate).Distinct().Sort());
                result.Add(testResults);             
            }
            return new Tuple<List<bool>, List<List<IEnumerable<Triple>>>>(existsResults, result);
        }

        private static int GetExecutionTime<T>(ref T result, Func<T> method)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            result = method();
            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            return (int)elapsedMs;
        }

        private static double GetExecutionTime(Action method)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            method();
            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            return elapsedMs;
        }
    }
}
