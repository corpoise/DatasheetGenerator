namespace DatasheetGenerator.Export;

using System.IO;

public sealed class ExportPathProvider
{
  private readonly string outputRootPath;

  public ExportPathProvider(string outputRootPath)
  {
    this.outputRootPath = outputRootPath;
  }

  public ExportPaths GetPaths(string datasheetName)
  {
    var jsonDirectory = Path.Combine(this.outputRootPath, "Json");
    var schemaDirectory = Path.Combine(this.outputRootPath, "Schema");
    var excelDirectory = Path.Combine(this.outputRootPath, "Excel");

    Directory.CreateDirectory(jsonDirectory);
    Directory.CreateDirectory(schemaDirectory);
    Directory.CreateDirectory(excelDirectory);

    return new ExportPaths(
      Path.Combine(schemaDirectory, $"{datasheetName}.schema.json"),
      Path.Combine(jsonDirectory, $"{datasheetName}.json"),
      Path.Combine(excelDirectory, $"{datasheetName}.xlsx"));
  }

  public string GetDomainJsonPath(string datasheetName, string domain)
  {
    var domainDirectory = Path.Combine(this.outputRootPath, domain);
    Directory.CreateDirectory(domainDirectory);
    return Path.Combine(domainDirectory, $"{datasheetName}.json");
  }
}
