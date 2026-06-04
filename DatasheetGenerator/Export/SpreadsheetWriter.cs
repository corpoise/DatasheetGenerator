namespace DatasheetGenerator.Export;

using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using DatasheetGenerator.Models;

public static class SpreadsheetWriter
{
  public static void Write(IReadOnlyList<FlatColumn> columns, DataTable data, string path)
  {
    if (File.Exists(path))
    {
      File.Delete(path);
    }

    var directory = Path.GetDirectoryName(path);
    if (string.IsNullOrWhiteSpace(directory) is false)
    {
      Directory.CreateDirectory(directory);
    }

    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    WriteEntry(archive, "[Content_Types].xml", CreateContentTypes());
    WriteEntry(archive, "_rels/.rels", CreateRootRelationships());
    WriteEntry(archive, "xl/workbook.xml", CreateWorkbook());
    WriteEntry(archive, "xl/_rels/workbook.xml.rels", CreateWorkbookRelationships());
    WriteEntry(archive, "xl/worksheets/sheet1.xml", CreateWorksheet(columns, data));
  }

  private static void WriteEntry(ZipArchive archive, string name, string content)
  {
    var entry = archive.CreateEntry(name);
    using var stream = entry.Open();
    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
    writer.Write(content);
  }

  private static string CreateContentTypes()
  {
    return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
</Types>
""";
  }

  private static string CreateRootRelationships()
  {
    return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""";
  }

  private static string CreateWorkbook()
  {
    return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="Data" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>
""";
  }

  private static string CreateWorkbookRelationships()
  {
    return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
</Relationships>
""";
  }

  private static string CreateWorksheet(IReadOnlyList<FlatColumn> columns, DataTable data)
  {
    var hasLevel1 = columns.Any(c => string.IsNullOrEmpty(c.Level1Group) is false);
    var hasLevel2 = columns.Any(c => string.IsNullOrEmpty(c.Level2Group) is false);
    var headerRowCount = 1 + (hasLevel1 ? 1 : 0) + (hasLevel2 ? 1 : 0);

    var builder = new StringBuilder();
    builder.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
    builder.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
    builder.AppendLine("  <sheetData>");

    var currentRow = 1;

    if (hasLevel1)
    {
      AppendGroupRow(builder, columns, currentRow, c => c.Level1Group);
      currentRow++;
    }

    if (hasLevel2)
    {
      AppendGroupRow(builder, columns, currentRow, c => c.Level2Group);
      currentRow++;
    }

    AppendLeafNameRow(builder, columns, currentRow);
    currentRow++;

    AppendDataRows(builder, columns, data, currentRow);

    builder.AppendLine("  </sheetData>");
    AppendMergeCells(builder, columns, hasLevel1, hasLevel2);
    builder.AppendLine("</worksheet>");
    return builder.ToString();
  }

  private static void AppendGroupRow(StringBuilder builder, IReadOnlyList<FlatColumn> columns, int rowIndex, Func<FlatColumn, string> groupKey)
  {
    builder.AppendLine(CultureInfo.InvariantCulture, $"""    <row r="{rowIndex}">""");
    for (var i = 0; i < columns.Count; i++)
    {
      AppendInlineStringCell(builder, i + 1, rowIndex, groupKey(columns[i]));
    }

    builder.AppendLine("    </row>");
  }

  private static void AppendLeafNameRow(StringBuilder builder, IReadOnlyList<FlatColumn> columns, int rowIndex)
  {
    builder.AppendLine(CultureInfo.InvariantCulture, $"""    <row r="{rowIndex}">""");
    for (var i = 0; i < columns.Count; i++)
    {
      AppendInlineStringCell(builder, i + 1, rowIndex, columns[i].LeafName);
    }

    builder.AppendLine("    </row>");
  }

  private static void AppendDataRows(StringBuilder builder, IReadOnlyList<FlatColumn> columns, DataTable data, int startRowIndex)
  {
    var rowIndex = startRowIndex;
    foreach (DataRow row in data.Rows)
    {
      if (row.RowState is DataRowState.Deleted)
      {
        continue;
      }

      builder.AppendLine(CultureInfo.InvariantCulture, $"""    <row r="{rowIndex}">""");
      for (var i = 0; i < columns.Count; i++)
      {
        var value = Convert.ToString(row[columns[i].Path]) ?? string.Empty;
        AppendInlineStringCell(builder, i + 1, rowIndex, value);
      }

      builder.AppendLine("    </row>");
      rowIndex++;
    }
  }

  private static void AppendInlineStringCell(StringBuilder builder, int columnIndex, int rowIndex, string value)
  {
    var cellReference = GetCellReference(columnIndex, rowIndex);
    var escapedValue = SecurityElement.Escape(value) ?? string.Empty;
    builder.AppendLine(CultureInfo.InvariantCulture, $"""      <c r="{cellReference}" t="inlineStr"><is><t>{escapedValue}</t></is></c>""");
  }

  private static void AppendMergeCells(StringBuilder builder, IReadOnlyList<FlatColumn> columns, bool hasLevel1, bool hasLevel2)
  {
    var merges = new List<string>();
    var currentRow = 1;

    if (hasLevel1)
    {
      merges.AddRange(GetMergeReferences(columns, currentRow, c => c.Level1Group));
      currentRow++;
    }

    if (hasLevel2)
    {
      merges.AddRange(GetLevel2MergeReferences(columns, currentRow));
    }

    if (merges.Count is 0)
    {
      return;
    }

    builder.AppendLine(CultureInfo.InvariantCulture, $"""  <mergeCells count="{merges.Count}">""");
    foreach (var merge in merges)
    {
      builder.AppendLine(CultureInfo.InvariantCulture, $"""    <mergeCell ref="{merge}"/>""");
    }

    builder.AppendLine("  </mergeCells>");
  }

  private static IReadOnlyList<string> GetMergeReferences(IReadOnlyList<FlatColumn> columns, int rowIndex, Func<FlatColumn, string> groupKey)
  {
    var references = new List<string>();
    var i = 0;
    while (i < columns.Count)
    {
      var key = groupKey(columns[i]);
      if (string.IsNullOrEmpty(key))
      {
        i++;
        continue;
      }

      var j = i + 1;
      while (j < columns.Count && groupKey(columns[j]) == key)
      {
        j++;
      }

      if (j > i + 1)
      {
        references.Add($"{GetCellReference(i + 1, rowIndex)}:{GetCellReference(j, rowIndex)}");
      }

      i = j;
    }

    return references;
  }

  private static IReadOnlyList<string> GetLevel2MergeReferences(IReadOnlyList<FlatColumn> columns, int rowIndex)
  {
    var references = new List<string>();
    var i = 0;
    while (i < columns.Count)
    {
      var col = columns[i];
      if (string.IsNullOrEmpty(col.Level2Group))
      {
        i++;
        continue;
      }

      var j = i + 1;
      while (j < columns.Count && columns[j].Level1Group == col.Level1Group && columns[j].Level2Group == col.Level2Group)
      {
        j++;
      }

      if (j > i + 1)
      {
        references.Add($"{GetCellReference(i + 1, rowIndex)}:{GetCellReference(j, rowIndex)}");
      }

      i = j;
    }

    return references;
  }

  private static string GetCellReference(int columnIndex, int rowIndex)
  {
    var dividend = columnIndex;
    var columnName = string.Empty;

    while (dividend > 0)
    {
      var modulo = (dividend - 1) % 26;
      columnName = Convert.ToChar(65 + modulo) + columnName;
      dividend = (dividend - modulo) / 26;
    }

    return $"{columnName}{rowIndex}";
  }
}
