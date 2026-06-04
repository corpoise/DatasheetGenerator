namespace DatasheetGenerator.Models;

public sealed record SchemaGraphColumn
{
  public string Path { get; init; } = string.Empty;
  public string? Ref { get; init; }
}
