// See https://aka.ms/new-console-template for more information
using k2extensionsLib;




string[] files = new string[]{
    "TestData//btc2019-lu.se_00001.nq.gz",
    //"TestData//btc2019-drugbank.ca_00001.nq.gz",
    //"TestData//btc2019-ordnancesurvey.co.uk_00001.nq.gz",
    "TestData//btc2019-uba.de_00001.nq.gz",
    "TestData//btc2019-l3s.de_00001.nq.gz",
};
bool usek2Triples = false;
int k = 2;
string outFile = "result.csv";


using (var fs = new StreamWriter(outFile, false)) {

    foreach (string file in files)
    {
        try
        {
            string result = Tester.TestExtensions(new List<IK2Extension>() { new MK2(k), new K3(k), new LeafRankV1(k), new LeafRankK2(k) }, file, usek2Triples);
            fs.Write(result);
            Tester.PrintCSVTable(result);
        }
        catch (Exception e) { Console.WriteLine(e.Message + "\r\n" + e.StackTrace); }
    }
}

Console.ReadKey();


