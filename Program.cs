using System.Dynamic;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CSharp.RuntimeBinder;

// Demo application
// Note: tested - works on both Windows and Linux

namespace ScriptRunner;

public class TestClass
{
    public async Task Write1(string message)
    {
        await Task.Delay(1);
        Console.WriteLine($"Message: '{message}'");
    }

    public void Write2(OtherClass obj, Type t)
    {
        Console.WriteLine($"Message: '{obj.GetMessage(t)}'");
    }
}

public class OtherClass
{
    public string GetMessage(Type t)
    {
        if (t == typeof(string))
        {
            return "OK";
        }
        else
        {
            return "Not OK";
        }

    }
}

public class Program
{
    public static void Main()
    {
        // should contain single public class with dynamic injections param and public method 'Process' without params
        string codeToCompile = @"
            using System;

            public class ScriptProcessor(dynamic injections)
            {
                public void Process()
                {
                    injections.TestClass.Write1(injections.Message);
                    injections.TestClass.Write2(injections.OtherClass, injections.SomeType);
                }
            }
            ";

        var refPaths = new[]
        {
            typeof(object).GetTypeInfo().Assembly.Location,
            typeof(Enumerable).GetTypeInfo().Assembly.Location,
            typeof(DynamicObject).Assembly.Location,
            typeof(RuntimeBinderException).Assembly.Location,
            Path.Combine(Path.GetDirectoryName(typeof(object).GetTypeInfo().Assembly.Location)!, "System.Runtime.dll")
        };
        var references = refPaths.Select(r => MetadataReference.CreateFromFile(r));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var syntaxTree = CSharpSyntaxTree.ParseText(codeToCompile);
        var compilation = CSharpCompilation.Create(Path.GetRandomFileName(), [syntaxTree], references, options);

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            Console.WriteLine("Compilation failed");

            var failures = result.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);
            foreach (var diagnostic in failures)
            {
                Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
            }
        }
        else
        {
            Console.WriteLine("Compilation successful");

            try
            {
                ms.Seek(0, SeekOrigin.Begin);
                var assembly = AssemblyLoadContext.Default.LoadFromStream(ms);
                var type = assembly.ExportedTypes.First();

                var injections = new ExpandoObject() as dynamic;
                injections.Message = "HELLO!";
                injections.SomeType = typeof(string);
                injections.TestClass = new TestClass();
                injections.OtherClass = new OtherClass() as object;

                var instance = assembly.CreateInstance(type.Name, false, BindingFlags.Public | BindingFlags.Instance, null, [injections], null, null);
                var method = type.GetMethod("Process", BindingFlags.Public | BindingFlags.Instance)!;
                method.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Execution failed");
                var exception = ex;
                while (exception != null)
                {
                    Console.Error.WriteLine("{0}: {1}", exception.GetType().FullName, exception.Message);
                    exception = exception.InnerException;
                }
            }
        }
    }
}