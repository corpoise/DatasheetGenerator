namespace DatasheetGenerator.Tests;

using System.IO;
using DatasheetGenerator.Models;
using DatasheetGenerator.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class SchemaGraphServiceTests : IDisposable
{
  private readonly string schemaDir;
  private readonly SchemaGraphService service;

  public SchemaGraphServiceTests()
  {
    var root = Path.Combine(Path.GetTempPath(), $"SchemaGraphTests_{Guid.NewGuid():N}");
    this.schemaDir = Path.Combine(root, "schema");
    Directory.CreateDirectory(this.schemaDir);
    this.service = new SchemaGraphService(new SchemaService(), new DataEntryService());
  }

  public void Dispose()
  {
    var root = Path.GetDirectoryName(this.schemaDir)!;
    if (Directory.Exists(root))
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void Build_WithDirectRef_ReturnsTwoNodes()
  {
    this.WriteSchema("Source", CreateScalarRefSchema("Target", "Name"));
    this.WriteSchema("Target", CreateSimpleSchema());

    var dto = this.service.Build("Source", this.schemaDir);

    Assert.Equal(2, dto.Nodes.Count);
    Assert.Contains(dto.Nodes, n => n.Schema == "Source" && n.Level is 0);
    Assert.Contains(dto.Nodes, n => n.Schema == "Target" && n.Level is 1);
  }

  [Fact]
  public void Build_WithDirectRef_ReturnsEdge()
  {
    this.WriteSchema("Source", CreateScalarRefSchema("Target", "Name"));
    this.WriteSchema("Target", CreateSimpleSchema());

    var dto = this.service.Build("Source", this.schemaDir);

    Assert.Single(dto.Edges);
    var edge = dto.Edges[0];
    Assert.Equal("Source", edge.FromNode);
    Assert.Equal("TargetRef", edge.FromColumn);
    Assert.Equal("Target", edge.ToNode);
    Assert.Equal("Name", edge.ToColumn);
  }

  [Fact]
  public void Build_WithMultipleSourceRows_ReturnsOneEdge()
  {
    this.WriteSchema("Source", CreateScalarRefSchema("Target", "Name"));
    this.WriteSchema("Target", CreateSimpleSchema());

    var dto = this.service.Build("Source", this.schemaDir);

    Assert.Single(dto.Edges);
  }

  [Fact]
  public void Build_WithArrayRef_ReturnsOneEdge()
  {
    this.WriteSchema("ArraySource", CreateArrayRefSchema("Target", "Name"));
    this.WriteSchema("Target", CreateSimpleSchema());

    var dto = this.service.Build("ArraySource", this.schemaDir);

    var edges = dto.Edges.Where(e => e.FromNode == "ArraySource" && e.ToNode == "Target").ToList();
    Assert.Single(edges);
    Assert.Equal("Items.Group", edges[0].FromColumn);
    Assert.Equal("Name", edges[0].ToColumn);
  }

  [Fact]
  public void Build_WithSchemaOnly_ReturnsNodeWithColumns()
  {
    this.WriteSchema("Source", CreateSimpleSchema());

    var dto = this.service.Build("Source", this.schemaDir);

    Assert.Single(dto.Nodes);
    Assert.Equal(2, dto.Nodes[0].Columns.Count);
  }

  [Fact]
  public void Build_WithCircularRef_CompletesWithoutInfiniteLoop()
  {
    this.WriteSchema("A", CreateScalarRefSchema("B", "Name", colName: "BRef"));
    this.WriteSchema("B", CreateScalarRefSchema("A", "Name", colName: "ARef"));

    var dto = this.service.Build("A", this.schemaDir);

    Assert.Equal(2, dto.Nodes.Count);
  }

  [Fact]
  public void Build_WithTransitiveRef_AssignsCorrectLevels()
  {
    this.WriteSchema("A", CreateScalarRefSchema("B", "Name", colName: "BRef"));
    this.WriteSchema("B", CreateScalarRefSchema("C", "Name", colName: "CRef"));
    this.WriteSchema("C", CreateSimpleSchema());

    var dto = this.service.Build("A", this.schemaDir);

    Assert.Equal(0, dto.Nodes.First(n => n.Schema == "A").Level);
    Assert.Equal(1, dto.Nodes.First(n => n.Schema == "B").Level);
    Assert.Equal(2, dto.Nodes.First(n => n.Schema == "C").Level);
  }

  [Fact]
  public void Build_DeIndexesArrayColumnPaths()
  {
    this.WriteSchema("ArraySource", CreateArrayRefSchema("Target", "Name"));
    this.WriteSchema("Target", CreateSimpleSchema());

    var dto = this.service.Build("ArraySource", this.schemaDir);

    var node = dto.Nodes.First(n => n.Schema == "ArraySource");
    Assert.Contains(node.Columns, c => c.Path == "Items.Group");
    Assert.DoesNotContain(node.Columns, c => c.Path.Contains(".0.") || c.Path.Contains(".1."));
  }

  [Fact]
  public void SchemaGraphWriter_WritesFileToOutputDirectory()
  {
    var dto = new SchemaGraphDto { RootSchema = "TestSchema", Nodes = [], Edges = [] };
    var writer = new SchemaGraphWriter();
    var outputDir = Path.Combine(Path.GetDirectoryName(this.schemaDir)!, "output");

    writer.Write(dto, outputDir);

    Assert.True(File.Exists(Path.Combine(outputDir, "TestSchema.graph.json")));
  }

  [Fact]
  public void SchemaGraphWriter_OutputIsValidJson()
  {
    this.WriteSchema("Source", CreateSimpleSchema());
    var dto = this.service.Build("Source", this.schemaDir);
    var writer = new SchemaGraphWriter();
    var outputDir = Path.Combine(Path.GetDirectoryName(this.schemaDir)!, "output");

    writer.Write(dto, outputDir);

    var content = File.ReadAllText(Path.Combine(outputDir, "Source.graph.json"));
    var parsed = JObject.Parse(content);
    Assert.Equal("Source", parsed["rootSchema"]?.Value<string>());
    Assert.NotNull(parsed["nodes"]);
    Assert.NotNull(parsed["edges"]);
  }

  [Fact]
  public void Build_WithReverseRef_RootRemainsAtLevelZero()
  {
    // Caller references Root — when Root is the view target, Root should stay at level 0
    this.WriteSchema("Caller", CreateScalarRefSchema("Root", "Name"));
    this.WriteSchema("Root", CreateSimpleSchema());

    var dto = this.service.Build("Root", this.schemaDir);

    Assert.Contains(dto.Nodes, n => n.Schema == "Root" && n.Level is 0);
    Assert.Contains(dto.Nodes, n => n.Schema == "Caller" && n.Level is 1);
  }

  [Fact]
  public void Build_WithReverseRefChain_ArrangesCallersBeyondRoot()
  {
    // CallerOfCaller references Caller; Caller references Root
    this.WriteSchema("CallerOfCaller", CreateScalarRefSchema("Caller", "Name"));
    this.WriteSchema("Caller", CreateScalarRefSchema("Root", "Name"));
    this.WriteSchema("Root", CreateSimpleSchema());

    var dto = this.service.Build("Root", this.schemaDir);

    Assert.Equal(0, dto.Nodes.First(n => n.Schema == "Root").Level);
    Assert.Equal(1, dto.Nodes.First(n => n.Schema == "Caller").Level);
    Assert.Equal(2, dto.Nodes.First(n => n.Schema == "CallerOfCaller").Level);
  }

  [Fact]
  public void Build_WithMultipleRefs_OrdersNodesByColumnRefOrder()
  {
    // A references Z first (column position), then B — reverse alphabetical order
    this.WriteSchema("A", CreateSchemaWithRefsInOrder("Z", "B"));
    this.WriteSchema("B", CreateSimpleSchema());
    this.WriteSchema("Z", CreateSimpleSchema());

    var dto = this.service.Build("A", this.schemaDir);

    var level1Nodes = dto.Nodes.Where(n => n.Level == 1).ToList();
    Assert.Equal(2, level1Nodes.Count);
    Assert.Equal("Z", level1Nodes[0].Schema);
    Assert.Equal("B", level1Nodes[1].Schema);
  }

  private void WriteSchema(string name, JObject schema)
  {
    File.WriteAllText(
      Path.Combine(this.schemaDir, $"{name}.schema.json"),
      schema.ToString(Formatting.Indented));
  }

  private static JObject CreateSimpleSchema()
  {
    return new JObject
    {
      ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer", ["minimum"] = 1 },
        ["Name"] = new JObject { ["type"] = "string" }
      },
      ["required"] = new JArray("Id", "Name")
    };
  }

  private static JObject CreateScalarRefSchema(string targetSchema, string targetColumn, string colName = "TargetRef")
  {
    return new JObject
    {
      ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer", ["minimum"] = 1 },
        ["Name"] = new JObject { ["type"] = "string" },
        [colName] = new JObject { ["ref"] = $"{targetSchema}.{targetColumn}" }
      },
      ["required"] = new JArray("Id", "Name")
    };
  }

  private static JObject CreateSchemaWithRefsInOrder(string firstRef, string secondRef)
  {
    return new JObject
    {
      ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer", ["minimum"] = 1 },
        ["Name"] = new JObject { ["type"] = "string" },
        [$"{firstRef}Ref"] = new JObject { ["ref"] = $"{firstRef}.Name" },
        [$"{secondRef}Ref"] = new JObject { ["ref"] = $"{secondRef}.Name" }
      },
      ["required"] = new JArray("Id", "Name")
    };
  }

  private static JObject CreateArrayRefSchema(string targetSchema, string targetColumn)
  {
    return new JObject
    {
      ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer", ["minimum"] = 1 },
        ["Items"] = new JObject
        {
          ["type"] = "array",
          ["itemName"] = "Item",
          ["minItems"] = 0,
          ["maxItems"] = 3,
          ["items"] = new JObject
          {
            ["type"] = "object",
            ["properties"] = new JObject
            {
              ["Group"] = new JObject { ["ref"] = $"{targetSchema}.{targetColumn}" },
              ["Weight"] = new JObject { ["type"] = "integer", ["minimum"] = 0 }
            },
            ["required"] = new JArray("Group", "Weight")
          }
        }
      },
      ["required"] = new JArray("Id")
    };
  }
}
