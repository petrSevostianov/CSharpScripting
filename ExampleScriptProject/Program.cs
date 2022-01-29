using System;
using System.IO;
using System.Runtime.CompilerServices;

class Program {

    public static string GetThisFilePath([CallerFilePath] string path = "") {
        return path;
    }

    static void Main() {
        //Console.Clear();
        /*var files = Directory.GetFiles(Path.GetDirectoryName(GetThisFilePath()), "*.cs", SearchOption.AllDirectories);
        foreach (var i in files) {
            Console.WriteLine(i);
        }*/
        Console.WriteLine("Hello!");
    }
}
