using System.Xml.Linq;
using DeadCodeHunter.Loading;
using Microsoft.CodeAnalysis;

namespace DeadCodeHunter.Util;

internal static class ProjectMetadataInspector
{
    public static ProjectMetadata Evaluate(Project project)
    {
        bool isPackable = false;
        bool isTest = IsLikelyTestProject(project);

        if (project.FilePath is string projectFile && File.Exists(projectFile))
        {
            try
            {
                XDocument doc = XDocument.Load(projectFile);
                XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                IEnumerable<XElement> properties = doc.Descendants(ns + "PropertyGroup");

                string? isPackableValue = properties.Elements(ns + "IsPackable").Select(e => e.Value?.Trim())
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                string? generatePackageValue = properties.Elements(ns + "GeneratePackageOnBuild").Select(e => e.Value?.Trim())
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

                if (!string.IsNullOrEmpty(isPackableValue))
                {
                    isPackable = IsTrue(isPackableValue);
                }
                else if (!string.IsNullOrEmpty(generatePackageValue))
                {
                    isPackable = IsTrue(generatePackageValue);
                }

                if (!isTest)
                {
                    isTest = ContainsTestPackages(projectFile);
                }
            }
            catch
            {
                // Ignore XML parsing issues; default heuristics will apply.
            }
        }

        return new ProjectMetadata(
            IsPackable: isPackable,
            IsTestProject: isTest,
            AssemblyName: project.AssemblyName);
    }

    private static bool IsTrue(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("1", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyTestProject(Project project)
    {
        string name = project.Name ?? string.Empty;
        if (name.Contains(".Tests", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (project.FilePath is string filePath && filePath.Contains("Test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return project.OutputFilePath?.Contains("test", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool ContainsTestPackages(string projectFile)
    {
        try
        {
            XDocument doc = XDocument.Load(projectFile);
            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            foreach (XElement reference in doc.Descendants(ns + "PackageReference"))
            {
                string? include = reference.Attribute("Include")?.Value;
                if (include is null)
                    continue;

                if (include.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                    include.Contains("nunit", StringComparison.OrdinalIgnoreCase) ||
                    include.Contains("mstest", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore parsing errors; fall back to default heuristics.
        }

        return false;
    }
}
