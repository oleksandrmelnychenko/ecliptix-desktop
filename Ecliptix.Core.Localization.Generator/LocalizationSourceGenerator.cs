using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Ecliptix.Core.Localization.Generator;

[Generator]
public class LocalizationSourceGenerator : IIncrementalGenerator
{
    // The new entry point for incremental generators.
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // STEP 1: Define the pipeline for what constitutes a "locale file".
        // We're looking for .json files inside a "Locales" directory.
        IncrementalValuesProvider<AdditionalText> localeFiles = context.AdditionalTextsProvider
            .Where(f => IsLocaleFile(f.Path));

        // STEP 2: Transform the file into its content and necessary metadata.
        // This stage reads the file content and handles potential read errors.
        IncrementalValuesProvider<LocaleFileContent> fileContents = localeFiles
            .Select((file, cancellationToken) =>
            {
                var sourceText = file.GetText(cancellationToken);
                return new LocaleFileContent(file.Path, sourceText);
            });

        // STEP 3: Parse the JSON content and report diagnostics for parsing errors.
        // We also collect all successfully parsed data.
        var compilationAndFiles = context.CompilationProvider.Combine(fileContents.Collect());
        
        context.RegisterSourceOutput(compilationAndFiles, (spc, source) =>
        {
            var allCulturesData = new Dictionary<string, Dictionary<string, string>>();

            foreach (var fileContent in source.Right) // source.Right is the ImmutableArray<LocaleFileContent>
            {
                string cultureName = Path.GetFileNameWithoutExtension(fileContent.FilePath);
                
                if (string.IsNullOrWhiteSpace(fileContent.Content?.ToString()))
                {
                    ReportDiagnostic(spc, "LOCGEN002", "Empty Locale File", $"Locale file '{fileContent.FilePath}' is empty or unreadable.", DiagnosticSeverity.Warning, fileContent.FilePath, fileContent.Content);
                    continue;
                }
                
                var flattenedStrings = new Dictionary<string, string>();
                try
                {
                    using var document = JsonDocument.Parse(fileContent.Content.ToString());
                    ProcessJsonElement(document.RootElement, string.Empty, flattenedStrings);
                    allCulturesData[cultureName] = flattenedStrings;
                }
                catch (JsonException ex)
                {
                    ReportDiagnostic(spc, "LOCGEN001", "JSON Parsing Error", $"Failed to parse locale file '{fileContent.FilePath}': {ex.Message}", DiagnosticSeverity.Error, fileContent.FilePath, fileContent.Content, ex);
                }
            }

            // STEP 4: Generate the final source code based on the collected data.
            string generatedSource = allCulturesData.Any()
                ? GenerateLocalesClassSource(allCulturesData)
                : GenerateEmptyLocalesClassSource();

            spc.AddSource("GeneratedLocales.g.cs", SourceText.From(generatedSource, Encoding.UTF8));
        });
    }

    // A simple record to hold file data as it moves through the pipeline.
    private record struct LocaleFileContent(string FilePath, SourceText? Content);
    
    // This helper method remains unchanged.
    private static bool IsLocaleFile(string filePath)
    {
        if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? parentDirectoryName = Path.GetFileName(Path.GetDirectoryName(filePath));
        return "Locales".Equals(parentDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    // This method now generates the source string directly.
    private string GenerateEmptyLocalesClassSource()
    {
        var sourceBuilder = new StringBuilder();
        sourceBuilder.AppendLine("using System.Collections.Generic;");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("namespace Ecliptix.Core.Services.Generated");
        sourceBuilder.AppendLine("{");
        sourceBuilder.AppendLine("    public static class GeneratedLocales");
        sourceBuilder.AppendLine("    {");
        sourceBuilder.AppendLine("        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllCultures = ");
        sourceBuilder.AppendLine("            System.Collections.Immutable.ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty;"); // Using Immutable for efficiency
        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine("}");
        return sourceBuilder.ToString();
    }
    
    // This method remains largely unchanged.
    private string GenerateLocalesClassSource(Dictionary<string, Dictionary<string, string>> allCulturesData)
    {
        var sourceBuilder = new StringBuilder();
        sourceBuilder.AppendLine("using System.Collections.Generic;");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("namespace Ecliptix.Core.Services.Generated");
        sourceBuilder.AppendLine("{");
        sourceBuilder.AppendLine("    public static class GeneratedLocales");
        sourceBuilder.AppendLine("    {");
        sourceBuilder.AppendLine("        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllCultures = ");
        sourceBuilder.AppendLine("            new Dictionary<string, IReadOnlyDictionary<string, string>>(" + allCulturesData.Count + ")");
        sourceBuilder.AppendLine("            {");

        foreach (var cultureEntry in allCulturesData)
        {
            sourceBuilder.AppendLine($"                [\"{cultureEntry.Key}\"] = new Dictionary<string, string>(" + cultureEntry.Value.Count + ")");
            sourceBuilder.AppendLine("                {");
            foreach (var stringEntry in cultureEntry.Value)
            {
                // Using SymbolDisplay is a robust way to create string literals. Good choice!
                string escapedKey = SymbolDisplay.FormatLiteral(stringEntry.Key, true);
                string escapedValue = SymbolDisplay.FormatLiteral(stringEntry.Value, true);
                sourceBuilder.AppendLine($"                    {{{escapedKey}, {escapedValue}}},");
            }
            sourceBuilder.AppendLine("                },");
        }

        sourceBuilder.AppendLine("            };");
        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine("}");
        return sourceBuilder.ToString();
    }

    // This JSON processing logic remains the same.
    private static void ProcessJsonElement(JsonElement element, string currentPath, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    string newPath = string.IsNullOrEmpty(currentPath) ? property.Name : $"{currentPath}.{property.Name}";
                    ProcessJsonElement(property.Value, newPath, result);
                }
                break;
            case JsonValueKind.Array:
                // Note: Arrays in localization are uncommon. This flattens them as "key[0]", "key[1]", etc.
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string newPath = $"{currentPath}[{index++}]";
                    ProcessJsonElement(item, newPath, result);
                }
                break;
            case JsonValueKind.String:
                result[currentPath] = element.GetString() ?? string.Empty;
                break;
            // Other primitive types can be handled as needed.
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                result[currentPath] = element.GetRawText();
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                break; // Or handle as empty string if desired.
        }
    }
    
    // ReportDiagnostic now takes a SourceProductionContext instead of GeneratorExecutionContext.
    private void ReportDiagnostic(
        SourceProductionContext context, 
        string id, 
        string title, 
        string message, 
        DiagnosticSeverity severity, 
        string? filePath, 
        SourceText? sourceText,
        Exception? exception = null)
    {
        Location? location = null;

        if (filePath != null && sourceText != null && exception is JsonException jsonEx && jsonEx.LineNumber.HasValue)
        {
            int line = (int)jsonEx.LineNumber.Value;
            int charPos = (int)(jsonEx.BytePositionInLine ?? 0);
            
            // Basic bounds checking for safety
            if (line < sourceText.Lines.Count)
            {
                var textLine = sourceText.Lines[line];
                charPos = Math.Min(charPos, textLine.Span.Length - 1);
                var linePos = new LinePosition(line, charPos);
                location = Location.Create(filePath, textLine.Span, new LinePositionSpan(linePos, linePos));
            }
        }
        
        location ??= !string.IsNullOrEmpty(filePath) 
            ? Location.Create(filePath, TextSpan.FromBounds(0, 0), new LinePositionSpan(LinePosition.Zero, LinePosition.Zero)) 
            : Location.None;

        context.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor(id, title, message, "Localization", severity, isEnabledByDefault: true),
            location
        ));
    }
}