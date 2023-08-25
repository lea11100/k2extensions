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
    /// <summary>
    /// Class for testing an extension
    /// </summary>
    public class Tester
    {
        /// <summary>
        /// Tests extensions using the given data set
        /// </summary>
        /// <param name="extensionsUnderTest">Extensions that needs to be tested</param>
        /// <param name="fileName">Name of the file contianing the data set</param>
        /// <param name="useK2Triples">Indicates if the extension should use k^2-triples </param>
        /// <returns>Returns an CSV wih the results</returns>
        public static string TestExtensions(List<IK2Extension> extensionsUnderTest, string fileName, bool useK2Triples)
        {
            Console.WriteLine("Start testing: " + fileName);
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
            var s = g.Triples.Select(x => x.Subject).Distinct().ToArray();
            var o = g.Triples.Select(x => x.Object).Distinct().ToArray();
            var p = g.Triples.Select(x => x.Predicate).Distinct().ToArray();
            int numberOfTripels = g.Triples.Count();
            Triple[] triples = g.Triples.ToArray();
            for (int i = 0; i < 100; i++)
            {
                testTripels.Add(triples[r.Next(numberOfTripels)]);
            }
            int numberOfSubjects = s.Length, numberOfPredicates = p.Length, numberOfObjects = o.Length;
            for (int i = 0; i < 400; i++)
            {
                testTripels.Add(new Triple(
                    s[r.Next(numberOfSubjects)],
                    p[r.Next(numberOfPredicates)],
                    o[r.Next(numberOfObjects)]
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

        /// <summary>
        /// Prints an CSV-table to the console
        /// </summary>
        /// <param name="csvString">data in CSV format</param>
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

        /// <summary>
        /// Calculates the correct values for the given triples on the given graph
        /// </summary>
        /// <param name="testValues">Test triples</param>
        /// <param name="graph"></param>
        /// <returns>Each triple gets a list of lists containing the results for each request type</returns>
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

        /// <summary>
        /// Measure the execution time for a method an store the results of the method
        /// </summary>
        /// <typeparam name="T">Type of the result</typeparam>
        /// <param name="result">Container for storing the result</param>
        /// <param name="method">Method which execution time is measured</param>
        /// <returns>Elapsed milliseconds for executing the method</returns>
        private static long GetExecutionTime<T>(ref T result, Func<T> method)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            result = method();
            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            return (int)elapsedMs;
        }

        /// <summary>
        /// Measure the execution time for a method that does not return anything
        /// </summary>
        /// <param name="method">Method which execution time is measured</param>
        /// <returns>Elapsed milliseconds for executing the method</returns>
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
