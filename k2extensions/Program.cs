// See https://aka.ms/new-console-template for more information
using k2extensionsLib;

Console.WriteLine("Hello, World!");
new Tester().TestExtensions(new List<IK2Extension>() { new k2ArrayIndex(2)}, "TestData//about.rdf", true);
