namespace DatasheetGenerator.Models;

public sealed record SchemaInfo
{
  public string Name { get; init; } = string.Empty;
  public string FilePath { get; init; } = string.Empty;
}
