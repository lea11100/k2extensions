// See https://aka.ms/new-console-template for more information
using k2extensionsLib;
using Lucene.Net.Util;
using System.Drawing.Text;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using VDS.RDF;

string fileName = args.Length==1 ? args[0] : "TestData//btc2019-acropolis.org.uk_00001.nq.gz";
bool usek2Triples = args.Length == 2 && bool.Parse(args[1]);

string result = Tester.TestExtensions(new List<IK2Extension>() { new K3(2), new K2ArrayIndexPositional(2), new K2ArrayIndexK2(2), new MK2(2), }, fileName, usek2Triples);
//string result = Tester.TestExtensions(new List<IK2Extension>() { new K2ArrayIndexPositional(2)}, fileName, usek2Triples);

Tester.PrintCSVTable(result);



