namespace DatasheetGenerator.Tests;

using DatasheetGenerator.Models;
using DatasheetGenerator.Services;

public sealed class CodeGenerationServiceTests
{
  private static readonly CodeGenerationService Service = new();

  [Fact]
  public void GenerateCode_WithPrimitiveFields_GeneratesCorrectProperties()
  {
    var columns = new[]
    {
      new SchemaColumn { Name = "Id", JsonType = "integer" },
      new SchemaColumn { Name = "Name", JsonType = "string" },
      new SchemaColumn { Name = "IsActive", JsonType = "boolean" },
      new SchemaColumn { Name = "Score", JsonType = "number" }
    };

    var code = Service.GenerateCode("Test", columns);

    Assert.Contains("public sealed record TestGameData", code);
    Assert.Contains("public int Id { get; init; }", code);
    Assert.Contains("public string Name { get; init; } = string.Empty;", code);
    Assert.Contains("public bool IsActive { get; init; }", code);
    Assert.Contains("public float Score { get; init; }", code);
  }

  [Fact]
  public void GenerateCode_WithVector2Format_GeneratesVector2Property()
  {
    var columns = new[]
    {
      new SchemaColumn
      {
        Name = "Position",
        JsonType = "object",
        Format = "vector2",
        Children = [new SchemaColumn { Name = "x", JsonType = "number" }, new SchemaColumn { Name = "y", JsonType = "number" }]
      }
    };

    var code = Service.GenerateCode("Test", columns);

    Assert.Contains("public Vector2 Position { get; init; }", code);
  }

  [Fact]
  public void GenerateCode_WithVector3Format_GeneratesVector3Property()
  {
    var columns = new[]
    {
      new SchemaColumn
      {
        Name = "Direction",
        JsonType = "object",
        Format = "vector3",
        Children = [
          new SchemaColumn { Name = "x", JsonType = "number" },
          new SchemaColumn { Name = "y", JsonType = "number" },
          new SchemaColumn { Name = "z", JsonType = "number" }
        ]
      }
    };

    var code = Service.GenerateCode("Test", columns);

    Assert.Contains("public Vector3 Direction { get; init; }", code);
  }

  [Fact]
  public void GenerateCode_WithDatetimeFormat_GeneratesDateTimeProperty()
  {
    var columns = new[]
    {
      new SchemaColumn { Name = "CreatedAt", JsonType = "string", Format = "datetime" }
    };

    var code = Service.GenerateCode("Test", columns);

    Assert.Contains("public DateTime CreatedAt { get; init; }", code);
  }

  [Fact]
  public void GenerateCode_WithTimespanFormats_GeneratesTimeSpanPropertiesWithAttributes()
  {
    var columns = new[]
    {
      new SchemaColumn { Name = "CooldownHour", JsonType = "integer", Format = "timespan-hour" },
      new SchemaColumn { Name = "CooldownMinute", JsonType = "integer", Format = "timespan-minute" },
      new SchemaColumn { Name = "CooldownSec", JsonType = "integer", Format = "timespan-second" },
      new SchemaColumn { Name = "CooldownMs", JsonType = "integer", Format = "timespan-millisecond" }
    };

    var code = Service.GenerateCode("Test", columns);

    Assert.Contains("[TimeSpanType(TimeSpanType.Hour)]", code);
    Assert.Contains("[TimeSpanType(TimeSpanType.Minute)]", code);
    Assert.Contains("[TimeSpanType(TimeSpanType.Second)]", code);
    Assert.Contains("[TimeSpanType(TimeSpanType.Millisecond)]", code);
    Assert.Contains("public TimeSpan CooldownHour { get; init; }", code);
    Assert.Contains("public TimeSpan CooldownMinute { get; init; }", code);
    Assert.Contains("public TimeSpan CooldownSec { get; init; }", code);
    Assert.Contains("public TimeSpan CooldownMs { get; init; }", code);
  }

  [Fact]
  public void GenerateCode_WithReadonlyListOfStrings_GeneratesIReadOnlyListProperty()
  {
    var columns = new[]
    {
      new SchemaColumn { Name = "Tags", JsonType = "array", Format = "readonly-list", ItemJsonType = "string" }
    };

    var code = Service.GenerateCode("Test", columns);

    Assert.Contains("public IReadOnlyList<string> Tags { get; init; } = [];", code);
  }

  [Fact]
  public void GenerateCode_WithFrozenDictionaryOfObjects_GeneratesNestedRecord()
  {
    var columns = new[]
    {
      new SchemaColumn
      {
        Name = "StatMap",
        JsonType = "array",
        Format = "frozen-dictionary",
        KeyColumn = "StatName",
        ItemJsonType = "object",
        ItemChildren =
        [
          new SchemaColumn { Name = "StatName", JsonType = "string" },
          new SchemaColumn { Name = "Value", JsonType = "number" }
        ]
      }
    };

    var code = Service.GenerateCode("Test", columns);

    Assert.Contains("FrozenDictionary<string, TestGameDataStatMapItem>", code);
    Assert.Contains("FrozenDictionary<string, TestGameDataStatMapItem>.Empty", code);
    Assert.Contains("public sealed record TestGameDataStatMapItem", code);
    Assert.Contains("public string StatName { get; init; } = string.Empty;", code);
    Assert.Contains("public float Value { get; init; }", code);
  }

  [Fact]
  public void GenerateCode_IncludesRequiredUsingDirectives()
  {
    var columns = new[] { new SchemaColumn { Name = "Id", JsonType = "integer" } };

    var code = Service.GenerateCode("Test", columns);

    Assert.Contains("// <auto-generated/>", code);
    Assert.Contains("using System;", code);
    Assert.Contains("using System.Collections.Generic;", code);
    Assert.Contains("using System.Collections.Frozen;", code);
    Assert.Contains("namespace GameData;", code);
  }

  [Fact]
  public void GenerateCode_WithTimespanObjectFormat_GeneratesTimeSpanProperty()
  {
    var columns = new[]
    {
      new SchemaColumn
      {
        Name = "Cooldown",
        JsonType = "object",
        Format = "timespan",
        Children =
        [
          new SchemaColumn { Name = "Hour", JsonType = "integer" },
          new SchemaColumn { Name = "Minute", JsonType = "integer" },
          new SchemaColumn { Name = "Second", JsonType = "integer" },
          new SchemaColumn { Name = "Millisecond", JsonType = "integer" }
        ]
      }
    };

    var code = Service.GenerateCode("Test", columns);

    Assert.Contains("public TimeSpan Cooldown { get; init; }", code);
    Assert.DoesNotContain("int Hour", code);
    Assert.DoesNotContain("int Minute", code);
  }

  [Fact]
  public void GenerateCode_WithNullableField_GeneratesNullableType()
  {
    var columns = new[]
    {
      new SchemaColumn { Name = "Description", JsonType = "string", IsNullable = true }
    };

    var code = Service.GenerateCode("Test", columns);

    Assert.Contains("public string? Description { get; init; }", code);
    Assert.DoesNotContain("string.Empty", code);
  }
}
