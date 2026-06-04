namespace DatasheetGenerator.Models;

public sealed record SchemaGraphEdge
{
  public string FromNode { get; init; } = string.Empty;
  public string FromColumn { get; init; } = string.Empty;
  public string ToNode { get; init; } = string.Empty;
  public string ToColumn { get; init; } = string.Empty;
}
