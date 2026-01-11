#r "C:\\Programming\\CSharp\\Discord Music Bot\\CCMusique\\bin\\Debug\\net8.0\\CCTavern.dll"

using System;
using System.IO;
using System.Reflection;
using CCTavern;

var folder = @"C:\Programming\CSharp\Discord Music Bot\CCMusique\bin\Debug\net8.0";

// Load all DLLs and EXEs
foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly))
{
    if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            Assembly.LoadFrom(file);
            Console.WriteLine($"Loaded: {Path.GetFileName(file)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load {file}: {ex.Message}");
        }
    }
}

Console.WriteLine("All assemblies loaded.");

// Load environment.

System.Environment.CurrentDirectory = folder;
CCTavern.Program.SetupEnvironment(false);