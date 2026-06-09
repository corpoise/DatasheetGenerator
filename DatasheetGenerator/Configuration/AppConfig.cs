namespace DatasheetGenerator.Configuration;

public sealed record AppConfig
{
  public string? OutputRootPath { get; init; }
  public IReadOnlyList<string>? Domains { get; init; }
  public string? CodeOutputPath { get; init; }
  public string? EnumFilePath { get; init; }
}
