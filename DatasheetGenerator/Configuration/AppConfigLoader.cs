namespace DatasheetGenerator.Configuration;

using System.IO;
using Newtonsoft.Json;

public sealed class AppConfigLoader
{
  public AppConfigLoadResult Load(string configPath)
  {
    if (File.Exists(configPath) is false)
    {
      return new AppConfigLoadResult(false, $"Config file does not exist. Add OutputRootPath to {configPath}.", string.Empty, [], string.Empty, string.Empty);
    }

    var text = File.ReadAllText(configPath);
    var config = JsonConvert.DeserializeObject<AppConfig>(text);
    if (string.IsNullOrWhiteSpace(config?.OutputRootPath))
    {
      return new AppConfigLoadResult(false, "OutputRootPath is not configured.", string.Empty, [], string.Empty, string.Empty);
    }

    if (config.Domains is null || config.Domains.Count is 0)
    {
      return new AppConfigLoadResult(false, "Domains is not configured.", string.Empty, [], string.Empty, string.Empty);
    }

    var configDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();

    var outputRootPath = config.OutputRootPath;
    if (Path.IsPathRooted(outputRootPath) is false)
    {
      outputRootPath = Path.GetFullPath(Path.Combine(configDirectory, outputRootPath));
    }

    var codeOutputPath = string.Empty;
    if (string.IsNullOrWhiteSpace(config.CodeOutputPath) is false)
    {
      codeOutputPath = config.CodeOutputPath;
      if (Path.IsPathRooted(codeOutputPath) is false)
      {
        codeOutputPath = Path.GetFullPath(Path.Combine(configDirectory, codeOutputPath));
      }
    }

    var enumFilePath = string.Empty;
    if (string.IsNullOrWhiteSpace(config.EnumFilePath) is false)
    {
      enumFilePath = config.EnumFilePath;
      if (Path.IsPathRooted(enumFilePath) is false)
      {
        enumFilePath = Path.GetFullPath(Path.Combine(configDirectory, enumFilePath));
      }
    }

    return new AppConfigLoadResult(true, "Config loaded.", outputRootPath, config.Domains ?? [], codeOutputPath, enumFilePath);
  }
}
