using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;



public class Source {
    public string Path { get; init; }
    public SourceText SourceText { get; init; }

    public DateTime LastWriteTime;

    public Source(string path, string? code = null) {
        Path = path;

        if (File.Exists(path)) {
            LastWriteTime = GetCurrentLastWriteTime();
        }

        if (code == null) {
            byte[] fileContent = { };
            try {
                fileContent = File.ReadAllBytes(path);
            }
            catch (Exception e) { 
                Console.WriteLine(e.GetType());
                
            }


            SourceText = SourceText.From(fileContent, fileContent.Length, canBeEmbedded: true);
        } else {
            SourceText = SourceText.From(code, encoding: Encoding.UTF8);
        }
    }

    public bool Changed() {
        if (LastWriteTime == GetCurrentLastWriteTime()) {

            return true;
        }
        return false;
    }

    public DateTime GetCurrentLastWriteTime() { 
        return File.GetLastWriteTimeUtc(Path);
    }

    public SyntaxTree GetSyntaxTree() {

        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText,
            new CSharpParseOptions().WithKind(SourceCodeKind.Regular), path: Path);

        //PrintSyntaxNode(syntaxTree.GetRoot());


        return syntaxTree;
    }

    /*public void PrintSyntaxNode(SyntaxNode syntaxNode, int indent = 0) {
        string name = "**************";
        if (syntaxNode is IdentifierNameSyntax identifierNameSyntax) {
            name = identifierNameSyntax.ToString();
        }

        if (syntaxNode is MemberAccessExpressionSyntax memberAccessExpressionSyntax) {
            name = memberAccessExpressionSyntax.ToString();
        }

        //Microsoft.CodeAnalysis.CSharp.Syntax.
        //ComplexElementInitializerSyntax



        Console.WriteLine($"{new String('\t',indent)}type:{syntaxNode.GetType().Name}  kind: {syntaxNode.Kind()} name: {name}");
        foreach (var n in syntaxNode.ChildNodes()) {
            PrintSyntaxNode(n, indent + 1);
        }
    }*/


}

public class Engine{

    public class CollectibleAssemblyLoadContext : System.Runtime.Loader.AssemblyLoadContext {
        public CollectibleAssemblyLoadContext(string? name) : base(name, isCollectible: true) {}
        protected override Assembly? Load(AssemblyName name) {
            return null;
        }
    }

    public CollectibleAssemblyLoadContext? AssemblyLoadContext { get; protected set; }
    public Assembly? Assembly { get; protected set; }


    /*public List<Source> Sources { get; init; } = new List<Source>();

    public void Add(Source source) {
        Sources.Add(source);
    }*/

    public static IEnumerable<MetadataReference> CollectReferences() {
        var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        yield return mscorlib;

        var referencedAssemblies = Assembly.GetExecutingAssembly().GetReferencedAssemblies();
        foreach (var referencedAssembly in referencedAssemblies) {
            var assembly = Assembly.Load(referencedAssembly);

            //MetadataReference.
            var metadataReference = MetadataReference.CreateFromFile(assembly.Location);
            yield return metadataReference;
        }
    }

    public void Compile(Source[] sources, bool debug = false) {
        OptimizationLevel optimizationLevel = debug ? OptimizationLevel.Debug : OptimizationLevel.Release;
        CSharpCompilationOptions CompilationOptions =
            new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                optimizationLevel: optimizationLevel);

        var compilation = CSharpCompilation.Create(
            "Project.dll",
            sources.Select(x => x.GetSyntaxTree()).ToArray(),
            CollectReferences(),
            CompilationOptions);

        using var peStream = new MemoryStream();
        using var pdbStream = debug ? new MemoryStream() : null;


        var emitResult = compilation.Emit(
            peStream: peStream,
            pdbStream: pdbStream
            );

        ProcessEmitResult(emitResult);

        if (!emitResult.Success)
            throw new Exception("Compilation failed.");

        peStream.Seek(0, SeekOrigin.Begin);
        pdbStream?.Seek(0, SeekOrigin.Begin);

