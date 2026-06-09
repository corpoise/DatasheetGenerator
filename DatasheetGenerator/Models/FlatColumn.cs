namespace DatasheetGenerator.Models;

public sealed record FlatColumn
{
  public string Path { get; init; } = string.Empty;
  public string LeafName { get; init; } = string.Empty;
  public string Level1Group { get; init; } = string.Empty;
  public string Level2Group { get; init; } = string.Empty;
  public string JsonType { get; init; } = "string";
  public bool IsRequired { get; init; }
  public bool IsNullable { get; init; }
  public string? Ref { get; init; }
  public double? Minimum { get; init; }
  public double? Maximum { get; init; }
  public bool ShowRequiredMark { get; init; }
  public string? Format { get; init; }
}
