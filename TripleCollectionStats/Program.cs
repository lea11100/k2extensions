// See https://aka.ms/new-console-template for more information
using VDS.RDF;

var res = "Size;Triples;Subjects;Predicates;Objects\r\n";
foreach (var f in Directory.GetFiles("D:\\BTC", "*.nq.gz"))
{
    try
    {
        if (new FileInfo(f).Length > 50000000) continue;
        TripleStore g = new();
        g.LoadFromFile(f);
        Console.WriteLine("Graph loaded: " + f);
        res += f + ";"
            + new FileInfo(f).Length + " Bytes;"
            + g.Triples.Count() + ";"
            + g.Triples.Select(x => x.Subject).Distinct().Count() + ";"
            + g.Triples.Select(x => x.Predicate).Distinct().Count() + ";"
            + g.Triples.Select(x => x.Object).Distinct().Count() + ";\r\n";
        g = null;
        
    }
    catch { }
}

PrintCSVTable(res);
File.WriteAllText("BTSStats.csv", res);
Console.ReadKey();

static void PrintCSVTable(string csvString)
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