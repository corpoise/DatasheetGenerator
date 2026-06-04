namespace DatasheetGenerator.Tests;

using DatasheetGenerator.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class SchemaServiceTests
{
  [Fact]
  public void ParseSchema_WithFlatSchema_ReturnsCorrectColumns()
  {
    var service = new SchemaService();
    var schema = CreateFlatSchema();

    var columns = service.ParseSchema(schema);

    Assert.Equal(2, columns.Count);
    Assert.Equal("Id", columns[0].Name);
    Assert.Equal("integer", columns[0].JsonType);
    Assert.Equal("Name", columns[1].Name);
    Assert.Equal("string", columns[1].JsonType);
  }

  [Fact]
  public void ParseSchema_WithNestedSchema_ReturnsCorrectChildren()
  {
    var service = new SchemaService();
    var schema = CreateNestedSchema();

    var columns = service.ParseSchema(schema);

    Assert.Equal(2, columns.Count);
    Assert.Equal("Id", columns[0].Name);
    Assert.Equal("Stat", columns[1].Name);
    Assert.Equal("object", columns[1].JsonType);
    Assert.Equal(2, columns[1].Children.Count);
    Assert.Equal("Type", columns[1].Children[0].Name);
    Assert.Equal("Value", columns[1].Children[1].Name);
  }

  [Fact]
  public void ParseSchema_WithRequiredFields_SetsIsRequired()
  {
    var service = new SchemaService();
    var schema = CreateFlatSchema();

    var columns = service.ParseSchema(schema);

    Assert.True(columns[0].IsRequired);
    Assert.False(columns[1].IsRequired);
  }

  [Fact]
  public void ParseSchema_WithNullableField_SetsIsNullable()
  {
    var service = new SchemaService();
    var schemaText = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Name"] = new JObject { ["type"] = new JArray("string", "null") }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schemaText);

    Assert.True(columns[0].IsNullable);
  }

  [Fact]
  public void ParseSchema_WithNumberField_ReturnsNumberType()
  {
    var service = new SchemaService();
    var schemaText = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Scale"] = new JObject { ["type"] = "number" }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schemaText);

    Assert.Equal("number", columns[0].JsonType);
  }

  [Fact]
  public void ValidateSchemaText_WithValidSchema_ReturnsSuccess()
  {
    var service = new SchemaService();

    var result = service.ValidateSchemaText(CreateFlatSchema());

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithInvalidJson_ReturnsFailure()
  {
    var service = new SchemaService();

    var result = service.ValidateSchemaText("{ invalid json }");

    Assert.False(result.IsValid);
    Assert.Contains("Invalid JSON", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithNonObjectRoot_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "array",
      ["items"] = new JObject()
    }.ToString();

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("object", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithNoProperties_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject()
    }.ToString();

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("properties", result.Message);
  }

  [Fact]
  public void GetSchemaFiles_WhenDirectoryDoesNotExist_ReturnsEmpty()
  {
    var service = new SchemaService();

    var files = service.GetSchemaFiles(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}"));

    Assert.Empty(files);
  }

  [Fact]
  public void SaveAndLoad_RoundTrip_PreservesContent()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"SchemaServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new SchemaService();
      var schemaText = CreateFlatSchema();
      var path = Path.Combine(directory, "test.schema.json");

      service.SaveSchema(path, schemaText);
      var loaded = service.LoadSchemaText(path);

      Assert.Equal(schemaText, loaded);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  private static string CreateFlatSchema()
  {
    return new JObject
    {
      ["type"] = "object",
      ["domain"] = new JArray("client", "server"),
      ["required"] = new JArray("Id"),
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer" },
        ["Name"] = new JObject { ["type"] = "string" }
      }
    }.ToString(Formatting.Indented);
  }

  [Fact]
  public void ParseSchema_WithArrayItemName_ParsesItemName()
  {
    var service = new SchemaService();
    var schemaText = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Stats"] = new JObject
        {
          ["type"] = "array",
          ["minItems"] = 1,
          ["maxItems"] = 3,
          ["itemName"] = "Stat",
          ["items"] = new JObject { ["type"] = "integer" }
        }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schemaText);

    Assert.Equal("Stat", columns[0].ItemName);
  }

  [Fact]
  public void ParseSchema_WithArrayProperty_ReturnsCorrectArrayColumn()
  {
    var service = new SchemaService();
    var schemaText = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Tags"] = new JObject
        {
          ["type"] = "array",
          ["minItems"] = 1,
          ["maxItems"] = 3,
          ["items"] = new JObject { ["type"] = "string" }
        }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schemaText);

    Assert.Single(columns);
    Assert.Equal("Tags", columns[0].Name);
    Assert.Equal("array", columns[0].JsonType);
    Assert.Equal(1, columns[0].ArrayMin);
    Assert.Equal(3, columns[0].ArrayMax);
    Assert.Equal("string", columns[0].ItemJsonType);
  }

  [Fact]
  public void ValidateSchemaText_WithArrayWithoutMinMax_ReturnsFailure()
  {
    var service = new SchemaService();
    var schemaText = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Tags"] = new JObject
        {
          ["type"] = "array",
          ["items"] = new JObject { ["type"] = "string" }
        }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schemaText);

    Assert.False(result.IsValid);
    Assert.Contains("Tags", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithValidArraySchema_ReturnsSuccess()
  {
    var service = new SchemaService();
    var schemaText = new JObject
    {
      ["type"] = "object",
      ["domain"] = new JArray("client", "server"),
      ["properties"] = new JObject
      {
        ["Tags"] = new JObject
        {
          ["type"] = "array",
          ["minItems"] = 1,
          ["maxItems"] = 3,
          ["items"] = new JObject { ["type"] = "string" }
        }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schemaText);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ParseSchema_WithArrayOfObjectItems_SetsItemChildren()
  {
    var service = new SchemaService();

    var columns = service.ParseSchema(CreateArrayOfObjectsSchema());

    var stat = columns.Single(c => c.Name is "Stat");
    Assert.Equal("array", stat.JsonType);
    Assert.Equal("object", stat.ItemJsonType);
    Assert.Equal(2, stat.ItemChildren.Count);
    Assert.Equal("Type", stat.ItemChildren[0].Name);
    Assert.Equal("string", stat.ItemChildren[0].JsonType);
    Assert.Equal("Value", stat.ItemChildren[1].Name);
    Assert.Equal("integer", stat.ItemChildren[1].JsonType);
  }

  [Fact]
  public void ParseSchema_WithArrayOfObjectItems_SetsRequiredOnChildren()
  {
    var service = new SchemaService();

    var columns = service.ParseSchema(CreateArrayOfObjectsSchema());

    var stat = columns.Single(c => c.Name is "Stat");
    Assert.True(stat.ItemChildren[0].IsRequired);
    Assert.True(stat.ItemChildren[1].IsRequired);
  }

  private static string CreateArrayOfObjectsSchema()
  {
    return new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Stat"] = new JObject
        {
          ["type"] = "array",
          ["minItems"] = 1,
          ["maxItems"] = 4,
          ["items"] = new JObject
          {
            ["type"] = "object",
            ["properties"] = new JObject
            {
              ["Type"] = new JObject { ["type"] = "string" },
              ["Value"] = new JObject { ["type"] = "integer" }
            },
            ["required"] = new JArray("Type", "Value")
          }
        }
      }
    }.ToString(Formatting.Indented);
  }

  [Fact]
  public void RenameSchema_WhenOnlySchemaExists_RenamesSchemaFile()
  {
    var (schemaDir, jsonDir, excelDir) = CreateTempRenameDirs();
    try
    {
      var service = new SchemaService();
      File.WriteAllText(Path.Combine(schemaDir, "Old.schema.json"), "{}");

      var result = service.RenameSchema("Old", "New", schemaDir, jsonDir, excelDir);

      Assert.True(result.IsValid);
      Assert.True(File.Exists(Path.Combine(schemaDir, "New.schema.json")));
      Assert.False(File.Exists(Path.Combine(schemaDir, "Old.schema.json")));
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(schemaDir)!, true);
    }
  }

  [Fact]
  public void RenameSchema_WhenAllFilesExist_RenamesAllFiles()
  {
    var (schemaDir, jsonDir, excelDir) = CreateTempRenameDirs();
    try
    {
      var service = new SchemaService();
      File.WriteAllText(Path.Combine(schemaDir, "Old.schema.json"), "{}");
      File.WriteAllText(Path.Combine(jsonDir, "Old.json"), "[]");
      File.WriteAllText(Path.Combine(excelDir, "Old.xlsx"), "fake");

      var result = service.RenameSchema("Old", "New", schemaDir, jsonDir, excelDir);

      Assert.True(result.IsValid);
      Assert.True(File.Exists(Path.Combine(schemaDir, "New.schema.json")));
      Assert.True(File.Exists(Path.Combine(jsonDir, "New.json")));
      Assert.True(File.Exists(Path.Combine(excelDir, "New.xlsx")));
      Assert.False(File.Exists(Path.Combine(schemaDir, "Old.schema.json")));
      Assert.False(File.Exists(Path.Combine(jsonDir, "Old.json")));
      Assert.False(File.Exists(Path.Combine(excelDir, "Old.xlsx")));
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(schemaDir)!, true);
    }
  }

  [Fact]
  public void RenameSchema_WhenJsonExistsButExcelDoesNot_SkipsExcel()
  {
    var (schemaDir, jsonDir, excelDir) = CreateTempRenameDirs();
    try
    {
      var service = new SchemaService();
      File.WriteAllText(Path.Combine(schemaDir, "Old.schema.json"), "{}");
      File.WriteAllText(Path.Combine(jsonDir, "Old.json"), "[]");

      var result = service.RenameSchema("Old", "New", schemaDir, jsonDir, excelDir);

      Assert.True(result.IsValid);
      Assert.True(File.Exists(Path.Combine(schemaDir, "New.schema.json")));
      Assert.True(File.Exists(Path.Combine(jsonDir, "New.json")));
      Assert.False(File.Exists(Path.Combine(excelDir, "New.xlsx")));
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(schemaDir)!, true);
    }
  }

  [Fact]
  public void RenameSchema_WhenTargetSchemaExists_ReturnsFailure()
  {
    var (schemaDir, jsonDir, excelDir) = CreateTempRenameDirs();
    try
    {
      var service = new SchemaService();
      File.WriteAllText(Path.Combine(schemaDir, "Old.schema.json"), "{}");
      File.WriteAllText(Path.Combine(schemaDir, "New.schema.json"), "{}");

      var result = service.RenameSchema("Old", "New", schemaDir, jsonDir, excelDir);

      Assert.False(result.IsValid);
      Assert.True(File.Exists(Path.Combine(schemaDir, "Old.schema.json")));
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(schemaDir)!, true);
    }
  }

  [Fact]
  public void RenameSchema_WhenTargetJsonExists_ReturnsFailure()
  {
    var (schemaDir, jsonDir, excelDir) = CreateTempRenameDirs();
    try
    {
      var service = new SchemaService();
      File.WriteAllText(Path.Combine(schemaDir, "Old.schema.json"), "{}");
      File.WriteAllText(Path.Combine(jsonDir, "Old.json"), "[]");
      File.WriteAllText(Path.Combine(jsonDir, "New.json"), "[]");

      var result = service.RenameSchema("Old", "New", schemaDir, jsonDir, excelDir);

      Assert.False(result.IsValid);
      Assert.True(File.Exists(Path.Combine(schemaDir, "Old.schema.json")));
      Assert.True(File.Exists(Path.Combine(jsonDir, "Old.json")));
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(schemaDir)!, true);
    }
  }

  [Fact]
  public void RenameSchema_WhenTargetExcelExists_ReturnsFailure()
  {
    var (schemaDir, jsonDir, excelDir) = CreateTempRenameDirs();
    try
    {
      var service = new SchemaService();
      File.WriteAllText(Path.Combine(schemaDir, "Old.schema.json"), "{}");
      File.WriteAllText(Path.Combine(excelDir, "Old.xlsx"), "fake");
      File.WriteAllText(Path.Combine(excelDir, "New.xlsx"), "fake");

      var result = service.RenameSchema("Old", "New", schemaDir, jsonDir, excelDir);

      Assert.False(result.IsValid);
      Assert.True(File.Exists(Path.Combine(schemaDir, "Old.schema.json")));
      Assert.True(File.Exists(Path.Combine(excelDir, "Old.xlsx")));
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(schemaDir)!, true);
    }
  }

  [Fact]
  public void RenameSchema_WhenDomainJsonExists_RenamesDomainJsonFile()
  {
    var (schemaDir, jsonDir, excelDir) = CreateTempRenameDirs();
    try
    {
      var domainDir = Path.Combine(Path.GetDirectoryName(schemaDir)!, "domain");
      Directory.CreateDirectory(domainDir);
      var service = new SchemaService();
      File.WriteAllText(Path.Combine(schemaDir, "Old.schema.json"), "{}");
      File.WriteAllText(Path.Combine(domainDir, "Old.json"), "[]");

      var result = service.RenameSchema("Old", "New", schemaDir, jsonDir, excelDir, [domainDir]);

      Assert.True(result.IsValid);
      Assert.True(File.Exists(Path.Combine(domainDir, "New.json")));
      Assert.False(File.Exists(Path.Combine(domainDir, "Old.json")));
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(schemaDir)!, true);
    }
  }

  [Fact]
  public void RenameSchema_WhenTargetDomainJsonExists_ReturnsFailure()
  {
    var (schemaDir, jsonDir, excelDir) = CreateTempRenameDirs();
    try
    {
      var domainDir = Path.Combine(Path.GetDirectoryName(schemaDir)!, "domain");
      Directory.CreateDirectory(domainDir);
      var service = new SchemaService();
      File.WriteAllText(Path.Combine(schemaDir, "Old.schema.json"), "{}");
      File.WriteAllText(Path.Combine(domainDir, "Old.json"), "[]");
      File.WriteAllText(Path.Combine(domainDir, "New.json"), "[]");

      var result = service.RenameSchema("Old", "New", schemaDir, jsonDir, excelDir, [domainDir]);

      Assert.False(result.IsValid);
      Assert.True(File.Exists(Path.Combine(domainDir, "Old.json")));
    }
    finally
    {
      Directory.Delete(Path.GetDirectoryName(schemaDir)!, true);
    }
  }

  private static (string SchemaDir, string JsonDir, string ExcelDir) CreateTempRenameDirs()
  {
    var root = Path.Combine(Path.GetTempPath(), $"SchemaRename_{Guid.NewGuid():N}");
    var schemaDir = Path.Combine(root, "schema");
    var jsonDir = Path.Combine(root, "json");
    var excelDir = Path.Combine(root, "excel");
    Directory.CreateDirectory(schemaDir);
    Directory.CreateDirectory(jsonDir);
    Directory.CreateDirectory(excelDir);
    return (schemaDir, jsonDir, excelDir);
  }

  [Fact]
  public void ParsePivotInfo_WhenNoPivotColumn_ReturnsNull()
  {
    var service = new SchemaService();

    var result = service.ParsePivotInfo(CreateFlatSchema());

    Assert.Null(result);
  }

  [Fact]
  public void ParsePivotInfo_WhenBothPivotFieldsPresent_ReturnsPivotInfo()
  {
    var service = new SchemaService();

    var result = service.ParsePivotInfo(CreatePivotSchema("Name", "LevelData"));

    Assert.NotNull(result);
    Assert.Equal("Name", result.PivotColumn);
    Assert.Equal("LevelData", result.PivotName);
  }

  [Fact]
  public void ValidateSchemaText_WithPivotColumnButNoPivotName_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["PivotColumn"] = "Name",
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Name"] = new JObject { ["type"] = "string" },
        ["Level"] = new JObject { ["type"] = "integer" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("PivotName", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WhenRequiredFieldNotInProperties_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["required"] = new JArray("Id", "Ghost"),
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("Ghost", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WhenNestedObjectRequiredFieldNotInProperties_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Stat"] = new JObject
        {
          ["type"] = "object",
          ["required"] = new JArray("Type", "Ghost"),
          ["properties"] = new JObject
          {
            ["Type"] = new JObject { ["type"] = "string" }
          }
        }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("Ghost", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WhenArrayItemRequiredFieldNotInProperties_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Tags"] = new JObject
        {
          ["type"] = "array",
          ["minItems"] = 1,
          ["maxItems"] = 3,
          ["items"] = new JObject
          {
            ["type"] = "object",
            ["required"] = new JArray("Id", "Ghost"),
            ["properties"] = new JObject
            {
              ["Id"] = new JObject { ["type"] = "integer" }
            }
          }
        }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithValidRef_Passes()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["domain"] = new JArray("client", "server"),
      ["properties"] = new JObject
      {
        ["Name"] = new JObject { ["type"] = "string" },
        ["SkillSet"] = new JObject { ["ref"] = "TowerSkillSet.Name" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithRefInvalidFormat_NoDot_Fails()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["SkillSet"] = new JObject { ["ref"] = "TowerSkillSetName" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithRefInvalidFormat_TooManyParts_Fails()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["SkillSet"] = new JObject { ["ref"] = "A.B.C" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
  }

  [Fact]
  public void ValidateSchemaText_RefOnArrayColumn_Fails()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Items"] = new JObject
        {
          ["type"] = "array",
          ["minItems"] = 0,
          ["maxItems"] = 3,
          ["ref"] = "OtherSchema.Name",
          ["items"] = new JObject { ["type"] = "string" }
        }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
  }

  [Fact]
  public void ValidateSchemaText_RefOnObjectColumn_Fails()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Info"] = new JObject
        {
          ["type"] = "object",
          ["ref"] = "OtherSchema.Name",
          ["properties"] = new JObject
          {
            ["Value"] = new JObject { ["type"] = "string" }
          }
        }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
  }

  [Fact]
  public void ParseSchema_WithRef_SetsRefProperty()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["SkillSet"] = new JObject { ["ref"] = "TowerSkillSet.Name" }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schema);

    Assert.Single(columns);
    Assert.Equal("TowerSkillSet.Name", columns[0].Ref);
  }

  [Fact]
  public void ValidateSchemaText_WithNonObjectPropertyInNestedObject_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Outer"] = new JObject
        {
          ["type"] = "object",
          ["properties"] = new JObject
          {
            ["Name"] = new JObject { ["type"] = "string" },
            ["ref"] = "SomeSchema.col"
          }
        }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithNonObjectPropertyInArrayItems_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Items"] = new JObject
        {
          ["type"] = "array",
          ["minItems"] = 0,
          ["maxItems"] = 3,
          ["items"] = new JObject
          {
            ["type"] = "object",
            ["properties"] = new JObject
            {
              ["Name"] = new JObject { ["type"] = "string" },
              ["ref"] = "SomeSchema.col"
            }
          }
        }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
  }

  [Fact]
  public void ParseSchema_WithNonObjectPropertyInNestedObject_SkipsProperty()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Outer"] = new JObject
        {
          ["type"] = "object",
          ["properties"] = new JObject
          {
            ["Name"] = new JObject { ["type"] = "string" },
            ["ref"] = "SomeSchema.col"
          }
        }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schema);

    var outer = columns.Single(c => c.Name is "Outer");
    Assert.Single(outer.Children);
    Assert.Equal("Name", outer.Children[0].Name);
  }

  [Fact]
  public void ParseSchema_WithNonObjectPropertyInArrayItems_SkipsProperty()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Items"] = new JObject
        {
          ["type"] = "array",
          ["minItems"] = 0,
          ["maxItems"] = 3,
          ["items"] = new JObject
          {
            ["type"] = "object",
            ["properties"] = new JObject
            {
              ["Name"] = new JObject { ["type"] = "string" },
              ["ref"] = "SomeSchema.col"
            }
          }
        }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schema);

    var items = columns.Single(c => c.Name is "Items");
    Assert.Single(items.ItemChildren);
    Assert.Equal("Name", items.ItemChildren[0].Name);
  }

  [Fact]
  public void ValidateSchemaText_WhenPivotColumnNotInProperties_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["PivotColumn"] = "NonExistent",
      ["PivotName"] = "LevelData",
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Name"] = new JObject { ["type"] = "string" },
        ["Level"] = new JObject { ["type"] = "integer" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("NonExistent", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithBothPivotFields_ReturnsSuccess()
  {
    var service = new SchemaService();

    var result = service.ValidateSchemaText(CreatePivotSchema("Name", "LevelData"));

    Assert.True(result.IsValid);
  }

  private static string CreatePivotSchema(string pivotColumn, string pivotName)
  {
    return new JObject
    {
      ["PivotColumn"] = pivotColumn,
      ["PivotName"] = pivotName,
      ["type"] = "object",
      ["domain"] = new JArray("client", "server"),
      ["properties"] = new JObject
      {
        ["Name"] = new JObject { ["type"] = "string" },
        ["Level"] = new JObject { ["type"] = "integer" },
        ["RequireExp"] = new JObject { ["type"] = "integer" }
      }
    }.ToString(Formatting.Indented);
  }

  [Fact]
  public void ValidateSchemaText_WithUnknownRootDomain_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["domain"] = new JArray("client", "unknown"),
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema, ["client", "server"]);

    Assert.False(result.IsValid);
    Assert.Contains("unknown", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithUnknownPropertyDomain_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["domain"] = new JArray("client", "server"),
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer" },
        ["Extra"] = new JObject { ["type"] = "string", ["domain"] = new JArray("mobile") }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema, ["client", "server"]);

    Assert.False(result.IsValid);
    Assert.Contains("mobile", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithAllowedDomains_AllValid_Passes()
  {
    var service = new SchemaService();

    var result = service.ValidateSchemaText(CreateFlatSchema(), ["client", "server"]);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WhenAllowedDomainsIsNull_SkipsConfigCheck()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["domain"] = new JArray("anything"),
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema, null);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithNoDomain_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("domain", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithEmptyDomainArray_ReturnsFailure()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["domain"] = new JArray(),
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("domain", result.Message);
  }

  [Fact]
  public void ParseSchemaDomains_WithDomainArray_ReturnsValues()
  {
    var service = new SchemaService();

    var result = service.ParseSchemaDomains(CreateFlatSchema());

    Assert.Equal(2, result.Count);
    Assert.Contains("client", result);
    Assert.Contains("server", result);
  }

  [Fact]
  public void ParseSchemaDomains_WithNoDomainKey_ReturnsEmpty()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ParseSchemaDomains(schema);

    Assert.Empty(result);
  }

  [Fact]
  public void ParseSchema_WithDomainOnProperty_SetsDomainOnColumn()
  {
    var service = new SchemaService();
    var schema = new JObject
    {
      ["type"] = "object",
      ["domain"] = new JArray("client", "server"),
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer" },
        ["ClientOnly"] = new JObject { ["type"] = "string", ["domain"] = new JArray("client") }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schema);

    Assert.Null(columns[0].Domain);
    Assert.NotNull(columns[1].Domain);
    Assert.Single(columns[1].Domain!);
    Assert.Equal("client", columns[1].Domain![0]);
  }

  private static string CreateNestedSchema()
  {
    return new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Id"] = new JObject { ["type"] = "integer" },
        ["Stat"] = new JObject
        {
          ["type"] = "object",
          ["properties"] = new JObject
          {
            ["Type"] = new JObject { ["type"] = "string" },
            ["Value"] = new JObject { ["type"] = "integer" }
          }
        }
      }
    }.ToString(Formatting.Indented);
  }
}
