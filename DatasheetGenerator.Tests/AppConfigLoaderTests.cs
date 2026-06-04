namespace DatasheetGenerator.Tests;

using DatasheetGenerator.Configuration;

public sealed class AppConfigLoaderTests
{
  [Fact]
  public void Load_WhenConfigFileDoesNotExist_ReturnsFailure()
  {
    var loader = new AppConfigLoader();
    var path = Path.Combine(Path.GetTempPath(), $"Missing_{Guid.NewGuid():N}.json");

    var result = loader.Load(path);

    Assert.False(result.IsValid);
    Assert.Contains("Config file does not exist.", result.Message);
  }

  [Fact]
  public void Load_WhenOutputRootPathIsMissing_ReturnsFailure()
  {
    var directory = CreateTempDirectory();
    try
    {
      var path = Path.Combine(directory, "appsettings.json");
      File.WriteAllText(path, "{}");
      var loader = new AppConfigLoader();

      var result = loader.Load(path);

      Assert.False(result.IsValid);
      Assert.Equal("OutputRootPath is not configured.", result.Message);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void Load_WhenOutputRootPathExists_ReturnsResolvedPath()
  {
    var directory = CreateTempDirectory();
    try
    {
      var path = Path.Combine(directory, "appsettings.json");
      File.WriteAllText(path, """{ "OutputRootPath": "Output", "Domains": ["client"] }""");
      var loader = new AppConfigLoader();

      var result = loader.Load(path);

      Assert.True(result.IsValid);
      Assert.Equal(Path.Combine(directory, "Output"), result.OutputRootPath);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void Load_WhenDomainsConfigured_ReturnsDomains()
  {
    var directory = CreateTempDirectory();
    try
    {
      var path = Path.Combine(directory, "appsettings.json");
      File.WriteAllText(path, """{ "OutputRootPath": "Output", "Domains": [ "client", "server" ] }""");
      var loader = new AppConfigLoader();

      var result = loader.Load(path);

      Assert.True(result.IsValid);
      Assert.Equal(2, result.Domains.Count);
      Assert.Contains("client", result.Domains);
      Assert.Contains("server", result.Domains);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void Load_WhenDomainsAbsent_ReturnsFailure()
  {
    var directory = CreateTempDirectory();
    try
    {
      var path = Path.Combine(directory, "appsettings.json");
      File.WriteAllText(path, """{ "OutputRootPath": "Output" }""");
      var loader = new AppConfigLoader();

      var result = loader.Load(path);

      Assert.False(result.IsValid);
      Assert.Contains("Domains", result.Message);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void Load_WhenDomainsEmpty_ReturnsFailure()
  {
    var directory = CreateTempDirectory();
    try
    {
      var path = Path.Combine(directory, "appsettings.json");
      File.WriteAllText(path, """{ "OutputRootPath": "Output", "Domains": [] }""");
      var loader = new AppConfigLoader();

      var result = loader.Load(path);

      Assert.False(result.IsValid);
      Assert.Contains("Domains", result.Message);
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  private static string CreateTempDirectory()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"DatasheetGeneratorTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    return directory;
  }
}
