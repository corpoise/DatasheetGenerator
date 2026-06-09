namespace DatasheetGenerator.Services;

using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class EnumParsingService
{
  private static readonly Regex EnumBlockRegex = new(
    @"enum\s+(\w+)\s*\{([^}]*)\}",
    RegexOptions.Singleline | RegexOptions.Compiled);

  public IReadOnlyDictionary<string, IReadOnlyList<string>> ParseCSharpFile(string filePath)
  {
    return this.ParseText(File.ReadAllText(filePath));
  }

  public IReadOnlyDictionary<string, IReadOnlyList<string>> ParseText(string text)
  {
    var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    foreach (Match match in EnumBlockRegex.Matches(text))
    {
      var name = match.Groups[1].Value;
      var body = match.Groups[2].Value;
      var values = new List<string>();
      foreach (var rawLine in body.Split('\n'))
      {
        var line = rawLine;
        var commentIdx = line.IndexOf("//", StringComparison.Ordinal);
        if (commentIdx >= 0)
        {
          line = line[..commentIdx];
        }
        foreach (var part in line.Split(','))
        {
          var token = part;
          var eqIdx = token.IndexOf('=');
          if (eqIdx >= 0)
          {
            token = token[..eqIdx];
          }
          token = token.Trim();
          if (token.Length > 0 && (char.IsLetter(token[0]) || token[0] == '_') &&
              token.All(c => char.IsLetterOrDigit(c) || c == '_'))
          {
            values.Add(token);
          }
        }
      }
      if (values.Count > 0)
      {
        result[name] = values;
      }
    }
    return result;
  }

  public IReadOnlyDictionary<string, IReadOnlyList<string>> GenerateEnumSchema(
    string? enumFilePath,
    string enumSchemaOutputPath)
  {
    IReadOnlyDictionary<string, IReadOnlyList<string>> enumSchema;
    if (string.IsNullOrEmpty(enumFilePath) is false && File.Exists(enumFilePath))
    {
      enumSchema = this.ParseCSharpFile(enumFilePath);
    }
    else
    {
      if (File.Exists(enumSchemaOutputPath))
      {
        return this.LoadEnumSchema(enumSchemaOutputPath);
      }
      enumSchema = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    }
    WriteEnumSchema(enumSchema, enumSchemaOutputPath);
    return enumSchema;
  }

  public IReadOnlyDictionary<string, IReadOnlyList<string>> LoadEnumSchema(string enumSchemaPath)
  {
    if (File.Exists(enumSchemaPath) is false)
    {
      return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    }
    try
    {
      var json = JObject.Parse(File.ReadAllText(enumSchemaPath));
      var definitions = json["definitions"] as JObject;
      if (definitions is null)
      {
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
      }
      var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
      foreach (var prop in definitions.Properties())
      {
        if (prop.Value is JArray arr)
        {
          result[prop.Name] = arr.Select(t => t.ToString()).ToList();
        }
      }
      return result;
    }
    catch
    {
      return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    }
  }

  private static void WriteEnumSchema(
    IReadOnlyDictionary<string, IReadOnlyList<string>> enumSchema,
    string outputPath)
  {
    var definitions = new JObject();
    foreach (var (name, values) in enumSchema)
    {
      var arr = new JArray();
      foreach (var v in values)
      {
        arr.Add(v);
      }
      definitions[name] = arr;
    }
    var json = new JObject { ["definitions"] = definitions };
    var directory = Path.GetDirectoryName(outputPath);
    if (string.IsNullOrEmpty(directory) is false)
    {
      Directory.CreateDirectory(directory);
    }
    File.WriteAllText(outputPath, json.ToString(Formatting.Indented));
  }
}
