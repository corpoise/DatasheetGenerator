namespace DatasheetGenerator.Models;

public sealed record SchemaGraphDto
{
  public string RootSchema { get; init; } = string.Empty;
  public IReadOnlyList<SchemaGraphNode> Nodes { get; init; } = [];
  public IReadOnlyList<SchemaGraphEdge> Edges { get; init; } = [];
}
