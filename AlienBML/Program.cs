// Alien Isolation (Binary XML converter)
// Written by WRS (xentax.com)

using System;

namespace CathodeLib
{
    class Program
    {
        static void Main(string[] args)
        {
            string filename_src = null;
            string filename_dst = null;
            if (args.Length == 1 || args.Length == 2) filename_src = args[0];
            if (args.Length == 2) filename_dst = args[1];

            if (filename_src == null)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("\tAlienBML.exe source_file <target_file>");
                Console.WriteLine("\t source_file   A BML or XML filename");
                Console.WriteLine("\t target_file   A BML or XML filename (optional)");
            }
            else
            {
                new AlienBML(filename_src, filename_dst).Run();
            }
        }
    }
}
