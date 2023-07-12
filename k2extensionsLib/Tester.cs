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

            var testTripels = new List<Triple>();
            var r = new Random();
            var s = g.Triples.Select(x => x.Subject).Distinct();
            var o = g.Triples.Select(x => x.Object).Distinct();
            var p = g.Triples.Select(x => x.Predicate).Distinct();
            for (int i = 0; i < 50; i++)
            {
                testTripels.Add(g.Triples.ElementAt(r.Next(g.Triples.Count())));
            }
            for (int i = 0; i < 150; i++)
            {
                testTripels.Add(new Triple(
                    s.ElementAt(r.Next(s.Count())),
                    p.ElementAt(r.Next(p.Count())),
                    o.ElementAt(r.Next(o.Count()))
                    ));
            }
            s = null; p = null; o = null;
            var correctValues = getCorrectValues(testTripels, g);
            string result = "Name;Compression;SPO;SP?O;SP?O?;S?P?O;S?PO?;S?PO;SPO?;S?P?O?;Compression size\r\n";
            
            Console.WriteLine("Tests initialized");
            foreach(var ext in extensionsUnderTest)
            {
                int exceptionCounter = 0;
                result += ext.GetType().Name + ";";
                Console.WriteLine("Start testing " + ext.GetType().Name);
                result += GetExecutionTime(()=>ext.Compress(g, useK2Triples)) + " ms;";
                Console.WriteLine("Compression finished");
                IEnumerable<Triple> res = new List<Triple>();
                var timeResults = new List<List<long>>();
                for (int j = 0; j < 7; j++)
                {
                    timeResults.Add(new List<long>());
                }
                for (int j =0; j < testTripels.Count; j++)
                {
                    try
                    {
                        Triple t = testTripels[j];
                        timeResults[0].Add(GetExecutionTime(ref res, () => ext.Exists(t.Subject, t.Predicate, t.Object)));
                        Assert.AreEqual(correctValues[j][0], res.Sort());
                        timeResults[1].Add(GetExecutionTime(ref res, () => ext.Connections(t.Subject, t.Object)));
                        Assert.AreEqual(correctValues[j][1], res.Sort());
                        timeResults[2].Add(GetExecutionTime(ref res, () => ext.Succ(t.Subject)));
                        Assert.AreEqual(correctValues[j][2], res.Sort());
                        timeResults[3].Add(GetExecutionTime(ref res, () => ext.Prec(t.Object)));
                        Assert.AreEqual(correctValues[j][3], res.Sort());
                        timeResults[4].Add(GetExecutionTime(ref res, () => ext.AllEdgesOfType(t.Predicate)));
                        Assert.AreEqual(correctValues[j][4], res.Sort());
                        timeResults[5].Add(GetExecutionTime(ref res, () => ext.PrecOfType(t.Object, t.Predicate)));
                        Assert.AreEqual(correctValues[j][5], res.Sort());
                        timeResults[6].Add(GetExecutionTime(ref res, () => ext.SuccOfType(t.Subject, t.Predicate)));
                        Assert.AreEqual(correctValues[j][6], res.Sort());
                        Console.Write($"\r{j + 1}/{testTripels.Count} finished");
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
                Assert.AreEqual(g.Triples.Distinct().Sort(),res.Sort());
                result += ext.StorageSpace +"\r\n";
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

        private static List<List<IEnumerable<Triple>>> getCorrectValues(List<Triple> testValues, TripleStore graph)
        {
            var result = new List<List<IEnumerable<Triple>>>();
            foreach (var t in testValues)
            {
                var testResults = new List<IEnumerable<Triple>>()
                {
                    graph.Triples.Where(x => x.Equals(t)).Distinct().Sort(),
                    graph.GetTriplesWithSubjectObject(t.Subject, t.Object).Distinct().Sort(),
                    graph.GetTriplesWithSubject(t.Subject).Distinct().Sort(),
                    graph.GetTriplesWithObject(t.Object).Distinct().Sort(),
                    graph.GetTriplesWithPredicate(t.Predicate).Distinct().Sort(),
                    graph.GetTriplesWithPredicateObject(t.Predicate, t.Object).Distinct().Sort(),
                    graph.GetTriplesWithSubjectPredicate(t.Subject, t.Predicate).Distinct().Sort()
                };
                result.Add(testResults);             
            }
            return new List<List<IEnumerable<Triple>>>(result);
        }

        private static long GetExecutionTime<T>(ref T result, Func<T> method)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            result = method();
            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            return (int)elapsedMs;
        }

        private static long GetExecutionTime(Action method)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            method();
            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            return elapsedMs;
        }
    }
}
