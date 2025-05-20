using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Ecliptix.Core.Localization.Generator;

[Generator]
public class LocalizationSourceGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        IEnumerable<AdditionalText> localeFiles = context.AdditionalFiles
            .Where(f => IsLocaleFile(f.Path));

        if (!localeFiles.Any())
        {
            GenerateEmptyLocalesClass(context);
            return;
        }

        Dictionary<string, Dictionary<string, string>> allCulturesData = new();

        foreach (AdditionalText file in localeFiles)
        {
            string cultureName = Path.GetFileNameWithoutExtension(file.Path);
            SourceText? sourceText = file.GetText(context.CancellationToken);
            string? jsonContent = sourceText?.ToString();

            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                ReportDiagnostic(context, "LOCGEN002", "Empty Locale File", $"Locale file '{file.Path}' is empty or unreadable.", DiagnosticSeverity.Warning, file.Path, sourceText);
                continue;
            }

            Dictionary<string, string> flattenedStrings = new();
            try
            {
                if (jsonContent != null)
                {
                    using JsonDocument document = JsonDocument.Parse(jsonContent);
                    ProcessJsonElement(document.RootElement, string.Empty, flattenedStrings);
                }

                allCulturesData[cultureName] = flattenedStrings;
            }
            catch (JsonException ex)
            {
                ReportDiagnostic(context, "LOCGEN001", "JSON Parsing Error", $"Failed to parse locale file '{file.Path}': {ex.Message}", DiagnosticSeverity.Error, file.Path, sourceText, ex);
                continue;
            }
        }

        string generatedSource = GenerateLocalesClassSource(allCulturesData);
        context.AddSource("GeneratedLocales.g.cs", SourceText.From(generatedSource, Encoding.UTF8));
    }

    private static bool IsLocaleFile(string filePath)
    {
        if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? parentDirectoryName = Path.GetFileName(Path.GetDirectoryName(filePath));
        return "Locales".Equals(parentDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    private void GenerateEmptyLocalesClass(GeneratorExecutionContext context)
    {
        StringBuilder sourceBuilder = new();
        sourceBuilder.AppendLine("using System.Collections.Generic;");
        sourceBuilder.AppendLine("");
        sourceBuilder.AppendLine("namespace Ecliptix.Core.Services.Generated");
        sourceBuilder.AppendLine("{");
        sourceBuilder.AppendLine("    public static class GeneratedLocales");
        sourceBuilder.AppendLine("    {");
        sourceBuilder.AppendLine("        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllCultures = ");
        sourceBuilder.AppendLine("            new Dictionary<string, IReadOnlyDictionary<string, string>>(0);");
        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine("}");
        context.AddSource("GeneratedLocales.g.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
    }
    
    private string GenerateLocalesClassSource(Dictionary<string, Dictionary<string, string>> allCulturesData)
    {
        var sourceBuilder = new StringBuilder();
        sourceBuilder.AppendLine("using System.Collections.Generic;");
        sourceBuilder.AppendLine("");
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

    private static void ProcessJsonElement(JsonElement element, string currentPath, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string newPath = string.IsNullOrEmpty(currentPath) ? property.Name : $"{currentPath}.{property.Name}";
                    ProcessJsonElement(property.Value, newPath, result);
                }
                break;
            case JsonValueKind.Array:
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
            case JsonValueKind.Number:
                result[currentPath] = element.GetRawText();
                break;
            case JsonValueKind.True:
                result[currentPath] = "true";
                break;
            case JsonValueKind.False:
                result[currentPath] = "false";
                break;
            case JsonValueKind.Null:
                result[currentPath] = string.Empty;
                break;
            case JsonValueKind.Undefined:
                break;
        }
    }
    
    private void ReportDiagnostic(
        GeneratorExecutionContext context, 
        string id, 
        string title, 
        string messageFormat, 
        DiagnosticSeverity severity, 
        string filePath, 
        SourceText? sourceText,
        Exception? exception = null)
    {
        Location location = Location.None;
        if (!string.IsNullOrEmpty(filePath) && sourceText != null && exception is JsonException jsonEx)
        {
            if (jsonEx.LineNumber.HasValue && jsonEx.BytePositionInLine.HasValue)
            {
                TextLine line = sourceText.Lines.Count > jsonEx.LineNumber.Value ? sourceText.Lines[(int)jsonEx.LineNumber.Value] : sourceText.Lines.LastOrDefault();
                if(line != default)
                {
                    int charPos = (int)jsonEx.BytePositionInLine.Value; 
                    charPos = Math.Min(charPos, line.Span.Length -1);
                    charPos = Math.Max(0, charPos);
                    location = Location.Create(filePath, line.Span, new LinePositionSpan(new LinePosition((int)jsonEx.LineNumber.Value, charPos), new LinePosition((int)jsonEx.LineNumber.Value, charPos + 1)));
                }
            }
            if (location == Location.None) 
            {
                location = Location.Create(filePath, TextSpan.FromBounds(0,0), new LinePositionSpan(LinePosition.Zero, LinePosition.Zero));
            }
        }
        else if (!string.IsNullOrEmpty(filePath))
        {
            location = Location.Create(filePath, TextSpan.FromBounds(0,0), new LinePositionSpan(LinePosition.Zero, LinePosition.Zero));
        }

        context.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor(id, title, messageFormat, "Localization", severity, true),
            location,
            exception?.ToString() 
        ));
    }
}