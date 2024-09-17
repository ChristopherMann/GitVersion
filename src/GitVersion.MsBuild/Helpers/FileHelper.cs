using System.Text.RegularExpressions;
using GitVersion.Core;
using GitVersion.Helpers;
using Microsoft.Build.Framework;

namespace GitVersion.MsBuild;

internal static class FileHelper
{
    public static readonly string TempPath = MakeAndGetTempPath();

    private static string MakeAndGetTempPath()
    {
        var tempPath = PathHelper.Combine(Path.GetTempPath(), "GitVersionTask");
        Directory.CreateDirectory(tempPath);
        return tempPath;
    }

    public static void DeleteTempFiles()
    {
        if (!Directory.Exists(TempPath))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(TempPath))
        {
            if (File.GetLastWriteTime(file) >= DateTime.Now.AddDays(-1))
                continue;
            try
            {
                File.Delete(file);
            }
            catch (UnauthorizedAccessException)
            {
                //ignore contention
            }
        }
    }

    public static string GetFileExtension(string language) => language switch
    {
        "C#" => "cs",
        "F#" => "fs",
        "VB" => "vb",
        _ => throw new ArgumentException($"Unknown language detected: '{language}'")
    };

    public static string GetProjectExtension(string language) => language switch
    {
        "C#" => "csproj",
        "F#" => "fsproj",
        "VB" => "vbproj",
        _ => throw new ArgumentException($"Unknown language detected: '{language}'")
    };

    public static void CheckForInvalidFiles(IEnumerable<ITaskItem> compileFiles, string projectFile)
    {
        var invalidCompileFile = GetInvalidFiles(compileFiles, projectFile).FirstOrDefault();
        if (invalidCompileFile != null)
        {
            throw new WarningException("File contains assembly version attributes which conflict with the attributes generated by GitVersion " + invalidCompileFile);
        }
    }

    private static bool FileContainsVersionAttribute(string compileFile, string projectFile)
    {
        var compileFileExtension = Path.GetExtension(compileFile);

        var (attributeRegex, triviaRegex) = compileFileExtension switch
        {
            ".cs" => (RegexPatterns.AssemblyVersion.CSharp.AttributeRegex, RegexPatterns.AssemblyVersion.CSharp.TriviaRegex),
            ".fs" => (RegexPatterns.AssemblyVersion.FSharp.AttributeRegex, RegexPatterns.AssemblyVersion.FSharp.TriviaRegex),
            ".vb" => (RegexPatterns.AssemblyVersion.VisualBasic.AttributeRegex, RegexPatterns.AssemblyVersion.VisualBasic.TriviaRegex),
            _ => throw new WarningException("File with name containing AssemblyInfo could not be checked for assembly version attributes which conflict with the attributes generated by GitVersion " + compileFile)
        };

        return FileContainsVersionAttribute(compileFile, projectFile, attributeRegex, triviaRegex);
    }

    private static bool FileContainsVersionAttribute(string compileFile, string projectFile, Regex attributeRegex, Regex triviaRegex)
    {
        var combine = PathHelper.Combine(Path.GetDirectoryName(projectFile), compileFile);
        var allText = File.ReadAllText(combine);
        allText += PathHelper.NewLine; // Always add a new line, this handles the case for when a file ends with the EOF marker and no new line.

        var noCommentsOrStrings = triviaRegex.Replace(allText, me => me.Value.StartsWith("//") || me.Value.StartsWith("'") ? PathHelper.NewLine : string.Empty);
        return attributeRegex.IsMatch(noCommentsOrStrings);
    }

    private static IEnumerable<string> GetInvalidFiles(IEnumerable<ITaskItem> compileFiles, string projectFile)
        => compileFiles.Select(x => x.ItemSpec)
            .Where(compileFile => compileFile.Contains("AssemblyInfo"))
            .Where(s => FileContainsVersionAttribute(s, projectFile));

    public static FileWriteInfo GetFileWriteInfo(this string? intermediateOutputPath, string language, string projectFile, string outputFileName)
    {
        var fileExtension = GetFileExtension(language);
        string workingDirectory, fileName;

        if (intermediateOutputPath == null)
        {
            fileName = $"{outputFileName}_{Path.GetFileNameWithoutExtension(projectFile)}_{Path.GetRandomFileName()}.g.{fileExtension}";
            workingDirectory = TempPath;
        }
        else
        {
            fileName = $"{outputFileName}.g.{fileExtension}";
            workingDirectory = intermediateOutputPath;
        }

        return new FileWriteInfo(workingDirectory, fileName, fileExtension);
    }
}