        AssemblyLoadContext = new CollectibleAssemblyLoadContext("projectContext");
        Assembly = AssemblyLoadContext.LoadFromStream(peStream, pdbStream);
    }

    public enum RunResult { 
        SourcesChanged,
        Finished,
        Error
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Run(Func<bool> sourcesChanged) {


        var entryPoint = Assembly?.EntryPoint;

        //var projectType = Assembly?.GetType("Project");
        //var entryPoint = projectType?.GetMethod("Main");
        if (entryPoint == null) {
            throw new Exception("Entry point not found");
        }

        var programCancellationToken = entryPoint.DeclaringType?.GetField("ProgramCancellationToken");

        CancellationTokenSource? cancellationTokenSource = null;

        if (programCancellationToken != null) {
            cancellationTokenSource = new CancellationTokenSource();
            programCancellationToken.SetValue(null, cancellationTokenSource.Token);
        }


        object? result = null;

        Thread thread = new Thread(() => { 
            result = entryPoint.Invoke(null, null);
        });
        thread.Start();

        while (thread.IsAlive) {
            if (sourcesChanged()) return;

            /*foreach (var i in Sources) {
                if (i.LastWriteTime != i.GetCurrentLastWriteTime()) {
                    cancellationTokenSource?.Cancel();
                    thread.Join();
                    return RunResult.SourcesChanged;
                }            
            }*/
            Thread.Yield();
        }

        //return RunResult.Finished;
    }

    

    private void ProcessEmitResult(EmitResult emitResult) {
        foreach (var i in emitResult.Diagnostics) {
            /*if (i.Severity == DiagnosticSeverity.Error) {
                Debug.Fail(i.GetMessage());
            }*/
            Console.WriteLine(i);
        }
    }


    public WeakReference Unload() {
        var weakAssemblyLoadContext = new WeakReference(AssemblyLoadContext);
        AssemblyLoadContext?.Unload();
        return weakAssemblyLoadContext;        
    }

}



class Program {


    public static string GetThisFilePath([CallerFilePath] string path = "") {
        return path;    
    }


    


    private static readonly IEnumerable<MetadataReference> DefaultReferences = Engine.CollectReferences();

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference RunAndUnload(Func<bool> sourcesChanged, params Source[] sources) {
        WeakReference result;
        var engine = new Engine();
        try {
            engine.Compile(sources, true);
            engine.Run(sourcesChanged);
        }
        catch (Exception e) { 
        
        }
        finally {
            result = engine.Unload();
        }
        return result;

    }



    /*static void WaitForFilesReadyForRead(string[] files) {
        while (true) {
            foreach (var i in files) { 
                File.op
            }
        }
    }*/



    static void Main(string[] args) {
        if (args.Length < 1) {
            Console.Error.WriteLine("Solution directory is expected as the first argument.");
            return;
        }
        var directory = args[0];
        if (!Directory.Exists(directory)) {
            Console.Error.WriteLine($"Directory <{directory}> not found");
            return;
        }

        string[] collectFiles() { 
            return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
        }


        

        Source[] sources = collectFiles().Select(x => new Source(x)).ToArray();

        bool sourcesChanged() {
            var files = collectFiles();

            if (files.Length != sources.Length)
                return true;

            foreach (var i in sources) {
                if (i.LastWriteTime != i.GetCurrentLastWriteTime()) {
                    return true;
                }
            }
            return false;
        }


        //Engine.RunResult runResult = Engine.RunResult.SourcesChanged;

        while (true) {

            var csFiles = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);


            WeakReference weakReference = RunAndUnload(sourcesChanged,sources);

            var gcIteration = 0;
            while (weakReference.IsAlive) {
                gcIteration++;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Console.WriteLine($"Assembly unloaded with {gcIteration} GC.Collect()s");



            while (!sourcesChanged()) {
                Thread.Yield();
            }


            sources = collectFiles().Select(x => new Source(x)).ToArray();


        }

    }
}