namespace DatasheetGenerator.Services;

using System.IO;
using DatasheetGenerator.Models;
using Newtonsoft.Json.Linq;

public sealed class SchemaGraphService
{
  private readonly SchemaService schemaService;
  private readonly DataEntryService dataEntryService;

  public SchemaGraphService(SchemaService schemaService, DataEntryService dataEntryService)
  {
    this.schemaService = schemaService;
    this.dataEntryService = dataEntryService;
  }

  public SchemaGraphDto Build(string rootSchemaName, string schemaDirectory)
  {
    var allSchemas = this.LoadAllSchemas(schemaDirectory);

    if (!allSchemas.ContainsKey(rootSchemaName))
    {
      return new SchemaGraphDto { RootSchema = rootSchemaName, Nodes = [], Edges = [] };
    }

    var reverseRefs = BuildReverseRefMap(allSchemas);
    var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var positionCounter = 0;
    var queue = new Queue<string>();
    levels[rootSchemaName] = 0;
    positions[rootSchemaName] = positionCounter++;
    queue.Enqueue(rootSchemaName);

    while (queue.Count > 0)
    {
      var name = queue.Dequeue();
      var currentLevel = levels[name];

      if (allSchemas.TryGetValue(name, out var flatColumns))
      {
        foreach (var col in flatColumns)
        {
          var parsed = ParseRef(col.Ref);
          if (parsed is null)
          {
            continue;
          }

          var refSchemaName = parsed.Value.Schema;
          if (!allSchemas.ContainsKey(refSchemaName) || levels.ContainsKey(refSchemaName))
          {
            continue;
          }

          levels[refSchemaName] = currentLevel + 1;
          positions[refSchemaName] = positionCounter++;
          queue.Enqueue(refSchemaName);
        }
      }

      if (reverseRefs.TryGetValue(name, out var backRefSchemas))
      {
        foreach (var backSchemaName in backRefSchemas)
        {
          if (levels.ContainsKey(backSchemaName))
          {
            continue;
          }

          levels[backSchemaName] = currentLevel + 1;
          positions[backSchemaName] = positionCounter++;
          queue.Enqueue(backSchemaName);
        }
      }
    }

    var minLevel = levels.Values.Min();
    if (minLevel < 0)
    {
      var shift = -minLevel;
      foreach (var key in levels.Keys.ToList())
      {
        levels[key] += shift;
      }
    }

    var includedSchemas = levels.Keys
      .Where(allSchemas.ContainsKey)
      .ToDictionary(k => k, k => allSchemas[k], StringComparer.OrdinalIgnoreCase);

    var nodes = BuildNodes(includedSchemas, levels);
    var edges = BuildEdges(includedSchemas);

    return new SchemaGraphDto
    {
      RootSchema = rootSchemaName,
      Nodes = [.. nodes.OrderBy(n => n.Level).ThenBy(n => positions.TryGetValue(n.Schema, out var p) ? p : int.MaxValue)],
      Edges = edges
    };
  }

  private const string RefSeparator = ".schema.json#/definitions/";

  private static (string Schema, string Column)? ParseRef(string? @ref)
  {
    if (@ref is null) { return null; }
    var idx = @ref.IndexOf(RefSeparator, StringComparison.Ordinal);
    if (idx <= 0) { return null; }
    var schema = @ref[..idx];
    var column = @ref[(idx + RefSeparator.Length)..];
    if (string.IsNullOrEmpty(column)) { return null; }
    return (schema, column);
  }

