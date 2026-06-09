namespace DatasheetGenerator.Tests;

using DatasheetGenerator.Services;

public sealed class EnumParsingServiceTests
{
  private readonly EnumParsingService service = new();

  [Fact]
  public void ParseText_SingleEnum_ReturnsValues()
  {
    var text = """
      public enum MonsterType
      {
        Normal,
        Elite,
        Boss
      }
      """;

    var result = service.ParseText(text);

    Assert.Single(result);
    Assert.Equal(new[] { "Normal", "Elite", "Boss" }, result["MonsterType"]);
  }

  [Fact]
  public void ParseText_MultipleEnums_ReturnsAll()
  {
    var text = """
      public enum MonsterType { Normal, Elite }
      public enum ElementType { Fire, Water }
      """;

    var result = service.ParseText(text);

    Assert.Equal(2, result.Count);
    Assert.True(result.ContainsKey("MonsterType"));
    Assert.True(result.ContainsKey("ElementType"));
  }

  [Fact]
  public void ParseText_EnumWithExplicitAssignment_ExtractsNamesOnly()
  {
    var text = "public enum Status { Active = 1, Inactive = 2, Deleted = 99 }";

    var result = service.ParseText(text);

    Assert.Equal(new[] { "Active", "Inactive", "Deleted" }, result["Status"]);
  }

  [Fact]
  public void ParseText_EnumWithInlineComments_SkipsCommentText()
  {
    var text = """
      public enum MonsterType
      {
        Normal, // normal monster
        Boss    // final boss
      }
      """;

    var result = service.ParseText(text);

    Assert.Equal(new[] { "Normal", "Boss" }, result["MonsterType"]);
  }

  [Fact]
  public void ParseText_EmptyText_ReturnsEmpty()
  {
    var result = service.ParseText(string.Empty);

    Assert.Empty(result);
  }

  [Fact]
  public void ParseText_NoEnums_ReturnsEmpty()
  {
    var result = service.ParseText("namespace GameData; using System;");

    Assert.Empty(result);
  }

  [Fact]
  public void GenerateEnumSchema_WithValidFile_WritesSchemaAndReturnsValues()
  {
    var dir = Path.Combine(Path.GetTempPath(), $"EnumTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
      var enumFile = Path.Combine(dir, "enum.cs");
      File.WriteAllText(enumFile, "public enum MonsterType { Normal, Elite, Boss }");
      var schemaPath = Path.Combine(dir, "enum.schema.json");

      var result = service.GenerateEnumSchema(enumFile, schemaPath);

      Assert.Single(result);
      Assert.Equal(new[] { "Normal", "Elite", "Boss" }, result["MonsterType"]);
      Assert.True(File.Exists(schemaPath));
    }
    finally
    {
      Directory.Delete(dir, true);
    }
  }

  [Fact]
  public void GenerateEnumSchema_WithMissingEnumFile_WritesEmptyJsonIfSchemaNotExists()
  {
    var dir = Path.Combine(Path.GetTempPath(), $"EnumTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
      var schemaPath = Path.Combine(dir, "enum.schema.json");

      var result = service.GenerateEnumSchema(null, schemaPath);

      Assert.Empty(result);
      Assert.True(File.Exists(schemaPath));
    }
    finally
    {
      Directory.Delete(dir, true);
    }
  }

  [Fact]
  public void GenerateEnumSchema_WithExistingSchemaAndNoEnumFile_ReturnsExistingSchema()
  {
    var dir = Path.Combine(Path.GetTempPath(), $"EnumTest_{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    try
    {
      var schemaPath = Path.Combine(dir, "enum.schema.json");
      File.WriteAllText(schemaPath, """{ "definitions": { "MonsterType": ["Normal", "Elite"] } }""");

      var result = service.GenerateEnumSchema(null, schemaPath);

      Assert.Single(result);
      Assert.Equal(new[] { "Normal", "Elite" }, result["MonsterType"]);
    }
    finally
    {
      Directory.Delete(dir, true);
    }
  }
}
