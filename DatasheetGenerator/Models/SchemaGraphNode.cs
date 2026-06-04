namespace DatasheetGenerator.Models;

public sealed record SchemaGraphNode
{
  public string Schema { get; init; } = string.Empty;
  public int Level { get; init; }
  public IReadOnlyList<SchemaGraphColumn> Columns { get; init; } = [];
}
