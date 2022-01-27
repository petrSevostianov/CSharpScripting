using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

public class Source {
    public string Path { get; init; }
    public SourceText SourceText { get; init; }

    public Source(string path, string? code = null) {
        Path = path;
        if (code == null) {
            var fileContent = File.ReadAllBytes(path);
            SourceText = SourceText.From(fileContent, fileContent.Length, canBeEmbedded: true);
        } else {
            SourceText = SourceText.From(code, encoding: Encoding.UTF8);
        }
    }

    public SyntaxTree GetSyntaxTree() {

        
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText,
            new CSharpParseOptions().WithKind(SourceCodeKind.Regular), path: Path);
        return syntaxTree;
    }

}

public class Engine: IEnumerable{

    public class CollectibleAssemblyLoadContext : System.Runtime.Loader.AssemblyLoadContext {
        public CollectibleAssemblyLoadContext(string? name) : base(name, isCollectible: true) {}
        protected override Assembly? Load(AssemblyName name) {
            return null;
        }
    }

    public CollectibleAssemblyLoadContext? AssemblyLoadContext { get; protected set; }
    public Assembly? Assembly { get; protected set; }


    public List<Source> Sources { get; init; } = new List<Source>();

    public void Add(Source source) {
        Sources.Add(source);
    }

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

    protected bool Compile(bool debug = false) {
        OptimizationLevel optimizationLevel = debug ? OptimizationLevel.Debug : OptimizationLevel.Release;
        CSharpCompilationOptions CompilationOptions =
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: optimizationLevel);

        var compilation = CSharpCompilation.Create(
            "Project.dll",
            Sources.Select(x => x.GetSyntaxTree()),
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
            return false;

        peStream.Seek(0, SeekOrigin.Begin);
        pdbStream?.Seek(0, SeekOrigin.Begin);

        AssemblyLoadContext = new CollectibleAssemblyLoadContext("projectContext");
        Assembly = AssemblyLoadContext.LoadFromStream(peStream, pdbStream);

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Run(bool debug = false) {

        if (!Compile(debug)) return int.MinValue;

        //var main = Assembly?.EntryPoint;

        var projectType = Assembly?.GetType("Project");
        var main = projectType?.GetMethod("Main");
        if (main == null) {            
            return int.MinValue;
        }
        
        var result = main.Invoke(null, null);
        return Convert.ToInt32(result);
    }

    private void ProcessEmitResult(EmitResult emitResult) {
        foreach (var i in emitResult.Diagnostics) {
            Console.WriteLine(i);
        }
    }

    public IEnumerator GetEnumerator() {
        return Sources.GetEnumerator(); 
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
    static WeakReference RunAndUnload(params Source[] sources) {

        var engine = new Engine();

        foreach (var i in sources) {
            engine.Add(i);
        }


        engine.Run(true);

        return engine.Unload();
    }

    static void Main(string[] args) {

        var directory = Path.GetDirectoryName(GetThisFilePath());
        var sourceCodePath = Path.Combine(directory, "Program.script");

        WeakReference weakReference = RunAndUnload(new Source(sourceCodePath));


        var gcIteration = 0;
        while (weakReference.IsAlive) {
            gcIteration++;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Console.WriteLine(gcIteration);

    }
}