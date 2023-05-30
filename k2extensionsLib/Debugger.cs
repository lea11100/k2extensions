using NUnit.Framework.Internal.Execution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;

namespace k2extensionsLib
{
    internal class Debugger
    {
        internal static void printMatrix(IEnumerable<Triple> triples, IEnumerable<INode> Subjects, IEnumerable<INode> Objects)
        {
            var group = triples.GroupBy(x => x.Subject).ToDictionary(x => x.Key, x => x.Select(y => y.Object).ToList());
            foreach (var s in Subjects)
            {
                bool[] output = new bool[Objects.Count()];
                for (int i = 0; i < Objects.Count(); i++)
                {
                    List<INode> l;
                    if (group.TryGetValue(s, out l))
                    {
                        output[i] = l.Contains(Objects.ElementAt(i));
                    }
                }
                Console.WriteLine(string.Join(" ", output.Select(x => x ? "1" : "0")));
            }
        }

        internal static string getMatrix(List<Triple> triples, IEnumerable<INode> Subjects, IEnumerable<INode> Objects)
        {
            var group = triples.GroupBy(x => x.Subject).ToDictionary(x => x.Key, x => x.Select(y => y.Object).ToList());
            var result = "";
            foreach (var s in Subjects)
            {
                bool[] output = new bool[Objects.Count()];
                for (int i = 0; i < Objects.Count(); i++)
                {
                    List<INode> l;
                    if (group.TryGetValue(s, out l))
                    {
                        output[i] = l.Contains(Objects.ElementAt(i));
                    }
                }
                result += string.Join(" ", output.Select(x => x ? "1" : "0")) + "\r\n";
            }
            return result;
        }

        public static void PrintTree(TreeNode root)
        {
            PrintTree(root, 0);
        }

        private static void PrintTree(TreeNode node, int level)
        {
            Console.Write($"{GetIndentation(level)}{(node != null ? "1" : "0")}\r\n");
            if (node == null)
                return;
            for (int i = 0; i < 4; i++)
            {
                Console.Write($"{GetIndentation(level + 1)}");
                if (i == 3)
                    Console.Write("└");
                else
                    Console.Write("├");
                PrintTree(node.Children[i], level + 1);
            }
        }

        private static string GetIndentation(int level)
        {
            string s= "";
            for (int i = 0; i < level; i++)
            {
                s += "|    ";
            }
            return s;
        }
    }
}
