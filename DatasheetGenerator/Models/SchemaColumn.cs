namespace DatasheetGenerator.Models;

public sealed record SchemaColumn
{
  public string Name { get; init; } = string.Empty;
  public string JsonType { get; init; } = "string";
  public bool IsRequired { get; init; }
  public bool IsNullable { get; init; }
  public string? Ref { get; init; }
  public double? Minimum { get; init; }
  public double? Maximum { get; init; }
  public IReadOnlyList<SchemaColumn> Children { get; init; } = [];
  public int ArrayMin { get; init; }
  public int ArrayMax { get; init; }
  public string ItemJsonType { get; init; } = "string";
  public string ItemName { get; init; } = string.Empty;
  public IReadOnlyList<SchemaColumn> ItemChildren { get; init; } = [];
  public IReadOnlyList<string>? Domain { get; init; }
}
