namespace DatasheetGenerator.Services;

using System.Data;
using System.Globalization;
using System.IO;
using DatasheetGenerator.Export;
using DatasheetGenerator.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class DataEntryService
{
  public IReadOnlyList<FlatColumn> GetFlatColumns(IReadOnlyList<SchemaColumn> columns)
  {
    var result = new List<FlatColumn>();
    foreach (var column in columns)
    {
      this.CollectFlatColumns(column, string.Empty, string.Empty, string.Empty, result);
    }

    return result;
  }

  public DataTable CreateDataTable(IReadOnlyList<FlatColumn> columns)
  {
    var table = new DataTable();
    foreach (var column in columns)
    {
      table.Columns.Add(column.Path, typeof(string));
    }

    return table;
  }

  public DataTable LoadData(string jsonPath, IReadOnlyList<FlatColumn> flatColumns, PivotInfo? pivotInfo = null)
  {
    var table = this.CreateDataTable(flatColumns);
    if (File.Exists(jsonPath) is false)
    {
      return table;
    }

    var jsonText = File.ReadAllText(jsonPath);
    var json = JToken.Parse(jsonText);
    var jsonArray = json switch
    {
      JArray array => array,
      JObject obj => new JArray(obj),
      _ => new JArray()
    };

    if (pivotInfo is not null)
    {
      jsonArray = ReversePivot(jsonArray, pivotInfo);
    }

    foreach (var token in jsonArray)
    {
      if (token is not JObject item)
      {
        continue;
      }

      var row = table.NewRow();
      foreach (var col in flatColumns)
      {
        row[col.Path] = GetValueFromJson(item, col.Path);
      }

      table.Rows.Add(row);
    }

    return table;
  }

  public ValidationResult ValidateData(
    DataTable table,
    IReadOnlyList<FlatColumn> columns,
    IReadOnlyDictionary<string, IReadOnlySet<string>>? refValues = null)
  {
    var hasRows = false;
    foreach (DataRow row in table.Rows)
    {
      if (row.RowState is not DataRowState.Deleted)
      {
        hasRows = true;
        break;
      }
    }

    if (hasRows is false)
    {
      return new ValidationResult(false, "No data to save.");
    }

    foreach (DataRow row in table.Rows)
    {
      if (row.RowState is DataRowState.Deleted)
      {
        continue;
      }

      foreach (var col in columns)
      {
        var value = Convert.ToString(row[col.Path]) ?? string.Empty;
        var result = ValidateCellValue(col, value);
        if (result.IsValid is false)
        {
          return result;
        }

        if (col.Ref is not null && refValues is not null && string.IsNullOrEmpty(value) is false)
        {
          if (refValues.TryGetValue(col.Path, out var allowed) && allowed.Contains(value) is false)
          {
            return new ValidationResult(false, $"{col.LeafName}: '{value}'은(는) {col.Ref}에 존재하지 않습니다.");
          }
        }
      }
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  public IReadOnlyList<string> ValidateAll(
    DataTable table,
    IReadOnlyList<FlatColumn> flatColumns,
    IReadOnlyDictionary<string, IReadOnlySet<string>> refValues)
  {
    if (table.Rows.Count is 0)
    {
      return [];
    }

    var errors = new List<string>();

    CollectUnusedColumnErrors(table, flatColumns, errors);
    CollectRequiredErrors(table, flatColumns, errors);
    CollectRangeErrors(table, flatColumns, errors);
    CollectDuplicateErrors(table, flatColumns, "Id", errors);
    CollectDuplicateErrors(table, flatColumns, "Name", errors);

    var refColumns = flatColumns.Where(c => c.Ref is not null).ToList();
    foreach (DataRow row in table.Rows)
    {
      if (row.RowState is DataRowState.Deleted)
      {
        continue;
      }

      foreach (var col in refColumns)
      {
        if (!refValues.TryGetValue(col.Path, out var allowed))
        {
          continue;
        }

        var value = Convert.ToString(row[col.Path]) ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
          continue;
        }

        if (!allowed.Contains(value))
        {
          errors.Add($"{col.LeafName}: '{value}'은(는) {col.Ref}에 존재하지 않습니다.");
        }
      }
    }

    return errors;
  }

  public IReadOnlyList<string> ValidateAll(
    string jsonPath,
    IReadOnlyList<SchemaColumn> schemaColumns,
    IReadOnlyList<FlatColumn> flatColumns,
    string jsonDirectory,
    PivotInfo? pivotInfo = null)
  {
    if (File.Exists(jsonPath) is false)
    {
      return [];
    }

    var table = this.LoadData(jsonPath, flatColumns, pivotInfo);
    if (table.Rows.Count is 0)
    {
      return [];
    }

    var errors = new List<string>();

    CollectUnknownColumnErrors(jsonPath, schemaColumns, pivotInfo, errors);
    CollectUnusedColumnErrors(table, flatColumns, errors);
    CollectRequiredErrors(table, flatColumns, errors);
    CollectRangeErrors(table, flatColumns, errors);
    CollectDuplicateErrors(table, flatColumns, "Id", errors);
    CollectDuplicateErrors(table, flatColumns, "Name", errors);

    var refErrors = this.ValidateRefs(jsonPath, flatColumns, jsonDirectory, pivotInfo);
    errors.AddRange(refErrors);

    return errors;
  }

  private static void CollectUnknownColumnErrors(string jsonPath, IReadOnlyList<SchemaColumn> schemaColumns, PivotInfo? pivotInfo, List<string> errors)
  {
    JArray array;
    try
    {
      var token = JToken.Parse(File.ReadAllText(jsonPath));
      array = token switch
      {
        JArray a => a,
        JObject obj => new JArray(obj),
        _ => new JArray()
      };
    }
    catch
    {
      return;
    }

    var reported = new HashSet<string>(StringComparer.Ordinal);

    if (pivotInfo is not null)
    {
      foreach (var token in array)
      {
        if (token is not JObject group)
        {
          continue;
        }

        var items = group[pivotInfo.PivotName] as JArray;
        if (items is null)
        {
          continue;
        }

        var innerColumns = schemaColumns.Where(c => c.Name != pivotInfo.PivotColumn).ToList();
        foreach (var item in items)
        {
          if (item is JObject itemObj)
          {
            CheckUnknownProperties(itemObj, innerColumns, reported, errors);
          }
        }
      }
    }
    else
    {
      foreach (var token in array)
      {
        if (token is JObject obj)
        {
          CheckUnknownProperties(obj, schemaColumns, reported, errors);
        }
      }
    }
  }

  private static void CheckUnknownProperties(JObject obj, IReadOnlyList<SchemaColumn> schemaColumns, HashSet<string> reported, List<string> errors)
  {
    var knownNames = new HashSet<string>(schemaColumns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
    foreach (var prop in obj.Properties())
    {
      if (knownNames.Contains(prop.Name) is false)
      {
        if (reported.Add(prop.Name))
        {
          errors.Add($"오타 의심: '{prop.Name}' 컬럼이 스키마에 존재하지 않습니다.");
        }

        continue;
      }

      var matchingColumn = schemaColumns.First(c => string.Equals(c.Name, prop.Name, StringComparison.OrdinalIgnoreCase));
      if (matchingColumn.JsonType is "object" && prop.Value is JObject childObj)
      {
        CheckUnknownProperties(childObj, matchingColumn.Children, reported, errors);
      }
      else if (matchingColumn.JsonType is "array" && prop.Value is JArray arr && matchingColumn.ItemChildren.Count > 0)
      {
        foreach (var item in arr)
        {
          if (item is JObject itemObj)
          {
            CheckUnknownProperties(itemObj, matchingColumn.ItemChildren, reported, errors);
          }
        }
      }
    }
  }

  private static void CollectUnusedColumnErrors(DataTable table, IReadOnlyList<FlatColumn> flatColumns, List<string> errors)
  {
    foreach (var col in flatColumns)
    {
      if (col.IsRequired is false || col.IsNullable)
      {
        continue;
      }

      if (int.TryParse(col.LeafName, out _))
      {
        continue;
      }

      var allEmpty = true;
      foreach (DataRow row in table.Rows)
      {
        if (row.RowState is DataRowState.Deleted)
        {
          continue;
        }

        if (string.IsNullOrEmpty(Convert.ToString(row[col.Path])) is false)
        {
          allEmpty = false;
          break;
        }
      }

      if (allEmpty)
      {
        errors.Add($"미사용 컬럼: '{col.LeafName}'에 데이터가 없습니다.");
      }
    }
  }

  private static void CollectRequiredErrors(DataTable table, IReadOnlyList<FlatColumn> flatColumns, List<string> errors)
  {
    foreach (DataRow row in table.Rows)
    {
      if (row.RowState is DataRowState.Deleted)
      {
        continue;
      }

      foreach (var col in flatColumns)
      {
        if (col.IsRequired is false || col.IsNullable)
        {
          continue;
        }

        if (string.IsNullOrEmpty(Convert.ToString(row[col.Path])))
        {
          errors.Add($"필수 값 누락: '{col.LeafName}' 컬럼에 값이 없는 행이 있습니다.");
          break;
        }
      }
    }
  }

  private static void CollectRangeErrors(DataTable table, IReadOnlyList<FlatColumn> flatColumns, List<string> errors)
  {
    foreach (DataRow row in table.Rows)
    {
      if (row.RowState is DataRowState.Deleted)
      {
        continue;
      }

      foreach (var col in flatColumns)
      {
        if (col.Minimum is null && col.Maximum is null)
        {
          continue;
        }

        var raw = Convert.ToString(row[col.Path]) ?? string.Empty;
        if (string.IsNullOrEmpty(raw))
        {
          continue;
        }

        if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var numVal) is false)
        {
          continue;
        }

        if (col.Minimum is not null && numVal < col.Minimum.Value)
        {
          errors.Add($"범위 오류: '{col.LeafName}'의 값 {raw}이(가) 최솟값 {col.Minimum}보다 작습니다.");
        }

        if (col.Maximum is not null && numVal > col.Maximum.Value)
        {
          errors.Add($"범위 오류: '{col.LeafName}'의 값 {raw}이(가) 최댓값 {col.Maximum}보다 큽니다.");
        }
      }
    }
  }

  private static void CollectDuplicateErrors(DataTable table, IReadOnlyList<FlatColumn> flatColumns, string leafName, List<string> errors)
  {
    var targetColumns = flatColumns.Where(c => c.LeafName.Equals(leafName, StringComparison.OrdinalIgnoreCase)).ToList();
    foreach (var col in targetColumns)
    {
      var seen = new HashSet<string>(StringComparer.Ordinal);
      var duplicates = new HashSet<string>(StringComparer.Ordinal);
      foreach (DataRow row in table.Rows)
      {
        if (row.RowState is DataRowState.Deleted)
        {
          continue;
        }

        var value = Convert.ToString(row[col.Path]) ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
          continue;
        }

        if (seen.Add(value) is false)
        {
          duplicates.Add(value);
        }
      }

      foreach (var dup in duplicates)
      {
        errors.Add($"중복 값: '{col.LeafName}' 컬럼에 '{dup}'이(가) 중복됩니다.");
      }
    }
  }

  public IReadOnlyList<string> ValidateRefs(string jsonPath, IReadOnlyList<FlatColumn> flatColumns, string jsonDirectory, PivotInfo? pivotInfo = null)
  {
    var refColumns = flatColumns.Where(c => c.Ref is not null).ToList();
    if (refColumns.Count is 0)
    {
      return [];
    }

    var table = this.LoadData(jsonPath, flatColumns, pivotInfo);
    if (table.Rows.Count is 0)
    {
      return [];
    }

    var refValues = new Dictionary<string, IReadOnlySet<string>>();
    foreach (var col in refColumns)
    {
      var values = this.LoadRefValues(jsonDirectory, col.Ref!);
      if (values.Count > 0)
      {
        refValues[col.Path] = values;
      }
    }

    var errors = new List<string>();
    foreach (DataRow row in table.Rows)
    {
      if (row.RowState is DataRowState.Deleted)
      {
        continue;
      }

      foreach (var col in refColumns)
      {
        if (!refValues.TryGetValue(col.Path, out var allowed))
        {
          continue;
        }

        var value = Convert.ToString(row[col.Path]) ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
          continue;
        }

        if (!allowed.Contains(value))
        {
          errors.Add($"{col.LeafName}: '{value}'은(는) {col.Ref}에 존재하지 않습니다.");
        }
      }
    }

    return errors;
  }

  public IReadOnlySet<string> LoadRefValues(string jsonDirectory, string refExpression)
  {
    var dotIndex = refExpression.IndexOf('.');
    var schemaName = refExpression[..dotIndex];
    var columnName = refExpression[(dotIndex + 1)..];
    var jsonPath = Path.Combine(jsonDirectory, $"{schemaName}.json");

    if (File.Exists(jsonPath) is false)
    {
      return new HashSet<string>();
    }

    var jsonText = File.ReadAllText(jsonPath);
    JArray array;
    try
    {
      var token = JToken.Parse(jsonText);
      array = token switch
      {
        JArray a => a,
        JObject obj => new JArray(obj),
        _ => new JArray()
      };
    }
    catch (JsonException)
    {
      return new HashSet<string>();
    }

    var values = new HashSet<string>(StringComparer.Ordinal);
    foreach (var token in array)
    {
      if (token is not JObject obj)
      {
        continue;
      }

      var value = obj[columnName]?.ToString();
      if (string.IsNullOrEmpty(value) is false)
      {
        values.Add(value);
      }
    }

    return values;
  }

  public static ValidationResult ValidateCellValue(FlatColumn column, string value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      if (column.IsRequired && column.IsNullable is false)
      {
        return new ValidationResult(false, $"{column.LeafName}: value is required.");
      }

      return new ValidationResult(true, string.Empty);
    }

    if (column.JsonType is "integer" && long.TryParse(value, out _) is false)
    {
      return new ValidationResult(false, $"{column.LeafName}: '{value}' is not a valid integer.");
    }

    if (column.JsonType is "number" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) is false)
    {
      return new ValidationResult(false, $"{column.LeafName}: '{value}' is not a valid number.");
    }

    return new ValidationResult(true, string.Empty);
  }

  public void SaveData(
    DataTable table,
    IReadOnlyList<SchemaColumn> schemaColumns,
    IReadOnlyList<FlatColumn> flatColumns,
    string jsonPath,
    string xlsxPath,
    PivotInfo? pivotInfo = null)
  {
    var jsonDirectory = Path.GetDirectoryName(jsonPath);
    if (string.IsNullOrWhiteSpace(jsonDirectory) is false)
    {
      Directory.CreateDirectory(jsonDirectory);
    }

    var rows = this.CreateJsonRows(table, schemaColumns);

    if (pivotInfo is not null)
    {
      File.WriteAllText(jsonPath, ApplyPivot(rows, pivotInfo).ToString(Formatting.Indented));
    }
    else
    {
      var json = ShouldSaveAsSingleObject(jsonPath) && rows.Count > 0 ? rows[0] : rows;
      File.WriteAllText(jsonPath, json.ToString(Formatting.Indented));
    }

    SpreadsheetWriter.Write(flatColumns, table, xlsxPath);
  }

  public void SaveDomainJson(
    DataTable table,
    IReadOnlyList<SchemaColumn> schemaColumns,
    IReadOnlyList<string> schemaDomains,
    string targetDomain,
    string jsonPath,
    PivotInfo? pivotInfo = null)
  {
    var filteredColumns = FilterColumnsByDomain(schemaColumns, targetDomain, schemaDomains);

    var directory = Path.GetDirectoryName(jsonPath);
    if (string.IsNullOrWhiteSpace(directory) is false)
    {
      Directory.CreateDirectory(directory);
    }

    var rows = this.CreateJsonRows(table, filteredColumns);
    if (pivotInfo is not null)
    {
      File.WriteAllText(jsonPath, ApplyPivot(rows, pivotInfo).ToString(Formatting.Indented));
    }
    else
    {
      File.WriteAllText(jsonPath, rows.ToString(Formatting.Indented));
    }
  }

  private static IReadOnlyList<SchemaColumn> FilterColumnsByDomain(
    IReadOnlyList<SchemaColumn> columns,
    string targetDomain,
    IReadOnlyList<string> inheritedDomains)
  {
    var result = new List<SchemaColumn>();
    foreach (var column in columns)
    {
      var effectiveDomains = column.Domain ?? inheritedDomains;
      if (effectiveDomains.Contains(targetDomain) is false)
      {
        continue;
      }

      if (column.JsonType is "object")
      {
        var filteredChildren = FilterColumnsByDomain(column.Children, targetDomain, effectiveDomains);
        result.Add(column with { Children = filteredChildren });
      }
      else if (column.JsonType is "array" && column.ItemChildren.Count > 0)
      {
        var filteredItemChildren = FilterColumnsByDomain(column.ItemChildren, targetDomain, effectiveDomains);
        result.Add(column with { ItemChildren = filteredItemChildren });
      }
      else
      {
        result.Add(column);
      }
    }

    return result;
  }

  private JArray CreateJsonRows(DataTable table, IReadOnlyList<SchemaColumn> schemaColumns)
  {
    var rows = new JArray();
    foreach (DataRow row in table.Rows)
    {
      if (row.RowState is DataRowState.Deleted)
      {
        continue;
      }

      var item = new JObject();
      foreach (var column in schemaColumns)
      {
        item[column.Name] = this.CreateJsonValue(row, column, column.Name);
      }

      rows.Add(item);
    }

    return rows;
  }

  private JToken CreateJsonValue(DataRow row, SchemaColumn column, string path)
  {
    if (column.JsonType is "object")
    {
      var obj = new JObject();
      foreach (var child in column.Children)
      {
        obj[child.Name] = this.CreateJsonValue(row, child, $"{path}.{child.Name}");
      }

      return obj;
    }

    if (column.JsonType is "array")
    {
      var array = new JArray();
      for (var i = 0; i < column.ArrayMax; i++)
      {
        if (column.ItemChildren.Count > 0)
        {
          var hasAnyValue = column.ItemChildren.Any(child =>
            HasAnyValue(row, child, $"{path}.{i}.{child.Name}"));

          if (hasAnyValue is false)
          {
            break;
          }

          var obj = new JObject();
          foreach (var child in column.ItemChildren)
          {
            obj[child.Name] = this.CreateJsonValue(row, child, $"{path}.{i}.{child.Name}");
          }

          array.Add(obj);
        }
        else
        {
          var itemValue = Convert.ToString(row[$"{path}.{i}"]) ?? string.Empty;
          if (string.IsNullOrWhiteSpace(itemValue))
          {
            break;
          }

          if (column.ItemJsonType is "integer" && long.TryParse(itemValue, out var longVal))
          {
            array.Add(longVal);
          }
          else
          {
            array.Add(itemValue);
          }
        }
      }

      return array;
    }

    var value = Convert.ToString(row[path]) ?? string.Empty;

    if (string.IsNullOrWhiteSpace(value) && column.IsNullable)
    {
      return JValue.CreateNull();
    }

    if (column.JsonType is "integer" && long.TryParse(value, out var longValue))
    {
      return longValue;
    }

    if (column.JsonType is "number" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
    {
      return doubleValue;
    }

    return value;
  }

  private static bool ShouldSaveAsSingleObject(string jsonPath)
  {
    if (File.Exists(jsonPath) is false)
    {
      return false;
    }

    try
    {
      return JToken.Parse(File.ReadAllText(jsonPath)) is JObject;
    }
    catch (JsonException)
    {
      return false;
    }
  }

  private void CollectFlatColumns(
    SchemaColumn column,
    string parentPath,
    string level1Group,
    string level2Group,
    List<FlatColumn> result)
  {
    if (column.JsonType is "object")
    {
      var newLevel1 = string.IsNullOrEmpty(level1Group) ? column.Name : level1Group;
      var newLevel2 = string.IsNullOrEmpty(level1Group) ? string.Empty : column.Name;
      var newPath = string.IsNullOrEmpty(parentPath) ? column.Name : $"{parentPath}.{column.Name}";

      foreach (var child in column.Children)
      {
        this.CollectFlatColumns(child, newPath, newLevel1, newLevel2, result);
      }

      return;
    }

    if (column.JsonType is "array")
    {
      var itemLevel1 = string.IsNullOrEmpty(level1Group) ? column.Name : level1Group;
      var itemLevel2 = string.IsNullOrEmpty(level1Group) ? string.Empty : column.Name;
      var arrayPath = string.IsNullOrEmpty(parentPath) ? column.Name : $"{parentPath}.{column.Name}";

      for (var i = 0; i < column.ArrayMax; i++)
      {
        if (column.ItemChildren.Count > 0)
        {
          var itemLabel = string.IsNullOrEmpty(column.ItemName) ? (i + 1).ToString() : $"{column.ItemName}#{i + 1}";
          foreach (var child in column.ItemChildren)
          {
            this.CollectFlatColumns(
              child,
              $"{arrayPath}.{i}",
              itemLevel1,
              itemLabel,
              result,
              column.IsRequired && i < column.ArrayMin && child.IsRequired,
              i >= column.ArrayMin || child.IsNullable,
              column.IsRequired);
          }
        }
        else
        {
          var leafName = string.IsNullOrEmpty(column.ItemName) ? i.ToString() : $"{column.ItemName}#{i + 1}";
          result.Add(new FlatColumn
          {
            Path = $"{arrayPath}.{i}",
            LeafName = leafName,
            Level1Group = itemLevel1,
            Level2Group = itemLevel2,
            JsonType = column.ItemJsonType,
            IsRequired = column.IsRequired && i < column.ArrayMin,
            IsNullable = i >= column.ArrayMin,
            ShowRequiredMark = column.IsRequired
          });
        }
      }

      return;
    }

    result.Add(new FlatColumn
    {
      Path = string.IsNullOrEmpty(parentPath) ? column.Name : $"{parentPath}.{column.Name}",
      LeafName = column.Name,
      Level1Group = level1Group,
      Level2Group = level2Group,
      JsonType = column.JsonType,
      IsRequired = column.IsRequired,
      IsNullable = column.IsNullable,
      Ref = column.Ref,
      Minimum = column.Minimum,
      Maximum = column.Maximum,
      ShowRequiredMark = column.IsRequired && column.IsNullable is false
    });
  }

  private void CollectFlatColumns(
    SchemaColumn column,
    string parentPath,
    string level1Group,
    string level2Group,
    List<FlatColumn> result,
    bool isRequired,
    bool isNullable,
    bool showRequiredMark = false)
  {
    if (column.JsonType is "object")
    {
      var newPath = string.IsNullOrEmpty(parentPath) ? column.Name : $"{parentPath}.{column.Name}";

      foreach (var child in column.Children)
      {
        this.CollectFlatColumns(
          child,
          newPath,
          level1Group,
          level2Group,
          result,
          isRequired && child.IsRequired,
          isNullable || child.IsNullable,
          showRequiredMark);
      }

      return;
    }

    if (column.JsonType is "array")
    {
      var arrayPath = string.IsNullOrEmpty(parentPath) ? column.Name : $"{parentPath}.{column.Name}";

      for (var i = 0; i < column.ArrayMax; i++)
      {
        if (column.ItemChildren.Count > 0)
        {
          var itemLabel = string.IsNullOrEmpty(column.ItemName) ? (i + 1).ToString() : $"{column.ItemName}#{i + 1}";
          foreach (var child in column.ItemChildren)
          {
            this.CollectFlatColumns(
              child,
              $"{arrayPath}.{i}",
              level1Group,
              itemLabel,
              result,
              isRequired && i < column.ArrayMin && child.IsRequired,
              isNullable || i >= column.ArrayMin || child.IsNullable,
              showRequiredMark || isRequired);
          }
        }
        else
        {
          var leafName = string.IsNullOrEmpty(column.ItemName) ? i.ToString() : $"{column.ItemName}#{i + 1}";
          result.Add(new FlatColumn
          {
            Path = $"{arrayPath}.{i}",
            LeafName = leafName,
            Level1Group = level1Group,
            Level2Group = level2Group,
            JsonType = column.ItemJsonType,
            IsRequired = isRequired && i < column.ArrayMin,
            IsNullable = isNullable || i >= column.ArrayMin,
            ShowRequiredMark = showRequiredMark || isRequired
          });
        }
      }

      return;
    }

    result.Add(new FlatColumn
    {
      Path = string.IsNullOrEmpty(parentPath) ? column.Name : $"{parentPath}.{column.Name}",
      LeafName = column.Name,
      Level1Group = level1Group,
      Level2Group = level2Group,
      JsonType = column.JsonType,
      IsRequired = isRequired,
      IsNullable = isNullable,
      Ref = column.Ref,
      Minimum = column.Minimum,
      Maximum = column.Maximum,
      ShowRequiredMark = showRequiredMark || (isRequired && isNullable is false)
    });
  }

  private static bool HasAnyValue(DataRow row, SchemaColumn column, string path)
  {
    if (column.JsonType is "object")
    {
      return column.Children.Any(child => HasAnyValue(row, child, $"{path}.{child.Name}"));
    }

    if (column.JsonType is "array")
    {
      for (var i = 0; i < column.ArrayMax; i++)
      {
        if (column.ItemChildren.Count > 0)
        {
          if (column.ItemChildren.Any(child => HasAnyValue(row, child, $"{path}.{i}.{child.Name}")))
          {
            return true;
          }
        }
        else if (HasColumnValue(row, $"{path}.{i}"))
        {
          return true;
        }
      }

      return false;
    }

    return HasColumnValue(row, path);
  }

  private static bool HasColumnValue(DataRow row, string path)
  {
    return row.Table.Columns.Contains(path) &&
      string.IsNullOrWhiteSpace(Convert.ToString(row[path])) is false;
  }

  private static JArray ApplyPivot(JArray flatRows, PivotInfo pivotInfo)
  {
    var groups = new Dictionary<string, JArray>();
    var order = new List<string>();

    foreach (var token in flatRows)
    {
      if (token is not JObject row)
      {
        continue;
      }

      var key = row[pivotInfo.PivotColumn]?.ToString() ?? string.Empty;
      if (groups.TryGetValue(key, out var items) is false)
      {
        items = new JArray();
        groups[key] = items;
        order.Add(key);
      }

      var item = new JObject();
      foreach (var prop in row.Properties())
      {
        if (prop.Name == pivotInfo.PivotColumn)
        {
          continue;
        }

        item[prop.Name] = prop.Value;
      }

      items.Add(item);
    }

    var result = new JArray();
    foreach (var key in order)
    {
      var obj = new JObject();
      obj[pivotInfo.PivotColumn] = key;
      obj[pivotInfo.PivotName] = groups[key];
      result.Add(obj);
    }

    return result;
  }

  private static JArray ReversePivot(JArray pivotedRows, PivotInfo pivotInfo)
  {
    var result = new JArray();

    foreach (var token in pivotedRows)
    {
      if (token is not JObject group)
      {
        continue;
      }

      var pivotValue = group[pivotInfo.PivotColumn];
      var items = group[pivotInfo.PivotName] as JArray;

      if (items is null)
      {
        continue;
      }

      foreach (var itemToken in items)
      {
        if (itemToken is not JObject itemObj)
        {
          continue;
        }

        var flatRow = new JObject();
        flatRow[pivotInfo.PivotColumn] = pivotValue;
        foreach (var prop in itemObj.Properties())
        {
          flatRow[prop.Name] = prop.Value;
        }

        result.Add(flatRow);
      }
    }

    return result;
  }

  private static string GetValueFromJson(JObject item, string path)
  {
    var parts = path.Split('.');
    JToken? current = item;
    foreach (var part in parts)
    {
      if (current is JArray arr && int.TryParse(part, out var index))
      {
        current = index < arr.Count ? arr[index] : null;
      }
      else
      {
        current = current?[part];
      }

      if (current is null)
      {
        return string.Empty;
      }
    }

    return current.Type == JTokenType.Null ? string.Empty : current.ToString();
  }
}
