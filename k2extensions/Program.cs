// See https://aka.ms/new-console-template for more information
using k2extensionsLib;
using VDS.RDF;

//new Tester().TestExtensions(new List<IK2Extension>() { new K3(2), new K2ArrayIndex(2), new MK2(2)}, "TestData//about.rdf", false);
//new Tester().TestExtensions(new List<IK2Extension>() { new MK2(2) }, "TestData//ontology.rdf", false);
new Tester().TestExtensions(new List<IK2Extension>() { new K3(2)}, "TestData//test.rdf", true);

