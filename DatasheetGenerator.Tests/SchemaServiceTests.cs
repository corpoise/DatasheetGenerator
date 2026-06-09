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
        ["SkillSet"] = new JObject { ["ref"] = "TowerSkillSet.schema.json#/definitions/Name" }
      }
    }.ToString(Formatting.Indented);

    var result = service.ValidateSchemaText(schema);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithRefInvalidFormat_MissingSeparator_Fails()
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
  public void ValidateSchemaText_WithRefInvalidFormat_WrongSeparator_Fails()
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
          ["ref"] = "OtherSchema.schema.json#/definitions/Name",
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
          ["ref"] = "OtherSchema.schema.json#/definitions/Name",
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
        ["SkillSet"] = new JObject { ["ref"] = "TowerSkillSet.schema.json#/definitions/Name" }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schema);

    Assert.Single(columns);
    Assert.Equal("TowerSkillSet.schema.json#/definitions/Name", columns[0].Ref);
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

  // ── Custom Format: Parse ──────────────────────────────

  [Fact]
  public void ParseSchema_WithVector2Format_SetsFormat()
  {
    var service = new SchemaService();
    var schemaText = new JObject
    {
      ["type"] = "object",
      ["properties"] = new JObject
      {
        ["Position"] = new JObject
        {
          ["type"] = "object",
          ["format"] = "vector2",
          ["properties"] = new JObject
          {
            ["x"] = new JObject { ["type"] = "number" },
            ["y"] = new JObject { ["type"] = "number" }
          }
        }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schemaText);

    Assert.Equal("vector2", columns[0].Format);
  }

  [Fact]
  public void ParseSchema_WithFrozenDictionaryFormat_SetsFormatAndKeyColumn()
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
          ["format"] = "frozen-dictionary",
          ["keyColumn"] = "Id",
          ["minItems"] = 0,
          ["maxItems"] = 10,
          ["items"] = new JObject
          {
            ["type"] = "object",
            ["properties"] = new JObject
            {
              ["Id"] = new JObject { ["type"] = "string" },
              ["Value"] = new JObject { ["type"] = "integer" }
            }
          }
        }
      }
    }.ToString(Formatting.Indented);

    var columns = service.ParseSchema(schemaText);

    Assert.Equal("frozen-dictionary", columns[0].Format);
    Assert.Equal("Id", columns[0].KeyColumn);
  }

  // ── Custom Format: Validation — vector2 ──────────────

  [Fact]
  public void ValidateSchemaText_WithVector2_ValidProperties_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Position", new JObject
    {
      ["type"] = "object",
      ["format"] = "vector2",
      ["properties"] = new JObject
      {
        ["x"] = new JObject { ["type"] = "number" },
        ["y"] = new JObject { ["type"] = "number" }
      }
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithVector2_TypeNotObject_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Position", new JObject
    {
      ["type"] = "integer",
      ["format"] = "vector2"
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("vector", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithVector2_MissingX_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Position", new JObject
    {
      ["type"] = "object",
      ["format"] = "vector2",
      ["properties"] = new JObject
      {
        ["y"] = new JObject { ["type"] = "number" }
      }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("x", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithVector2_MissingY_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Position", new JObject
    {
      ["type"] = "object",
      ["format"] = "vector2",
      ["properties"] = new JObject
      {
        ["x"] = new JObject { ["type"] = "number" }
      }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("y", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithVector2_XNotNumber_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Position", new JObject
    {
      ["type"] = "object",
      ["format"] = "vector2",
      ["properties"] = new JObject
      {
        ["x"] = new JObject { ["type"] = "string" },
        ["y"] = new JObject { ["type"] = "number" }
      }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
  }

  // ── Custom Format: Validation — vector3 ──────────────

  [Fact]
  public void ValidateSchemaText_WithVector3_ValidProperties_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Position", new JObject
    {
      ["type"] = "object",
      ["format"] = "vector3",
      ["properties"] = new JObject
      {
        ["x"] = new JObject { ["type"] = "number" },
        ["y"] = new JObject { ["type"] = "number" },
        ["z"] = new JObject { ["type"] = "number" }
      }
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithVector3_MissingZ_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Position", new JObject
    {
      ["type"] = "object",
      ["format"] = "vector3",
      ["properties"] = new JObject
      {
        ["x"] = new JObject { ["type"] = "number" },
        ["y"] = new JObject { ["type"] = "number" }
      }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("z", result.Message);
  }

  // ── Custom Format: Validation — datetime ──────────────

  [Fact]
  public void ValidateSchemaText_WithDatetime_TypeString_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("CreatedAt", new JObject
    {
      ["type"] = "string",
      ["format"] = "datetime"
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithDatetime_TypeNotString_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("CreatedAt", new JObject
    {
      ["type"] = "integer",
      ["format"] = "datetime"
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("datetime", result.Message);
  }

  // ── Custom Format: Validation — timespan ──────────────

  [Fact]
  public void ValidateSchemaText_WithTimespanSecond_TypeInteger_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Duration", new JObject
    {
      ["type"] = "integer",
      ["format"] = "timespan-second"
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithTimespanMillisecond_TypeInteger_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Duration", new JObject
    {
      ["type"] = "integer",
      ["format"] = "timespan-millisecond"
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithTimespanMinute_TypeInteger_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Duration", new JObject
    {
      ["type"] = "integer",
      ["format"] = "timespan-minute"
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithTimespanHour_TypeInteger_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Duration", new JObject
    {
      ["type"] = "integer",
      ["format"] = "timespan-hour"
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithTimespanSecond_TypeNotInteger_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Duration", new JObject
    {
      ["type"] = "string",
      ["format"] = "timespan-second"
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("timespan", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithTimespanObject_TypeObject_WithAllComponents_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Cooldown", new JObject
    {
      ["type"] = "object",
      ["format"] = "timespan",
      ["properties"] = new JObject
      {
        ["Hour"] = new JObject { ["type"] = "integer" },
        ["Minute"] = new JObject { ["type"] = "integer" },
        ["Second"] = new JObject { ["type"] = "integer" },
        ["Millisecond"] = new JObject { ["type"] = "integer" }
      }
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithTimespanObject_TypeNotObject_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Cooldown", new JObject
    {
      ["type"] = "integer",
      ["format"] = "timespan"
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("timespan", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithTimespanObject_MissingComponent_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Cooldown", new JObject
    {
      ["type"] = "object",
      ["format"] = "timespan",
      ["properties"] = new JObject
      {
        ["Hour"] = new JObject { ["type"] = "integer" },
        ["Minute"] = new JObject { ["type"] = "integer" },
        ["Second"] = new JObject { ["type"] = "integer" }
      }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("Millisecond", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithTimespanObject_ComponentNotInteger_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Cooldown", new JObject
    {
      ["type"] = "object",
      ["format"] = "timespan",
      ["properties"] = new JObject
      {
        ["Hour"] = new JObject { ["type"] = "number" },
        ["Minute"] = new JObject { ["type"] = "integer" },
        ["Second"] = new JObject { ["type"] = "integer" },
        ["Millisecond"] = new JObject { ["type"] = "integer" }
      }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("integer", result.Message);
  }

  // ── Custom Format: Validation — readonly-list ──────────

  [Fact]
  public void ValidateSchemaText_WithReadonlyList_TypeArray_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Items", new JObject
    {
      ["type"] = "array",
      ["format"] = "readonly-list",
      ["minItems"] = 0,
      ["maxItems"] = 10,
      ["items"] = new JObject { ["type"] = "string" }
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithReadonlyList_TypeNotArray_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Items", new JObject
    {
      ["type"] = "object",
      ["format"] = "readonly-list",
      ["properties"] = new JObject
      {
        ["x"] = new JObject { ["type"] = "string" }
      }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("readonly-list", result.Message);
  }

  // ── Custom Format: Validation — frozen-dictionary ──────

  [Fact]
  public void ValidateSchemaText_WithFrozenDictionary_ValidSchema_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Stats", new JObject
    {
      ["type"] = "array",
      ["format"] = "frozen-dictionary",
      ["keyColumn"] = "Id",
      ["minItems"] = 0,
      ["maxItems"] = 10,
      ["items"] = new JObject
      {
        ["type"] = "object",
        ["properties"] = new JObject
        {
          ["Id"] = new JObject { ["type"] = "string" },
          ["Value"] = new JObject { ["type"] = "integer" }
        }
      }
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithFrozenDictionary_NoKeyColumn_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Stats", new JObject
    {
      ["type"] = "array",
      ["format"] = "frozen-dictionary",
      ["minItems"] = 0,
      ["maxItems"] = 10,
      ["items"] = new JObject
      {
        ["type"] = "object",
        ["properties"] = new JObject
        {
          ["Id"] = new JObject { ["type"] = "string" }
        }
      }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("keyColumn", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithFrozenDictionary_KeyColumnNotInItemsProperties_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Stats", new JObject
    {
      ["type"] = "array",
      ["format"] = "frozen-dictionary",
      ["keyColumn"] = "NonExistent",
      ["minItems"] = 0,
      ["maxItems"] = 10,
      ["items"] = new JObject
      {
        ["type"] = "object",
        ["properties"] = new JObject
        {
          ["Id"] = new JObject { ["type"] = "string" }
        }
      }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("NonExistent", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithFrozenDictionary_ItemsNotObject_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Stats", new JObject
    {
      ["type"] = "array",
      ["format"] = "frozen-dictionary",
      ["keyColumn"] = "Id",
      ["minItems"] = 0,
      ["maxItems"] = 10,
      ["items"] = new JObject { ["type"] = "string" }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
  }

  // ── Custom Format: Validation — unknown ──────────────

  [Fact]
  public void ValidateSchemaText_WithUnknownFormat_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("Field", new JObject
    {
      ["type"] = "string",
      ["format"] = "unsupported-format"
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("unsupported-format", result.Message);
  }

  // ── enum schema ref: Validation ──────────────────────

  [Fact]
  public void ValidateSchemaText_WithEnumSchemaRef_Passes()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("EnemyType", new JObject
    {
      ["type"] = "string",
      ["ref"] = "enum.schema.json#/definitions/MonsterType"
    });

    Assert.True(service.ValidateSchemaText(schema).IsValid);
  }

  [Fact]
  public void ValidateSchemaText_WithEnumSchemaRefEmptyTypeName_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("EnemyType", new JObject
    {
      ["type"] = "string",
      ["ref"] = "enum.schema.json#/definitions/"
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("컬럼 경로", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithEnumSchemaRefOnObjectType_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("EnemyType", new JObject
    {
      ["type"] = "object",
      ["ref"] = "enum.schema.json#/definitions/MonsterType",
      ["properties"] = new JObject { ["x"] = new JObject { ["type"] = "number" } }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("ref", result.Message);
  }

  [Fact]
  public void ValidateSchemaText_WithEnumSchemaRefOnArrayType_Fails()
  {
    var service = new SchemaService();
    var schema = BuildSchemaWithProperty("EnemyType", new JObject
    {
      ["type"] = "array",
      ["ref"] = "enum.schema.json#/definitions/MonsterType",
      ["minItems"] = 0,
      ["maxItems"] = 5,
      ["items"] = new JObject { ["type"] = "string" }
    });

    var result = service.ValidateSchemaText(schema);

    Assert.False(result.IsValid);
    Assert.Contains("ref", result.Message);
  }

  private static string BuildSchemaWithProperty(string name, JObject propObj)
  {
    return new JObject
    {
      ["type"] = "object",
      ["domain"] = new JArray("client"),
      ["properties"] = new JObject
      {
        [name] = propObj
      }
    }.ToString(Formatting.Indented);
  }
}
