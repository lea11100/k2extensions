// See https://aka.ms/new-console-template for more information
using k2extensionsLib;

Console.WriteLine("Hello, World!");
new Tester().TestExtensions(new List<IK2Extension>() { new K2ArrayIndex(2), new K3(2)}, "TestData//about.rdf", false);
