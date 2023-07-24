// See https://aka.ms/new-console-template for more information
using k2extensionsLib;
using Lucene.Net.Util;
using System.Drawing.Text;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using VDS.RDF;

//BigInteger b = new BigInteger(new byte[] { 255, 0, 123, 0, 123, 0 });
//BigInteger b2 = new BigInteger(528289038591);
//BigInteger b3 = new BigInteger(280377528711936);
//var t = b.ToByteArray();
//var t2 = b2.ToByteArray();
//var t3 = b3.ToByteArray();


string[] files = new string[]{
    "TestData//btc2019-lu.se_00001.nq.gz",
    "TestData//btc2019-uba.de_00001.nq.gz",
    "TestData//btc2019-drugbank.ca_00001.nq.gz",
    "TestData//btc2019-l3s.de_00001.nq.gz",   
};
bool usek2Triples = true;

foreach (string file in files)
{
    try
    {
        string result = Tester.TestExtensions(new List<IK2Extension>() { new K3(2), new MK2(2), new K2ArrayIndexPositional(2), new K2ArrayIndexK2(2) }, file, usek2Triples);
        Tester.PrintCSVTable(result);
    }
    catch { }
}

Console.ReadKey();


