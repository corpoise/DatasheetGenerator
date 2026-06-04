namespace DatasheetGenerator.Configuration;

using System.IO;
using Newtonsoft.Json;

public sealed class AppConfigLoader
{
  public AppConfigLoadResult Load(string configPath)
  {
    if (File.Exists(configPath) is false)
    {
      return new AppConfigLoadResult(false, $"Config file does not exist. Add OutputRootPath to {configPath}.", string.Empty, []);
    }

    var text = File.ReadAllText(configPath);
    var config = JsonConvert.DeserializeObject<AppConfig>(text);
    if (string.IsNullOrWhiteSpace(config?.OutputRootPath))
    {
      return new AppConfigLoadResult(false, "OutputRootPath is not configured.", string.Empty, []);
    }

    if (config.Domains is null || config.Domains.Count is 0)
    {
      return new AppConfigLoadResult(false, "Domains is not configured.", string.Empty, []);
    }

    var outputRootPath = config.OutputRootPath;
    if (Path.IsPathRooted(outputRootPath) is false)
    {
      var directory = Path.GetDirectoryName(configPath);
      if (string.IsNullOrWhiteSpace(directory))
      {
        directory = Directory.GetCurrentDirectory();
      }

      outputRootPath = Path.GetFullPath(Path.Combine(directory, outputRootPath));
    }

    return new AppConfigLoadResult(true, "Config loaded.", outputRootPath, config.Domains ?? []);
  }
}