  private Dictionary<string, IReadOnlyList<FlatColumn>> LoadAllSchemas(string schemaDirectory)
  {
    var result = new Dictionary<string, IReadOnlyList<FlatColumn>>(StringComparer.OrdinalIgnoreCase);

    if (Directory.Exists(schemaDirectory) is false)
    {
      return result;
    }

    foreach (var file in Directory.GetFiles(schemaDirectory, "*.schema.json"))
    {
      var nameWithExt = Path.GetFileNameWithoutExtension(file);
      var name = nameWithExt.EndsWith(".schema", StringComparison.OrdinalIgnoreCase)
        ? Path.GetFileNameWithoutExtension(nameWithExt)
        : nameWithExt;


      try
      {
        var schemaText = this.schemaService.LoadSchemaText(file);
        if (name.Equals("enum", StringComparison.OrdinalIgnoreCase))
        {
          result[name] = ParseEnumSchemaColumns(schemaText);
        }
        else
        {
          var columns = this.schemaService.ParseSchema(schemaText);
          result[name] = this.dataEntryService.GetFlatColumns(columns);
        }
      }
      catch
      {
        // skip schemas that fail to parse
      }
    }

    return result;
  }

  private static IReadOnlyList<FlatColumn> ParseEnumSchemaColumns(string schemaText)
  {
    try
    {
      var json = JObject.Parse(schemaText);
      if (json["definitions"] is not JObject definitions)
      {
        return [];
      }

      return definitions.Properties()
        .Select(p => new FlatColumn { Path = p.Name, LeafName = p.Name, JsonType = "string" })
        .ToList();
    }
    catch
    {
      return [];
    }
  }

  private static Dictionary<string, HashSet<string>> BuildReverseRefMap(
    IReadOnlyDictionary<string, IReadOnlyList<FlatColumn>> allSchemas)
  {
    var reverseRefs = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

    foreach (var (schemaName, flatColumns) in allSchemas)
    {
      foreach (var col in flatColumns)
      {
        var parsed = ParseRef(col.Ref);
        if (parsed is null)
        {
          continue;
        }

        var targetSchema = parsed.Value.Schema;
        if (!reverseRefs.TryGetValue(targetSchema, out var set))
        {
          set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          reverseRefs[targetSchema] = set;
        }

        set.Add(schemaName);
      }
    }

    return reverseRefs;
  }

  private static IReadOnlyList<SchemaGraphNode> BuildNodes(
    IReadOnlyDictionary<string, IReadOnlyList<FlatColumn>> schemaData,
    IReadOnlyDictionary<string, int> levels)
  {
    var nodes = new List<SchemaGraphNode>();
    foreach (var (name, flatColumns) in schemaData)
    {
      nodes.Add(new SchemaGraphNode
      {
        Schema = name,
        Level = levels.TryGetValue(name, out var level) ? level : 0,
        Columns = BuildDisplayColumns(flatColumns)
      });
    }

    return nodes;
  }

  private static IReadOnlyList<SchemaGraphColumn> BuildDisplayColumns(IReadOnlyList<FlatColumn> flatColumns)
  {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var columns = new List<SchemaGraphColumn>();
    foreach (var col in flatColumns)
    {
      var displayPath = DeIndexPath(col.Path);
      if (seen.Add(displayPath))
      {
        columns.Add(new SchemaGraphColumn { Path = displayPath, Ref = col.Ref });
      }
    }

    return columns;
  }

  private static IReadOnlyList<SchemaGraphEdge> BuildEdges(
    IReadOnlyDictionary<string, IReadOnlyList<FlatColumn>> schemaData)
  {
    var edges = new List<SchemaGraphEdge>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var (fromName, flatColumns) in schemaData)
    {
      foreach (var col in flatColumns)
      {
        var parsed = ParseRef(col.Ref);
        if (parsed is null)
        {
          continue;
        }

        var toName = parsed.Value.Schema;
        var toColumn = parsed.Value.Column;
        var fromColumn = DeIndexPath(col.Path);

        if (!schemaData.ContainsKey(toName))
        {
          continue;
        }

        var key = $"{fromName}|{fromColumn}|{toName}|{toColumn}";
        if (seen.Add(key) is false)
        {
          continue;
        }

        edges.Add(new SchemaGraphEdge
        {
          FromNode = fromName,
          FromColumn = fromColumn,
          ToNode = toName,
          ToColumn = toColumn
        });
      }
    }

    return edges;
  }

  private static string DeIndexPath(string path)
  {
    return string.Join(".", path.Split('.').Where(p => !int.TryParse(p, out _)));
  }
}
