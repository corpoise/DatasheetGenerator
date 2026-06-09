namespace DatasheetGenerator.Services;

using System.IO;
using DatasheetGenerator.Export;
using DatasheetGenerator.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class SchemaService
{
  public IReadOnlyList<SchemaInfo> GetSchemaFiles(string schemaDirectory)
  {
    if (Directory.Exists(schemaDirectory) is false)
    {
      return [];
    }

    return Directory.GetFiles(schemaDirectory, "*.schema.json")
      .Where(f => !GetSchemaName(f).Equals("enum", StringComparison.OrdinalIgnoreCase))
      .OrderBy(f => f)
      .Select(f => new SchemaInfo
      {
        Name = GetSchemaName(f),
        FilePath = f
      })
      .ToList();
  }

  public string LoadSchemaText(string path)
  {
    return File.ReadAllText(path);
  }

  public ValidationResult ValidateSchemaText(string text, IReadOnlyList<string>? allowedDomains = null)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return new ValidationResult(false, "Schema cannot be empty.");
    }

    JObject schema;
    try
    {
      schema = JObject.Parse(text);
    }
    catch (JsonReaderException ex)
    {
      return new ValidationResult(false, $"Invalid JSON: {ex.Message}");
    }

    var pivotColumn = schema["PivotColumn"]?.Value<string>();
    var pivotName = schema["PivotName"]?.Value<string>();
    if (string.IsNullOrWhiteSpace(pivotColumn) is false && string.IsNullOrWhiteSpace(pivotName))
    {
      return new ValidationResult(false, "PivotColumn이 지정된 경우 PivotName도 필요합니다.");
    }

    if (schema["type"]?.Value<string>() is not "object")
    {
      return new ValidationResult(false, "Schema root type must be \"object\".");
    }

    var properties = schema["properties"] as JObject;
    if (properties is null || properties.Count is 0)
    {
      return new ValidationResult(false, "Schema must have properties with at least one property.");
    }

    if (string.IsNullOrWhiteSpace(pivotColumn) is false && properties[pivotColumn] is null)
    {
      return new ValidationResult(false, $"PivotColumn '{pivotColumn}'이(가) properties에 존재하지 않습니다.");
    }

    var requiredValidation = ValidateRequiredFields(schema);
    if (requiredValidation.IsValid is false)
    {
      return requiredValidation;
    }

    var arrayValidation = ValidateArrayProperties(properties);
    if (arrayValidation.IsValid is false)
    {
      return arrayValidation;
    }

    var propObjectsValidation = ValidatePropertyObjects(properties);
    if (propObjectsValidation.IsValid is false)
    {
      return propObjectsValidation;
    }

    var refValidation = ValidateRefFields(properties);
    if (refValidation.IsValid is false)
    {
      return refValidation;
    }

    var customFormatValidation = ValidateCustomFormats(properties);
    if (customFormatValidation.IsValid is false)
    {
      return customFormatValidation;
    }

    var domainArray = schema["domain"] as JArray;
    if (domainArray is null || domainArray.Count is 0)
    {
      return new ValidationResult(false, "Schema must have a non-empty \"domain\" array.");
    }

    foreach (var token in domainArray)
    {
      if (string.IsNullOrWhiteSpace(token.Value<string>()))
      {
        return new ValidationResult(false, "Schema \"domain\" array must contain non-empty string values.");
      }
    }

    if (allowedDomains is not null)
    {
      foreach (var token in domainArray)
      {
        var domainValue = token.Value<string>() ?? string.Empty;
        if (allowedDomains.Contains(domainValue) is false)
        {
          return new ValidationResult(false, $"\"{domainValue}\"은(는) Config에 정의되지 않은 도메인입니다.");
        }
      }

      var propertyDomainValidation = ValidatePropertyDomainsAgainstConfig(properties, allowedDomains);
      if (propertyDomainValidation.IsValid is false)
      {
        return propertyDomainValidation;
      }
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  public IReadOnlyList<SchemaColumn> ParseSchema(string schemaText)
  {
    var schema = JObject.Parse(schemaText);
    var properties = schema["properties"] as JObject;
    if (properties is null)
    {
      return [];
    }

    var required = GetRequiredSet(schema["required"] as JArray);

    var columns = new List<SchemaColumn>();
    foreach (var property in properties.Properties())
    {
      if (property.Value is not JObject propObj)
      {
        continue;
      }
      columns.Add(this.CreateSchemaColumn(property.Name, propObj, required));
    }

    return columns;
  }

  public void SaveSchema(string path, string text)
  {
    var directory = Path.GetDirectoryName(path);
    if (string.IsNullOrWhiteSpace(directory) is false)
    {
      Directory.CreateDirectory(directory);
    }

    File.WriteAllText(path, text);
  }

  public PivotInfo? ParsePivotInfo(string schemaText)
  {
    var schema = JObject.Parse(schemaText);
    var pivotColumn = schema["PivotColumn"]?.Value<string>();

    if (string.IsNullOrWhiteSpace(pivotColumn))
    {
      return null;
    }

    var pivotName = schema["PivotName"]?.Value<string>() ?? string.Empty;
    return new PivotInfo(pivotColumn, pivotName);
  }

  public IReadOnlyList<string> ParseSchemaDomains(string schemaText)
  {
    var schema = JObject.Parse(schemaText);
    return (schema["domain"] as JArray)?.Values<string>().OfType<string>().ToList() ?? [];
  }

  public ValidationResult RenameSchema(string currentName, string newName, string schemaDirectory, string jsonDirectory, string excelDirectory, IReadOnlyList<string>? domainDirectories = null)
  {
    var newSchemaPath = Path.Combine(schemaDirectory, $"{newName}.schema.json");
    var oldJsonPath = Path.Combine(jsonDirectory, $"{currentName}.json");
    var newJsonPath = Path.Combine(jsonDirectory, $"{newName}.json");
    var oldExcelPath = Path.Combine(excelDirectory, $"{currentName}.xlsx");
    var newExcelPath = Path.Combine(excelDirectory, $"{newName}.xlsx");

    if (File.Exists(newSchemaPath))
    {
      return new ValidationResult(false, $"'{newName}' 이름의 스키마가 이미 존재합니다.");
    }

    if (File.Exists(oldJsonPath) && File.Exists(newJsonPath))
    {
      return new ValidationResult(false, $"'{newName}.json' 파일이 이미 존재합니다.");
    }

    if (File.Exists(oldExcelPath) && File.Exists(newExcelPath))
    {
      return new ValidationResult(false, $"'{newName}.xlsx' 파일이 이미 존재합니다.");
    }

    if (domainDirectories is not null)
    {
      foreach (var domainDir in domainDirectories)
      {
        var oldDomainPath = Path.Combine(domainDir, $"{currentName}.json");
        var newDomainPath = Path.Combine(domainDir, $"{newName}.json");
        if (File.Exists(oldDomainPath) && File.Exists(newDomainPath))
        {
          return new ValidationResult(false, $"'{newName}.json' 파일이 이미 존재합니다. ({Path.GetFileName(domainDir)})");
        }
      }
    }

    var oldSchemaPath = Path.Combine(schemaDirectory, $"{currentName}.schema.json");
    var renames = new List<(string From, string To)> { (oldSchemaPath, newSchemaPath) };

    if (File.Exists(oldJsonPath))
    {
      renames.Add((oldJsonPath, newJsonPath));
    }

    if (File.Exists(oldExcelPath))
    {
      renames.Add((oldExcelPath, newExcelPath));
    }

    if (domainDirectories is not null)
    {
      foreach (var domainDir in domainDirectories)
      {
        var oldDomainPath = Path.Combine(domainDir, $"{currentName}.json");
        if (File.Exists(oldDomainPath))
        {
          renames.Add((oldDomainPath, Path.Combine(domainDir, $"{newName}.json")));
        }
      }
    }

    var completed = new List<(string From, string To)>();
    try
    {
      foreach (var (from, to) in renames)
      {
        File.Move(from, to);
        completed.Add((from, to));
      }

      return new ValidationResult(true, string.Empty);
    }
    catch (Exception ex)
    {
      for (var i = completed.Count - 1; i >= 0; i--)
      {
        var (from, to) = completed[i];
        try
        {
          File.Move(to, from);
        }
        catch
        {
        }
      }

      return new ValidationResult(false, ex.Message);
    }
  }

  private SchemaColumn CreateSchemaColumn(string name, JObject? property, HashSet<string> required)
  {
    if (property is null)
    {
      return new SchemaColumn { Name = name };
    }

    var jsonType = GetJsonType(property);
    var isNullable = IsNullable(property);
    var isRequired = required.Contains(name);
    var @ref = property["ref"]?.Value<string>();
    var domain = (property["domain"] as JArray)?.Values<string>().OfType<string>().ToList();
    var minimum = property["minimum"]?.Value<double?>();
    var maximum = property["maximum"]?.Value<double?>();

    var format = property["format"]?.Value<string>();

    if (jsonType is "array")
    {
      var minItems = property["minItems"]?.Value<int>() ?? 0;
      var maxItems = property["maxItems"]?.Value<int>() ?? 0;
      var itemsObj = property["items"] as JObject;
      var itemType = itemsObj is not null ? GetJsonType(itemsObj) : "string";
      var itemName = property["itemName"]?.Value<string>() ?? string.Empty;
      var keyColumn = property["keyColumn"]?.Value<string>();

      var itemChildren = new List<SchemaColumn>();
      if (itemType is "object" && itemsObj is not null)
      {
        var childProperties = itemsObj["properties"] as JObject;
        var childRequired = GetRequiredSet(itemsObj["required"] as JArray);
        if (childProperties is not null)
        {
          foreach (var child in childProperties.Properties())
          {
            if (child.Value is not JObject childPropObj)
            {
              continue;
            }
            itemChildren.Add(this.CreateSchemaColumn(child.Name, childPropObj, childRequired));
          }
        }
      }

      return new SchemaColumn
      {
        Name = name,
        JsonType = jsonType,
        IsRequired = isRequired,
        IsNullable = isNullable,
        ArrayMin = minItems,
        ArrayMax = maxItems,
        ItemJsonType = itemType,
        ItemName = itemName,
        ItemChildren = itemChildren,
        Domain = domain,
        Format = format,
        KeyColumn = keyColumn
      };
    }

    var children = new List<SchemaColumn>();
    if (jsonType is "object")
    {
      var childProperties = property["properties"] as JObject;
      var childRequired = GetRequiredSet(property["required"] as JArray);

      if (childProperties is not null)
      {
        foreach (var child in childProperties.Properties())
        {
          if (child.Value is not JObject childPropObj)
          {
            continue;
          }
          children.Add(this.CreateSchemaColumn(child.Name, childPropObj, childRequired));
        }
      }
    }

    return new SchemaColumn
    {
      Name = name,
      JsonType = jsonType,
      IsRequired = isRequired,
      IsNullable = isNullable,
      Children = children,
      Ref = @ref,
      Minimum = minimum,
      Maximum = maximum,
      Domain = domain,
      Format = format
    };
  }

  private static ValidationResult ValidatePropertyDomainsAgainstConfig(JObject properties, IReadOnlyList<string> allowedDomains)
  {
    foreach (var property in properties.Properties())
    {
      var propObj = property.Value as JObject;
      if (propObj is null)
      {
        continue;
      }

      var propDomainArray = propObj["domain"] as JArray;
      if (propDomainArray is not null)
      {
        foreach (var token in propDomainArray)
        {
          var domainValue = token.Value<string>() ?? string.Empty;
          if (allowedDomains.Contains(domainValue) is false)
          {
            return new ValidationResult(false, $"'{property.Name}'의 도메인 \"{domainValue}\"은(는) Config에 정의되지 않은 도메인입니다.");
          }
        }
      }

      var jsonType = GetJsonType(propObj);
      if (jsonType is "object")
      {
        var childProps = propObj["properties"] as JObject;
        if (childProps is not null)
        {
          var childResult = ValidatePropertyDomainsAgainstConfig(childProps, allowedDomains);
          if (childResult.IsValid is false)
          {
            return childResult;
          }
        }
      }
      else if (jsonType is "array")
      {
        var itemsObj = propObj["items"] as JObject;
        if (itemsObj is not null && GetJsonType(itemsObj) is "object")
        {
          var childProps = itemsObj["properties"] as JObject;
          if (childProps is not null)
          {
            var childResult = ValidatePropertyDomainsAgainstConfig(childProps, allowedDomains);
            if (childResult.IsValid is false)
            {
              return childResult;
            }
          }
        }
      }
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidateRequiredFields(JObject schema)
  {
    var properties = schema["properties"] as JObject;
    var requiredArray = schema["required"] as JArray;

    if (properties is not null && requiredArray is not null)
    {
      foreach (var token in requiredArray)
      {
        var name = token.Value<string>();
        if (string.IsNullOrWhiteSpace(name) is false && properties[name] is null)
        {
          return new ValidationResult(false, $"required 필드 '{name}'이(가) properties에 존재하지 않습니다.");
        }
      }
    }

    if (properties is null)
    {
      return new ValidationResult(true, "Validation succeeded.");
    }

    foreach (var property in properties.Properties())
    {
      if (property.Value is not JObject propObj)
      {
        continue;
      }

      var type = GetJsonType(propObj);

      if (type is "object")
      {
        var nested = ValidateRequiredFields(propObj);
        if (nested.IsValid is false)
        {
          return nested;
        }
      }
      else if (type is "array")
      {
        var itemsObj = propObj["items"] as JObject;
        if (itemsObj is not null && GetJsonType(itemsObj) is "object")
        {
          var nested = ValidateRequiredFields(itemsObj);
          if (nested.IsValid is false)
          {
            return nested;
          }
        }
      }
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidateArrayProperties(JObject properties)
  {
    foreach (var property in properties.Properties())
    {
      var propObj = property.Value as JObject;
      if (propObj is null)
      {
        continue;
      }

      var type = GetJsonType(propObj);
      if (type is "array")
      {
        if (propObj["minItems"] is null || propObj["maxItems"] is null)
        {
          return new ValidationResult(false, $"Array property '{property.Name}' must have minItems and maxItems.");
        }
      }
      else if (type is "object")
      {
        var childProps = propObj["properties"] as JObject;
        if (childProps is not null)
        {
          var childResult = ValidateArrayProperties(childProps);
          if (childResult.IsValid is false)
          {
            return childResult;
          }
        }
      }
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidatePropertyObjects(JObject properties)
  {
    foreach (var property in properties.Properties())
    {
      var propObj = property.Value as JObject;
      if (propObj is null)
      {
        return new ValidationResult(false, $"속성 '{property.Name}'의 정의는 JSON 객체여야 합니다.");
      }

      var type = GetJsonType(propObj);
      if (type is "object")
      {
        var childProps = propObj["properties"] as JObject;
        if (childProps is not null)
        {
          var childResult = ValidatePropertyObjects(childProps);
          if (childResult.IsValid is false)
          {
            return childResult;
          }
        }
      }
      else if (type is "array")
      {
        var itemsObj = propObj["items"] as JObject;
        if (itemsObj is not null && GetJsonType(itemsObj) is "object")
        {
          var childProps = itemsObj["properties"] as JObject;
          if (childProps is not null)
          {
            var childResult = ValidatePropertyObjects(childProps);
            if (childResult.IsValid is false)
            {
              return childResult;
            }
          }
        }
      }
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidateRefFields(JObject properties)
  {
    foreach (var property in properties.Properties())
    {
      var propObj = property.Value as JObject;
      if (propObj is null)
      {
        continue;
      }

      var refValue = propObj["ref"]?.Value<string>();
      if (refValue is not null)
      {
        var type = GetJsonType(propObj);
        if (type is "object" or "array")
        {
          return new ValidationResult(false, $"ref 필드 '{property.Name}'은(는) 스칼라 타입 컬럼에만 사용할 수 있습니다.");
        }

        const string refSeparator = ".schema.json#/definitions/";
        var sepIdx = refValue.IndexOf(refSeparator, StringComparison.Ordinal);
        if (sepIdx <= 0)
        {
          return new ValidationResult(false, $"ref '{refValue}' 형식이 올바르지 않습니다. 'SchemaName.schema.json#/definitions/ColumnName' 형식이어야 합니다.");
        }
        var columnPath = refValue[(sepIdx + refSeparator.Length)..];
        if (string.IsNullOrWhiteSpace(columnPath))
        {
          return new ValidationResult(false, $"ref '{refValue}'의 컬럼 경로가 비어있습니다.");
        }
      }

      var jsonType = GetJsonType(propObj);
      if (jsonType is "object")
      {
        var childProps = propObj["properties"] as JObject;
        if (childProps is not null)
        {
          var childResult = ValidateRefFields(childProps);
          if (childResult.IsValid is false)
          {
            return childResult;
          }
        }
      }
      else if (jsonType is "array")
      {
        var itemsObj = propObj["items"] as JObject;
        if (itemsObj is not null && GetJsonType(itemsObj) is "object")
        {
          var childProps = itemsObj["properties"] as JObject;
          if (childProps is not null)
          {
            var childResult = ValidateRefFields(childProps);
            if (childResult.IsValid is false)
            {
              return childResult;
            }
          }
        }
      }
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidateCustomFormats(JObject properties)
  {
    foreach (var property in properties.Properties())
    {
      var propObj = property.Value as JObject;
      if (propObj is null)
      {
        continue;
      }

      var format = propObj["format"]?.Value<string>();
      if (format is not null)
      {
        var formatResult = ValidateFormat(property.Name, propObj, format);
        if (formatResult.IsValid is false)
        {
          return formatResult;
        }
      }

      var jsonType = GetJsonType(propObj);
      if (jsonType is "object")
      {
        var childProps = propObj["properties"] as JObject;
        if (childProps is not null)
        {
          var childResult = ValidateCustomFormats(childProps);
          if (childResult.IsValid is false)
          {
            return childResult;
          }
        }
      }
      else if (jsonType is "array")
      {
        var itemsObj = propObj["items"] as JObject;
        if (itemsObj is not null)
        {
          if (GetJsonType(itemsObj) is "object")
          {
            var itemChildProps = itemsObj["properties"] as JObject;
            if (itemChildProps is not null)
            {
              var childResult = ValidateCustomFormats(itemChildProps);
              if (childResult.IsValid is false)
              {
                return childResult;
              }
            }
          }
        }
      }
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidateFormat(string propertyName, JObject propObj, string format)
  {
    var jsonType = GetJsonType(propObj);

    return format switch
    {
      "vector2" => ValidateVectorFormat(propertyName, propObj, jsonType, ["x", "y"]),
      "vector3" => ValidateVectorFormat(propertyName, propObj, jsonType, ["x", "y", "z"]),
      "timespan" => ValidateTimespanObjectFormat(propertyName, propObj, jsonType),
      "datetime" => ValidateDatetimeFormat(propertyName, jsonType),
      "timespan-hour" or "timespan-minute" or "timespan-second" or "timespan-millisecond"
        => ValidateTimespanFormat(propertyName, jsonType),
      "readonly-list" => ValidateReadonlyListFormat(propertyName, jsonType),
      "frozen-dictionary" => ValidateFrozenDictionaryFormat(propertyName, propObj, jsonType),
      _ => new ValidationResult(false, $"'{propertyName}'에 알 수 없는 format '{format}'이(가) 지정되었습니다.")
    };
  }

  private static ValidationResult ValidateVectorFormat(string propertyName, JObject propObj, string jsonType, string[] components)
  {
    if (jsonType is not "object")
    {
      return new ValidationResult(false, $"'{propertyName}'의 vector format은 type이 object이어야 합니다.");
    }

    var childProps = propObj["properties"] as JObject;
    foreach (var component in components)
    {
      if (childProps?[component] is not JObject componentObj)
      {
        return new ValidationResult(false, $"'{propertyName}'의 vector format에 '{component}' 프로퍼티가 없습니다.");
      }

      if (GetJsonType(componentObj) is not "number")
      {
        return new ValidationResult(false, $"'{propertyName}.{component}'는 number 타입이어야 합니다.");
      }
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidateDatetimeFormat(string propertyName, string jsonType)
  {
    if (jsonType is not "string")
    {
      return new ValidationResult(false, $"'{propertyName}'의 datetime format은 type이 string이어야 합니다.");
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidateTimespanFormat(string propertyName, string jsonType)
  {
    if (jsonType is not "integer")
    {
      return new ValidationResult(false, $"'{propertyName}'의 timespan format은 type이 integer이어야 합니다.");
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidateTimespanObjectFormat(string propertyName, JObject propObj, string jsonType)
  {
    if (jsonType is not "object")
    {
      return new ValidationResult(false, $"'{propertyName}'의 timespan format은 type이 object이어야 합니다.");
    }

    string[] components = ["Hour", "Minute", "Second", "Millisecond"];
    var childProps = propObj["properties"] as JObject;
    foreach (var component in components)
    {
      if (childProps?[component] is not JObject componentObj)
      {
        return new ValidationResult(false, $"'{propertyName}'의 timespan format에 '{component}' 프로퍼티가 없습니다.");
      }

      if (GetJsonType(componentObj) is not "integer")
      {
        return new ValidationResult(false, $"'{propertyName}.{component}'는 integer 타입이어야 합니다.");
      }
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidateReadonlyListFormat(string propertyName, string jsonType)
  {
    if (jsonType is not "array")
    {
      return new ValidationResult(false, $"'{propertyName}'의 readonly-list format은 type이 array이어야 합니다.");
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static ValidationResult ValidateFrozenDictionaryFormat(string propertyName, JObject propObj, string jsonType)
  {
    if (jsonType is not "array")
    {
      return new ValidationResult(false, $"'{propertyName}'의 frozen-dictionary format은 type이 array이어야 합니다.");
    }

    var keyColumn = propObj["keyColumn"]?.Value<string>();
    if (string.IsNullOrWhiteSpace(keyColumn))
    {
      return new ValidationResult(false, $"'{propertyName}'의 frozen-dictionary format은 keyColumn이 필요합니다.");
    }

    var itemsObj = propObj["items"] as JObject;
    if (itemsObj is null || GetJsonType(itemsObj) is not "object")
    {
      return new ValidationResult(false, $"'{propertyName}'의 frozen-dictionary format은 items가 object 타입이어야 합니다.");
    }

    var itemsProperties = itemsObj["properties"] as JObject;
    if (itemsProperties?[keyColumn] is null)
    {
      return new ValidationResult(false, $"'{propertyName}'의 frozen-dictionary keyColumn '{keyColumn}'이(가) items.properties에 없습니다.");
    }

    return new ValidationResult(true, "Validation succeeded.");
  }

  private static string GetJsonType(JObject property)
  {
    var type = property["type"];
    if (type is JArray array)
    {
      foreach (var item in array)
      {
        var t = item.Value<string>();
        if (t is "object") { return "object"; }
        if (t is "integer") { return "integer"; }
        if (t is "number") { return "number"; }
        if (t is "array") { return "array"; }
      }

      return "string";
    }

    return type?.Value<string>() switch
    {
      "integer" => "integer",
      "number" => "number",
      "object" => "object",
      "array" => "array",
      _ => "string"
    };
  }

  private static bool IsNullable(JObject property)
  {
    var type = property["type"];
    if (type is JArray array)
    {
      return array.Values<string>().Contains("null");
    }

    return false;
  }

  private static HashSet<string> GetRequiredSet(JArray? requiredArray)
  {
    if (requiredArray is null)
    {
      return [];
    }

    return requiredArray.Values<string>().OfType<string>().ToHashSet();
  }

  private static string GetSchemaName(string filePath)
  {
    var name = Path.GetFileNameWithoutExtension(filePath);
    if (name.EndsWith(".schema", StringComparison.OrdinalIgnoreCase))
    {
      return Path.GetFileNameWithoutExtension(name);
    }

    return name;
  }
}
