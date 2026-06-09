namespace DatasheetGenerator.Tests;

using System.Data;
using System.IO.Compression;
using DatasheetGenerator.Models;
using DatasheetGenerator.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class DataEntryServiceTests
{
  [Fact]
  public void GetFlatColumns_WithFlatSchema_ReturnsRootColumns()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn { Name = "Id", JsonType = "integer" },
      new SchemaColumn { Name = "Name", JsonType = "string" }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.Equal(2, flat.Count);
    Assert.Equal("Id", flat[0].Path);
    Assert.Equal("Id", flat[0].LeafName);
    Assert.Equal(string.Empty, flat[0].Level1Group);
    Assert.Equal("Name", flat[1].Path);
  }

  [Fact]
  public void GetFlatColumns_WithNestedSchema_ReturnsCorrectPaths()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn { Name = "Id", JsonType = "integer" },
      new SchemaColumn
      {
        Name = "Stat",
        JsonType = "object",
        Children = new[]
        {
          new SchemaColumn { Name = "Type", JsonType = "string" },
          new SchemaColumn { Name = "Value", JsonType = "integer" }
        }
      }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.Equal(3, flat.Count);
    Assert.Equal("Id", flat[0].Path);
    Assert.Equal(string.Empty, flat[0].Level1Group);
    Assert.Equal("Stat.Type", flat[1].Path);
    Assert.Equal("Type", flat[1].LeafName);
    Assert.Equal("Stat", flat[1].Level1Group);
    Assert.Equal(string.Empty, flat[1].Level2Group);
    Assert.Equal("Stat.Value", flat[2].Path);
  }

  [Fact]
  public void GetFlatColumns_WithDeepNestedSchema_SetsLevel1AndLevel2()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn
      {
        Name = "Object",
        JsonType = "object",
        Children = new[]
        {
          new SchemaColumn
          {
            Name = "Inner",
            JsonType = "object",
            Children = new[]
            {
              new SchemaColumn { Name = "Value", JsonType = "integer" }
            }
          }
        }
      }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.Single(flat);
    Assert.Equal("Object.Inner.Value", flat[0].Path);
    Assert.Equal("Object", flat[0].Level1Group);
    Assert.Equal("Inner", flat[0].Level2Group);
    Assert.Equal("Value", flat[0].LeafName);
  }

  [Fact]
  public void ValidateCellValue_WhenRequiredAndEmpty_ReturnsFailure()
  {
    var column = new FlatColumn { LeafName = "Id", JsonType = "integer", IsRequired = true, IsNullable = false };

    var result = DataEntryService.ValidateCellValue(column, string.Empty);

    Assert.False(result.IsValid);
    Assert.Contains("Id", result.Message);
  }

  [Fact]
  public void ValidateCellValue_WhenNullableAndEmpty_ReturnsSuccess()
  {
    var column = new FlatColumn { LeafName = "Name", JsonType = "string", IsRequired = false, IsNullable = true };

    var result = DataEntryService.ValidateCellValue(column, string.Empty);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateCellValue_WhenIntegerWithNonNumeric_ReturnsFailure()
  {
    var column = new FlatColumn { LeafName = "Id", JsonType = "integer" };

    var result = DataEntryService.ValidateCellValue(column, "abc");

    Assert.False(result.IsValid);
    Assert.Contains("integer", result.Message);
  }

  [Fact]
  public void ValidateCellValue_WhenIntegerWithNumeric_ReturnsSuccess()
  {
    var column = new FlatColumn { LeafName = "Id", JsonType = "integer" };

    var result = DataEntryService.ValidateCellValue(column, "42");

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateCellValue_WhenNumberWithDecimal_ReturnsSuccess()
  {
    var column = new FlatColumn { LeafName = "RouteYScale", JsonType = "number" };

    var result = DataEntryService.ValidateCellValue(column, "0.7");

    Assert.True(result.IsValid);
  }

  [Fact]
  public void SaveData_WritesCorrectJsonFile()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var (schemaColumns, flatColumns) = CreateSampleSchema();
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["Id"] = "1";
      row["Name"] = "Test";
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "test.json");
      var xlsxPath = Path.Combine(directory, "test.xlsx");

      service.SaveData(table, schemaColumns, flatColumns, jsonPath, xlsxPath);

      var json = JArray.Parse(File.ReadAllText(jsonPath));
      Assert.Single(json);
      Assert.Equal(1, json[0]?["Id"]?.Value<int>());
      Assert.Equal("Test", json[0]?["Name"]?.Value<string>());
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void SaveData_WithNestedColumns_WritesNestedJson()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Id", JsonType = "integer" },
        new SchemaColumn
        {
          Name = "Stat",
          JsonType = "object",
          Children = new[]
          {
            new SchemaColumn { Name = "Type", JsonType = "string" },
            new SchemaColumn { Name = "Value", JsonType = "integer" }
          }
        }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["Id"] = "1";
      row["Stat.Type"] = "Hp";
      row["Stat.Value"] = "100";
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "test.json");
      var xlsxPath = Path.Combine(directory, "test.xlsx");

      service.SaveData(table, schemaColumns, flatColumns, jsonPath, xlsxPath);

      var json = JArray.Parse(File.ReadAllText(jsonPath));
      Assert.Equal("Hp", json[0]?["Stat"]?["Type"]?.Value<string>());
      Assert.Equal(100, json[0]?["Stat"]?["Value"]?.Value<int>());
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void SaveData_WritesExcelFile()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var (schemaColumns, flatColumns) = CreateSampleSchema();
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["Id"] = "1";
      row["Name"] = "Test";
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "test.json");
      var xlsxPath = Path.Combine(directory, "test.xlsx");

      service.SaveData(table, schemaColumns, flatColumns, jsonPath, xlsxPath);

      using var archive = ZipFile.OpenRead(xlsxPath);
      var worksheet = archive.GetEntry("xl/worksheets/sheet1.xml");
      Assert.NotNull(worksheet);

      using var stream = worksheet.Open();
      using var reader = new StreamReader(stream);
      var xml = reader.ReadToEnd();

      Assert.Contains("Id", xml);
      Assert.Contains("Name", xml);
      Assert.Contains("Test", xml);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateData_WhenNoRows_ReturnsFailure()
  {
    var service = new DataEntryService();
    var (_, flatColumns) = CreateSampleSchema();
    var table = service.CreateDataTable(flatColumns);

    var result = service.ValidateData(table, flatColumns);

    Assert.False(result.IsValid);
  }

  [Fact]
  public void ValidateData_WhenRequiredColumnIsEmpty_ReturnsFailure()
  {
    var service = new DataEntryService();
    var schemaColumns = new[]
    {
      new SchemaColumn { Name = "Id", JsonType = "integer", IsRequired = true, IsNullable = false }
    };
    var flatColumns = service.GetFlatColumns(schemaColumns);
    var table = service.CreateDataTable(flatColumns);
    var row = table.NewRow();
    row["Id"] = string.Empty;
    table.Rows.Add(row);

    var result = service.ValidateData(table, flatColumns);

    Assert.False(result.IsValid);
  }

  [Fact]
  public void LoadData_WhenJsonFileDoesNotExist_ReturnsEmptyTable()
  {
    var service = new DataEntryService();
    var (_, flatColumns) = CreateSampleSchema();

    var table = service.LoadData(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json"), flatColumns);

    Assert.Equal(0, table.Rows.Count);
    Assert.Equal(2, table.Columns.Count);
  }

  [Fact]
  public void LoadData_WithSingleObjectJsonFile_PopulatesOneRow()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var jsonPath = Path.Combine(directory, "GameConfig.json");
      File.WriteAllText(jsonPath, """{"SpawnPoint":{"X":330,"Y":324},"RouteYScale":0.7}""");

      var service = new DataEntryService();
      var schemaColumns = CreateGameConfigSchemaColumns();
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var table = service.LoadData(jsonPath, flatColumns);

      Assert.Single(table.Rows.Cast<DataRow>());
      Assert.Equal("330", table.Rows[0]["SpawnPoint.X"]);
      Assert.Equal("324", table.Rows[0]["SpawnPoint.Y"]);
      Assert.Equal("0.7", table.Rows[0]["RouteYScale"]);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void SaveData_WhenExistingJsonIsSingleObject_PreservesSingleObjectShape()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var jsonPath = Path.Combine(directory, "GameConfig.json");
      File.WriteAllText(jsonPath, """{"SpawnPoint":{"X":330,"Y":324},"RouteYScale":0.7}""");

      var service = new DataEntryService();
      var schemaColumns = CreateGameConfigSchemaColumns();
      var flatColumns = service.GetFlatColumns(schemaColumns);
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["SpawnPoint.X"] = "330";
      row["SpawnPoint.Y"] = "324";
      row["RouteYScale"] = "0.7";
      table.Rows.Add(row);

      var xlsxPath = Path.Combine(directory, "GameConfig.xlsx");
      service.SaveData(table, schemaColumns, flatColumns, jsonPath, xlsxPath);

      var json = JObject.Parse(File.ReadAllText(jsonPath));
      Assert.Equal(330, json["SpawnPoint"]?["X"]?.Value<int>());
      Assert.Equal(324, json["SpawnPoint"]?["Y"]?.Value<int>());
      Assert.Equal(0.7, json["RouteYScale"]?.Value<double>());
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void GetFlatColumns_WithArrayColumnWithItemName_UsesItemNameWithPostfix()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn { Name = "Stats", JsonType = "array", ArrayMin = 1, ArrayMax = 3, ItemJsonType = "integer", ItemName = "Stat" }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.Equal(3, flat.Count);
    Assert.Equal("Stat#1", flat[0].LeafName);
    Assert.Equal("Stat#2", flat[1].LeafName);
    Assert.Equal("Stat#3", flat[2].LeafName);
    Assert.Equal("Stats.0", flat[0].Path);
  }

  [Fact]
  public void GetFlatColumns_WithArrayColumn_ExpandsToMaxColumns()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn { Name = "Tags", JsonType = "array", IsRequired = true, ArrayMin = 1, ArrayMax = 3, ItemJsonType = "string" }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.Equal(3, flat.Count);
    Assert.Equal("Tags.0", flat[0].Path);
    Assert.Equal("0", flat[0].LeafName);
    Assert.Equal("Tags", flat[0].Level1Group);
    Assert.Equal(string.Empty, flat[0].Level2Group);
    Assert.True(flat[0].IsRequired);
    Assert.False(flat[0].IsNullable);
    Assert.Equal("Tags.2", flat[2].Path);
    Assert.False(flat[2].IsRequired);
    Assert.True(flat[2].IsNullable);
  }

  [Fact]
  public void SaveData_WithArrayColumn_WritesJsonArray()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Tags", JsonType = "array", ArrayMin = 1, ArrayMax = 3, ItemJsonType = "string" }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["Tags.0"] = "a";
      row["Tags.1"] = "b";
      row["Tags.2"] = string.Empty;
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "test.json");
      var xlsxPath = Path.Combine(directory, "test.xlsx");

      service.SaveData(table, schemaColumns, flatColumns, jsonPath, xlsxPath);

      var json = JArray.Parse(File.ReadAllText(jsonPath));
      var tags = json[0]?["Tags"] as JArray;
      Assert.NotNull(tags);
      Assert.Equal(2, tags.Count);
      Assert.Equal("a", tags[0].Value<string>());
      Assert.Equal("b", tags[1].Value<string>());
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void LoadData_WithArrayJsonFile_PopulatesIndexedColumns()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var jsonPath = Path.Combine(directory, "test.json");
      File.WriteAllText(jsonPath, """[{"Tags":["a","b","c"]}]""");

      var service = new DataEntryService();
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Tags", JsonType = "array", ArrayMin = 1, ArrayMax = 3, ItemJsonType = "string" }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var table = service.LoadData(jsonPath, flatColumns);

      Assert.Equal(1, table.Rows.Count);
      Assert.Equal("a", table.Rows[0]["Tags.0"]);
      Assert.Equal("b", table.Rows[0]["Tags.1"]);
      Assert.Equal("c", table.Rows[0]["Tags.2"]);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void GetFlatColumns_WithArrayOfObjectItems_ExpandsToChildColumns()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn
      {
        Name = "Stat",
        JsonType = "array",
        ArrayMin = 1,
        ArrayMax = 2,
        ItemJsonType = "object",
        ItemName = "Stat",
        ItemChildren = new[]
        {
          new SchemaColumn { Name = "Type", JsonType = "string", IsRequired = true },
          new SchemaColumn { Name = "Value", JsonType = "integer", IsRequired = true }
        }
      }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.Equal(4, flat.Count);
    Assert.Equal("Stat.0.Type", flat[0].Path);
    Assert.Equal("Type", flat[0].LeafName);
    Assert.Equal("Stat", flat[0].Level1Group);
    Assert.Equal("Stat#1", flat[0].Level2Group);
    Assert.Equal("Stat.0.Value", flat[1].Path);
    Assert.Equal("Value", flat[1].LeafName);
    Assert.Equal("Stat#1", flat[1].Level2Group);
    Assert.Equal("Stat.1.Type", flat[2].Path);
    Assert.Equal("Stat#2", flat[2].Level2Group);
    Assert.Equal("Stat.1.Value", flat[3].Path);
    Assert.Equal("Stat#2", flat[3].Level2Group);
  }

  [Fact]
  public void GetFlatColumns_WithNestedObjectInsideArrayItem_ExpandsToLeafColumns()
  {
    var service = new DataEntryService();
    var flat = service.GetFlatColumns(CreateArrayWithNestedObjectSchemaColumns());

    Assert.Equal(4, flat.Count);
    Assert.Equal("Rewards.0.Item.Id", flat[0].Path);
    Assert.Equal("Id", flat[0].LeafName);
    Assert.Equal("Rewards", flat[0].Level1Group);
    Assert.Equal("Reward#1", flat[0].Level2Group);
    Assert.True(flat[0].IsRequired);
    Assert.False(flat[0].IsNullable);
    Assert.Equal("Rewards.1.Item.Count", flat[3].Path);
    Assert.False(flat[3].IsRequired);
    Assert.True(flat[3].IsNullable);
  }

  [Fact]
  public void GetFlatColumns_WithArrayOfObjectItems_SetsRequiredAndNullableCorrectly()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn
      {
        Name = "Stat",
        JsonType = "array",
        IsRequired = true,
        ArrayMin = 1,
        ArrayMax = 2,
        ItemJsonType = "object",
        ItemChildren = new[]
        {
          new SchemaColumn { Name = "Type", JsonType = "string", IsRequired = true }
        }
      }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.True(flat[0].IsRequired);
    Assert.False(flat[0].IsNullable);
    Assert.False(flat[1].IsRequired);
    Assert.True(flat[1].IsNullable);
  }

  [Fact]
  public void SaveData_WithArrayOfObjectItems_WritesNestedJsonArray()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaColumns = CreateArrayOfObjectsSchemaColumns();
      var flatColumns = service.GetFlatColumns(schemaColumns);
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["Stat.0.Type"] = "Fire";
      row["Stat.0.Value"] = "100";
      row["Stat.1.Type"] = "Water";
      row["Stat.1.Value"] = "80";
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "test.json");
      var xlsxPath = Path.Combine(directory, "test.xlsx");
      service.SaveData(table, schemaColumns, flatColumns, jsonPath, xlsxPath);

      var json = JArray.Parse(File.ReadAllText(jsonPath));
      var stat = json[0]?["Stat"] as JArray;
      Assert.NotNull(stat);
      Assert.Equal(2, stat.Count);
      Assert.Equal("Fire", stat[0]?["Type"]?.Value<string>());
      Assert.Equal(100, stat[0]?["Value"]?.Value<int>());
      Assert.Equal("Water", stat[1]?["Type"]?.Value<string>());
      Assert.Equal(80, stat[1]?["Value"]?.Value<int>());
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void SaveData_WithNestedObjectInsideArrayItem_WritesNestedJsonArray()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaColumns = CreateArrayWithNestedObjectSchemaColumns();
      var flatColumns = service.GetFlatColumns(schemaColumns);
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["Rewards.0.Item.Id"] = "Gold";
      row["Rewards.0.Item.Count"] = "10";
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "test.json");
      var xlsxPath = Path.Combine(directory, "test.xlsx");
      service.SaveData(table, schemaColumns, flatColumns, jsonPath, xlsxPath);

      var json = JArray.Parse(File.ReadAllText(jsonPath));
      var rewards = json[0]?["Rewards"] as JArray;
      Assert.NotNull(rewards);
      Assert.Single(rewards);
      Assert.Equal("Gold", rewards[0]?["Item"]?["Id"]?.Value<string>());
      Assert.Equal(10, rewards[0]?["Item"]?["Count"]?.Value<int>());
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void LoadData_WithArrayOfObjectsJsonFile_PopulatesChildColumns()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var jsonPath = Path.Combine(directory, "test.json");
      File.WriteAllText(jsonPath, """[{"Stat":[{"Type":"Fire","Value":100},{"Type":"Water","Value":80}]}]""");

      var service = new DataEntryService();
      var flatColumns = service.GetFlatColumns(CreateArrayOfObjectsSchemaColumns());

      var table = service.LoadData(jsonPath, flatColumns);

      Assert.Equal(1, table.Rows.Count);
      Assert.Equal("Fire", table.Rows[0]["Stat.0.Type"]);
      Assert.Equal("100", table.Rows[0]["Stat.0.Value"]);
      Assert.Equal("Water", table.Rows[0]["Stat.1.Type"]);
      Assert.Equal("80", table.Rows[0]["Stat.1.Value"]);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void LoadData_WithPivotInfo_FlattensGroupsToRows()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var jsonPath = Path.Combine(directory, "test.json");
      var pivotedJson = new JArray
      {
        new JObject
        {
          ["Name"] = "Stage01",
          ["LevelData"] = new JArray
          {
            new JObject { ["Level"] = 1, ["RequireExp"] = 10 },
            new JObject { ["Level"] = 2, ["RequireExp"] = 20 }
          }
        }
      };
      File.WriteAllText(jsonPath, pivotedJson.ToString());

      var service = new DataEntryService();
      var flatColumns = new[]
      {
        new FlatColumn { Path = "Name", LeafName = "Name", JsonType = "string" },
        new FlatColumn { Path = "Level", LeafName = "Level", JsonType = "integer" },
        new FlatColumn { Path = "RequireExp", LeafName = "RequireExp", JsonType = "integer" }
      };
      var pivotInfo = new PivotInfo("Name", "LevelData");

      var table = service.LoadData(jsonPath, flatColumns, pivotInfo);

      Assert.Equal(2, table.Rows.Count);
      Assert.Equal("Stage01", table.Rows[0]["Name"]);
      Assert.Equal("1", table.Rows[0]["Level"]);
      Assert.Equal("10", table.Rows[0]["RequireExp"]);
      Assert.Equal("Stage01", table.Rows[1]["Name"]);
      Assert.Equal("2", table.Rows[1]["Level"]);
      Assert.Equal("20", table.Rows[1]["RequireExp"]);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void LoadData_WithPivotInfo_MultipleGroups_FlattensAll()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var jsonPath = Path.Combine(directory, "test.json");
      var pivotedJson = new JArray
      {
        new JObject
        {
          ["Name"] = "Stage01",
          ["LevelData"] = new JArray { new JObject { ["Level"] = 1, ["RequireExp"] = 10 } }
        },
        new JObject
        {
          ["Name"] = "Stage02",
          ["LevelData"] = new JArray { new JObject { ["Level"] = 1, ["RequireExp"] = 5 } }
        }
      };
      File.WriteAllText(jsonPath, pivotedJson.ToString());

      var service = new DataEntryService();
      var flatColumns = new[]
      {
        new FlatColumn { Path = "Name", LeafName = "Name", JsonType = "string" },
        new FlatColumn { Path = "Level", LeafName = "Level", JsonType = "integer" },
        new FlatColumn { Path = "RequireExp", LeafName = "RequireExp", JsonType = "integer" }
      };

      var table = service.LoadData(jsonPath, flatColumns, new PivotInfo("Name", "LevelData"));

      Assert.Equal(2, table.Rows.Count);
      Assert.Equal("Stage01", table.Rows[0]["Name"]);
      Assert.Equal("Stage02", table.Rows[1]["Name"]);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void SaveData_WithPivotInfo_GroupsByPivotColumn()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Name", JsonType = "string" },
        new SchemaColumn { Name = "Level", JsonType = "integer" },
        new SchemaColumn { Name = "RequireExp", JsonType = "integer" }
      };
      var flatColumns = new[]
      {
        new FlatColumn { Path = "Name", LeafName = "Name", JsonType = "string" },
        new FlatColumn { Path = "Level", LeafName = "Level", JsonType = "integer" },
        new FlatColumn { Path = "RequireExp", LeafName = "RequireExp", JsonType = "integer" }
      };

      var table = service.CreateDataTable(flatColumns);
      var row1 = table.NewRow();
      row1["Name"] = "Stage01";
      row1["Level"] = "1";
      row1["RequireExp"] = "10";
      table.Rows.Add(row1);

      var row2 = table.NewRow();
      row2["Name"] = "Stage01";
      row2["Level"] = "2";
      row2["RequireExp"] = "20";
      table.Rows.Add(row2);

      var jsonPath = Path.Combine(directory, "test.json");
      var xlsxPath = Path.Combine(directory, "test.xlsx");
      service.SaveData(table, schemaColumns, flatColumns, jsonPath, xlsxPath, new PivotInfo("Name", "LevelData"));

      var saved = JArray.Parse(File.ReadAllText(jsonPath));
      Assert.Single(saved);
      Assert.Equal("Stage01", saved[0]["Name"]!.ToString());
      var items = (JArray)saved[0]["LevelData"]!;
      Assert.Equal(2, items.Count);
      Assert.Equal(1L, items[0]["Level"]!.Value<long>());
      Assert.Equal(10L, items[0]["RequireExp"]!.Value<long>());
      Assert.Equal(2L, items[1]["Level"]!.Value<long>());
      Assert.Equal(20L, items[1]["RequireExp"]!.Value<long>());
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void SaveData_WithPivotInfo_MultipleGroups_EachGroupSeparated()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Name", JsonType = "string" },
        new SchemaColumn { Name = "Level", JsonType = "integer" }
      };
      var flatColumns = new[]
      {
        new FlatColumn { Path = "Name", LeafName = "Name", JsonType = "string" },
        new FlatColumn { Path = "Level", LeafName = "Level", JsonType = "integer" }
      };

      var table = service.CreateDataTable(flatColumns);
      var r1 = table.NewRow();
      r1["Name"] = "Stage01";
      r1["Level"] = "1";
      table.Rows.Add(r1);

      var r2 = table.NewRow();
      r2["Name"] = "Stage02";
      r2["Level"] = "1";
      table.Rows.Add(r2);

      var jsonPath = Path.Combine(directory, "test.json");
      var xlsxPath = Path.Combine(directory, "test.xlsx");
      service.SaveData(table, schemaColumns, flatColumns, jsonPath, xlsxPath, new PivotInfo("Name", "LevelData"));

      var saved = JArray.Parse(File.ReadAllText(jsonPath));
      Assert.Equal(2, saved.Count);
      Assert.Equal("Stage01", saved[0]["Name"]!.ToString());
      Assert.Equal("Stage02", saved[1]["Name"]!.ToString());
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  private static IReadOnlyList<SchemaColumn> CreateArrayOfObjectsSchemaColumns()
  {
    return new[]
    {
      new SchemaColumn
      {
        Name = "Stat",
        JsonType = "array",
        IsRequired = true,
        ArrayMin = 1,
        ArrayMax = 4,
        ItemJsonType = "object",
        ItemChildren = new[]
        {
          new SchemaColumn { Name = "Type", JsonType = "string", IsRequired = true },
          new SchemaColumn { Name = "Value", JsonType = "integer", IsRequired = true }
        }
      }
    };
  }

  private static IReadOnlyList<SchemaColumn> CreateGameConfigSchemaColumns()
  {
    return new[]
    {
      new SchemaColumn
      {
        Name = "SpawnPoint",
        JsonType = "object",
        IsRequired = true,
        Children = new[]
        {
          new SchemaColumn { Name = "X", JsonType = "integer", IsRequired = true },
          new SchemaColumn { Name = "Y", JsonType = "integer", IsRequired = true }
        }
      },
      new SchemaColumn { Name = "RouteYScale", JsonType = "number", IsRequired = true }
    };
  }

  private static IReadOnlyList<SchemaColumn> CreateArrayWithNestedObjectSchemaColumns()
  {
    return new[]
    {
      new SchemaColumn
      {
        Name = "Rewards",
        JsonType = "array",
        IsRequired = true,
        ArrayMin = 1,
        ArrayMax = 2,
        ItemJsonType = "object",
        ItemName = "Reward",
        ItemChildren = new[]
        {
          new SchemaColumn
          {
            Name = "Item",
            JsonType = "object",
            IsRequired = true,
            Children = new[]
            {
              new SchemaColumn { Name = "Id", JsonType = "string", IsRequired = true },
              new SchemaColumn { Name = "Count", JsonType = "integer", IsRequired = true }
            }
          }
        }
      }
    };
  }

  [Fact]
  public void SaveDomainJson_ClientFilter_OnlyIncludesClientFields()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaDomains = new[] { "client", "server" };
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Id", JsonType = "integer" },
        new SchemaColumn { Name = "ServerOnly", JsonType = "string", Domain = ["server"] }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["Id"] = "1";
      row["ServerOnly"] = "hidden";
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "client.json");
      service.SaveDomainJson(table, schemaColumns, schemaDomains, "client", jsonPath);

      var json = JArray.Parse(File.ReadAllText(jsonPath));
      Assert.Single(json);
      Assert.Equal(1, json[0]?["Id"]?.Value<int>());
      Assert.Null(json[0]?["ServerOnly"]);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void SaveDomainJson_ServerFilter_OnlyIncludesServerFields()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaDomains = new[] { "client", "server" };
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Id", JsonType = "integer" },
        new SchemaColumn { Name = "ClientOnly", JsonType = "string", Domain = ["client"] }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["Id"] = "1";
      row["ClientOnly"] = "hidden";
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "server.json");
      service.SaveDomainJson(table, schemaColumns, schemaDomains, "server", jsonPath);

      var json = JArray.Parse(File.ReadAllText(jsonPath));
      Assert.Single(json);
      Assert.Equal(1, json[0]?["Id"]?.Value<int>());
      Assert.Null(json[0]?["ClientOnly"]);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void SaveDomainJson_ColumnWithOverrideDomain_UsesOverrideDomain()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaDomains = new[] { "client" };
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Id", JsonType = "integer" },
        new SchemaColumn { Name = "ServerExtra", JsonType = "string", Domain = ["server"] }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["Id"] = "1";
      row["ServerExtra"] = "excluded";
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "client.json");
      service.SaveDomainJson(table, schemaColumns, schemaDomains, "client", jsonPath);

      var json = JArray.Parse(File.ReadAllText(jsonPath));
      Assert.Single(json);
      Assert.Equal(1, json[0]?["Id"]?.Value<int>());
      Assert.Null(json[0]?["ServerExtra"]);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  private static (IReadOnlyList<SchemaColumn> schema, IReadOnlyList<FlatColumn> flat) CreateSampleSchema()
  {
    var service = new DataEntryService();
    var schemaColumns = new[]
    {
      new SchemaColumn { Name = "Id", JsonType = "integer" },
      new SchemaColumn { Name = "Name", JsonType = "string" }
    };
    var flatColumns = service.GetFlatColumns(schemaColumns);
    return (schemaColumns, flatColumns);
  }

  [Fact]
  public void GetFlatColumns_WithRef_PropagatesRefToFlatColumn()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn { Name = "SkillSet", JsonType = "string", Ref = "TowerSkillSet.schema.json#/definitions/Name" }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.Single(flat);
    Assert.Equal("TowerSkillSet.schema.json#/definitions/Name", flat[0].Ref);
  }

  [Fact]
  public void LoadRefValues_ReturnsAllValues()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var json = new JArray
      {
        new JObject { ["Name"] = "SkillSet_A" },
        new JObject { ["Name"] = "SkillSet_B" },
        new JObject { ["Name"] = "SkillSet_C" }
      };
      File.WriteAllText(Path.Combine(directory, "TowerSkillSet.json"), json.ToString());

      var values = service.LoadRefValues(directory, "TowerSkillSet.schema.json#/definitions/Name");

      Assert.Equal(3, values.Count);
      Assert.Contains("SkillSet_A", values);
      Assert.Contains("SkillSet_B", values);
      Assert.Contains("SkillSet_C", values);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void LoadRefValues_MissingFile_ReturnsEmptySet()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();

      var values = service.LoadRefValues(directory, "NonExistent.schema.json#/definitions/Name");

      Assert.Empty(values);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateData_RefColumn_ValidValue_Passes()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new FlatColumn { Path = "SkillSet", LeafName = "SkillSet", JsonType = "string", IsRequired = true, Ref = "TowerSkillSet.schema.json#/definitions/Name" }
    };
    var table = service.CreateDataTable(columns);
    var row = table.NewRow();
    row["SkillSet"] = "SkillSet_A";
    table.Rows.Add(row);
    var refValues = new Dictionary<string, IReadOnlySet<string>>
    {
      ["SkillSet"] = new HashSet<string> { "SkillSet_A", "SkillSet_B" }
    };

    var result = service.ValidateData(table, columns, refValues);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateData_RefColumn_InvalidValue_ReturnsError()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new FlatColumn { Path = "SkillSet", LeafName = "SkillSet", JsonType = "string", IsRequired = true, Ref = "TowerSkillSet.schema.json#/definitions/Name" }
    };
    var table = service.CreateDataTable(columns);
    var row = table.NewRow();
    row["SkillSet"] = "NotExist";
    table.Rows.Add(row);
    var refValues = new Dictionary<string, IReadOnlySet<string>>
    {
      ["SkillSet"] = new HashSet<string> { "SkillSet_A", "SkillSet_B" }
    };

    var result = service.ValidateData(table, columns, refValues);

    Assert.False(result.IsValid);
    Assert.Contains("NotExist", result.Message);
  }

  [Fact]
  public void GetFlatColumns_WithRequiredArray_ShowRequiredMarkTrueForAllItems()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn { Name = "Tags", JsonType = "array", IsRequired = true, ArrayMin = 1, ArrayMax = 3, ItemJsonType = "string" }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.Equal(3, flat.Count);
    Assert.True(flat[0].ShowRequiredMark);
    Assert.True(flat[1].ShowRequiredMark);
    Assert.True(flat[2].ShowRequiredMark);
  }

  [Fact]
  public void GetFlatColumns_WithNonRequiredArray_ShowRequiredMarkFalseForAllItems()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn { Name = "Tags", JsonType = "array", IsRequired = false, ArrayMin = 0, ArrayMax = 3, ItemJsonType = "string" }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.All(flat, col => Assert.False(col.ShowRequiredMark));
  }

  [Fact]
  public void ValidateAll_WithNoData_ReturnsEmpty()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      var schemaColumns = new[] { new SchemaColumn { Name = "Id", JsonType = "integer", IsRequired = true } };
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var errors = service.ValidateAll(jsonPath, schemaColumns, flatColumns, directory);

      Assert.Empty(errors);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateAll_UnknownColumn_ReturnsOtaError()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"Id":1,"UnknownCol":"x"}]""");
      var schemaColumns = new[] { new SchemaColumn { Name = "Id", JsonType = "integer", IsRequired = true } };
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var errors = service.ValidateAll(jsonPath, schemaColumns, flatColumns, directory);

      Assert.Contains(errors, e => e.Contains("UnknownCol") && e.Contains("오타"));
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateAll_RequiredColumnEmpty_ReturnsError()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"Id":1,"Name":""},{"Id":2,"Name":"b"}]""");
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Id", JsonType = "integer", IsRequired = true },
        new SchemaColumn { Name = "Name", JsonType = "string", IsRequired = true }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var errors = service.ValidateAll(jsonPath, schemaColumns, flatColumns, directory);

      Assert.Contains(errors, e => e.Contains("Name") && e.Contains("필수"));
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateAll_UnusedColumn_ReturnsError()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"Id":1},{"Id":2}]""");
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Id", JsonType = "integer", IsRequired = true },
        new SchemaColumn { Name = "Name", JsonType = "string", IsRequired = true }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var errors = service.ValidateAll(jsonPath, schemaColumns, flatColumns, directory);

      Assert.Contains(errors, e => e.Contains("Name") && e.Contains("미사용"));
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateAll_ValueBelowMinimum_ReturnsError()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"Id":0}]""");
      var schemaColumns = new[] { new SchemaColumn { Name = "Id", JsonType = "integer", IsRequired = true, Minimum = 1 } };
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var errors = service.ValidateAll(jsonPath, schemaColumns, flatColumns, directory);

      Assert.Contains(errors, e => e.Contains("Id") && e.Contains("최솟값"));
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateAll_ValueAboveMaximum_ReturnsError()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"Level":200}]""");
      var schemaColumns = new[] { new SchemaColumn { Name = "Level", JsonType = "integer", IsRequired = true, Maximum = 100 } };
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var errors = service.ValidateAll(jsonPath, schemaColumns, flatColumns, directory);

      Assert.Contains(errors, e => e.Contains("Level") && e.Contains("최댓값"));
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateAll_DuplicateId_ReturnsError()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"Id":1},{"Id":1},{"Id":2}]""");
      var schemaColumns = new[] { new SchemaColumn { Name = "Id", JsonType = "integer", IsRequired = true } };
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var errors = service.ValidateAll(jsonPath, schemaColumns, flatColumns, directory);

      Assert.Contains(errors, e => e.Contains("Id") && e.Contains("중복"));
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateAll_DuplicateName_ReturnsError()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"Id":1,"Name":"Sword"},{"Id":2,"Name":"Sword"}]""");
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Id", JsonType = "integer", IsRequired = true },
        new SchemaColumn { Name = "Name", JsonType = "string", IsRequired = true }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var errors = service.ValidateAll(jsonPath, schemaColumns, flatColumns, directory);

      Assert.Contains(errors, e => e.Contains("Name") && e.Contains("중복"));
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateAll_AllValid_ReturnsEmpty()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"Id":1,"Name":"Sword"},{"Id":2,"Name":"Shield"}]""");
      var schemaColumns = new[]
      {
        new SchemaColumn { Name = "Id", JsonType = "integer", IsRequired = true, Minimum = 1 },
        new SchemaColumn { Name = "Name", JsonType = "string", IsRequired = true }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);

      var errors = service.ValidateAll(jsonPath, schemaColumns, flatColumns, directory);

      Assert.Empty(errors);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateRefs_WithNoRefColumns_ReturnsEmpty()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"Id":1,"Name":"Sword"}]""");
      var flatColumns = new[]
      {
        new FlatColumn { Path = "Id", LeafName = "Id", JsonType = "integer" },
        new FlatColumn { Path = "Name", LeafName = "Name", JsonType = "string" }
      };

      var errors = service.ValidateRefs(jsonPath, flatColumns, directory);

      Assert.Empty(errors);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateRefs_WithMissingJsonFile_ReturnsEmpty()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      var flatColumns = new[]
      {
        new FlatColumn { Path = "SkillSet", LeafName = "SkillSet", JsonType = "string", Ref = "TowerSkillSet.schema.json#/definitions/Name" }
      };

      var errors = service.ValidateRefs(jsonPath, flatColumns, directory);

      Assert.Empty(errors);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateRefs_WithNoRefTargetData_SkipsValidation()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"SkillSet":"UnknownSkill"}]""");
      var flatColumns = new[]
      {
        new FlatColumn { Path = "SkillSet", LeafName = "SkillSet", JsonType = "string", Ref = "TowerSkillSet.schema.json#/definitions/Name" }
      };
      // TowerSkillSet.json does NOT exist → skip validation for this ref

      var errors = service.ValidateRefs(jsonPath, flatColumns, directory);

      Assert.Empty(errors);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateRefs_WithValidRefValues_ReturnsEmpty()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"SkillSet":"SkillSet_A"},{"SkillSet":"SkillSet_B"}]""");
      File.WriteAllText(Path.Combine(directory, "TowerSkillSet.json"), """[{"Name":"SkillSet_A"},{"Name":"SkillSet_B"}]""");
      var flatColumns = new[]
      {
        new FlatColumn { Path = "SkillSet", LeafName = "SkillSet", JsonType = "string", Ref = "TowerSkillSet.schema.json#/definitions/Name" }
      };

      var errors = service.ValidateRefs(jsonPath, flatColumns, directory);

      Assert.Empty(errors);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateRefs_WithInvalidRefValue_ReturnsError()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"SkillSet":"NotExist"}]""");
      File.WriteAllText(Path.Combine(directory, "TowerSkillSet.json"), """[{"Name":"SkillSet_A"}]""");
      var flatColumns = new[]
      {
        new FlatColumn { Path = "SkillSet", LeafName = "SkillSet", JsonType = "string", Ref = "TowerSkillSet.schema.json#/definitions/Name" }
      };

      var errors = service.ValidateRefs(jsonPath, flatColumns, directory);

      Assert.Single(errors);
      Assert.Contains("NotExist", errors[0]);
      Assert.Contains("TowerSkillSet.schema.json#/definitions/Name", errors[0]);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void ValidateRefs_CollectsAllErrors()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var jsonPath = Path.Combine(directory, "Item.json");
      File.WriteAllText(jsonPath, """[{"SkillSet":"Bad1"},{"SkillSet":"Bad2"},{"SkillSet":"SkillSet_A"}]""");
      File.WriteAllText(Path.Combine(directory, "TowerSkillSet.json"), """[{"Name":"SkillSet_A"}]""");
      var flatColumns = new[]
      {
        new FlatColumn { Path = "SkillSet", LeafName = "SkillSet", JsonType = "string", Ref = "TowerSkillSet.schema.json#/definitions/Name" }
      };

      var errors = service.ValidateRefs(jsonPath, flatColumns, directory);

      Assert.Equal(2, errors.Count);
      Assert.Contains(errors, e => e.Contains("Bad1"));
      Assert.Contains(errors, e => e.Contains("Bad2"));
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  // ── Custom Format: FlatColumn propagation ─────────────

  [Fact]
  public void GetFlatColumns_WithDatetimeFormat_SetsFormatOnFlatColumn()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn { Name = "CreatedAt", JsonType = "string", Format = "datetime" }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.Single(flat);
    Assert.Equal("datetime", flat[0].Format);
  }

  [Fact]
  public void GetFlatColumns_WithTimespanFormat_SetsFormatOnFlatColumn()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new SchemaColumn { Name = "Duration", JsonType = "integer", Format = "timespan-second" }
    };

    var flat = service.GetFlatColumns(columns);

    Assert.Single(flat);
    Assert.Equal("timespan-second", flat[0].Format);
  }

  // ── Custom Format: ValidateCellValue — datetime ───────

  [Fact]
  public void ValidateCellValue_WithDatetimeFormat_ValidIso_Passes()
  {
    var column = new FlatColumn { LeafName = "CreatedAt", JsonType = "string", Format = "datetime" };

    var result = DataEntryService.ValidateCellValue(column, "2026-06-09T10:00:00Z");

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateCellValue_WithDatetimeFormat_ValidIsoWithOffset_Passes()
  {
    var column = new FlatColumn { LeafName = "CreatedAt", JsonType = "string", Format = "datetime" };

    var result = DataEntryService.ValidateCellValue(column, "2026-06-09T10:00:00+09:00");

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateCellValue_WithDatetimeFormat_SpaceSeparated24Hour_Passes()
  {
    var column = new FlatColumn { LeafName = "CreatedAt", JsonType = "string", Format = "datetime" };

    var result = DataEntryService.ValidateCellValue(column, "2026-06-09 20:00:10");

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateCellValue_WithDatetimeFormat_InvalidString_Fails()
  {
    var column = new FlatColumn { LeafName = "CreatedAt", JsonType = "string", Format = "datetime" };

    var result = DataEntryService.ValidateCellValue(column, "not-a-date");

    Assert.False(result.IsValid);
    Assert.Contains("24-hour", result.Message);
  }

  [Fact]
  public void ValidateCellValue_WithDatetimeFormat_PlainDate_Fails()
  {
    var column = new FlatColumn { LeafName = "CreatedAt", JsonType = "string", Format = "datetime" };

    var result = DataEntryService.ValidateCellValue(column, "2026-06-09");

    Assert.False(result.IsValid);
  }

  // ── Custom Format: SaveData — readonly-list skip empty ─

  [Fact]
  public void SaveData_WithReadonlyList_SkipsEmptyMiddleItems()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaColumns = new[]
      {
        new SchemaColumn
        {
          Name = "Tags",
          JsonType = "array",
          Format = "readonly-list",
          ArrayMin = 0,
          ArrayMax = 3,
          ItemJsonType = "string"
        }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["Tags.0"] = "A";
      row["Tags.1"] = string.Empty;
      row["Tags.2"] = "C";
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "test.json");
      service.SaveData(table, schemaColumns, flatColumns, jsonPath, Path.Combine(directory, "test.xlsx"));

      var json = JArray.Parse(File.ReadAllText(jsonPath));
      var tags = (JArray)json[0]["Tags"]!;
      Assert.Equal(2, tags.Count);
      Assert.Equal("A", tags[0].Value<string>());
      Assert.Equal("C", tags[1].Value<string>());
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  // ── Custom Format: SaveData — frozen-dictionary skip empty ─

  [Fact]
  public void SaveData_WithFrozenDictionary_SkipsEmptyRows()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DataEntryServiceTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
      var service = new DataEntryService();
      var schemaColumns = new[]
      {
        new SchemaColumn
        {
          Name = "StatMap",
          JsonType = "array",
          Format = "frozen-dictionary",
          ArrayMin = 0,
          ArrayMax = 3,
          ItemJsonType = "object",
          ItemChildren = new[]
          {
            new SchemaColumn { Name = "StatName", JsonType = "string" },
            new SchemaColumn { Name = "Value", JsonType = "number" }
          }
        }
      };
      var flatColumns = service.GetFlatColumns(schemaColumns);
      var table = service.CreateDataTable(flatColumns);
      var row = table.NewRow();
      row["StatMap.0.StatName"] = "Hp";
      row["StatMap.0.Value"] = "100";
      row["StatMap.2.StatName"] = "Mp";
      row["StatMap.2.Value"] = "50";
      table.Rows.Add(row);

      var jsonPath = Path.Combine(directory, "test.json");
      service.SaveData(table, schemaColumns, flatColumns, jsonPath, Path.Combine(directory, "test.xlsx"));

      var json = JArray.Parse(File.ReadAllText(jsonPath));
      var statMap = (JArray)json[0]["StatMap"]!;
      Assert.Equal(2, statMap.Count);
      Assert.Equal("Hp", statMap[0]["StatName"]!.Value<string>());
      Assert.Equal("Mp", statMap[1]["StatName"]!.Value<string>());
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  // ── EnumRef: ValidateData ─────────────────────────────

  [Fact]
  public void ValidateData_WithValidEnumRefValue_Passes()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new FlatColumn { Path = "EnemyType", LeafName = "EnemyType", JsonType = "string", Ref = "enum.schema.json#/definitions/MonsterType" }
    };
    var table = service.CreateDataTable(columns);
    var row = table.NewRow();
    row["EnemyType"] = "Elite";
    table.Rows.Add(row);
    var refValues = new Dictionary<string, IReadOnlySet<string>>
    {
      ["EnemyType"] = new HashSet<string> { "Normal", "Elite", "Boss" }
    };

    var result = service.ValidateData(table, columns, refValues);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void ValidateData_WithInvalidEnumRefValue_ReturnsError()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new FlatColumn { Path = "EnemyType", LeafName = "EnemyType", JsonType = "string", Ref = "enum.schema.json#/definitions/MonsterType" }
    };
    var table = service.CreateDataTable(columns);
    var row = table.NewRow();
    row["EnemyType"] = "Dragon";
    table.Rows.Add(row);
    var refValues = new Dictionary<string, IReadOnlySet<string>>
    {
      ["EnemyType"] = new HashSet<string> { "Normal", "Elite", "Boss" }
    };

    var result = service.ValidateData(table, columns, refValues);

    Assert.False(result.IsValid);
    Assert.Contains("Dragon", result.Message);
  }

  [Fact]
  public void ValidateAll_WithInvalidEnumRefValue_ReturnsError()
  {
    var service = new DataEntryService();
    var columns = new[]
    {
      new FlatColumn { Path = "EnemyType", LeafName = "EnemyType", JsonType = "string", Ref = "enum.schema.json#/definitions/MonsterType" }
    };
    var table = service.CreateDataTable(columns);
    var row = table.NewRow();
    row["EnemyType"] = "Unknown";
    table.Rows.Add(row);
    var refValues = new Dictionary<string, IReadOnlySet<string>>
    {
      ["EnemyType"] = new HashSet<string> { "Normal", "Elite", "Boss" }
    };

    var errors = service.ValidateAll(table, columns, refValues);

    Assert.NotEmpty(errors);
    Assert.Contains(errors, e => e.Contains("Unknown"));
  }
}
