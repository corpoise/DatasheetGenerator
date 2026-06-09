namespace DatasheetGenerator.Configuration;

public sealed record AppConfigLoadResult(bool IsValid, string Message, string OutputRootPath, IReadOnlyList<string> Domains, string CodeOutputPath, string EnumFilePath);
