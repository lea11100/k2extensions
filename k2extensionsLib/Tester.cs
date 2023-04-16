using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace k2extensionsLib
{
    internal class Tester
    {
        internal void TestExtensions(List<IK2Extension> extensionsUnderTest, string fileName, bool useK2Triples)
        {
            AdjacencyMatrixWithLabels matrix;
            if (useK2Triples) 
                matrix = FileReader.ConvertFromFileUsingK2Triples(fileName);   
            else           
                matrix = FileReader.ConvertFromFile(fileName);

            foreach (var ext in extensionsUnderTest)
            {
                //TODO Test methods
            }
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
