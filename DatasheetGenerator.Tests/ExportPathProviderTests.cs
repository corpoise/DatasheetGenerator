namespace DatasheetGenerator.Tests;

using DatasheetGenerator.Export;

public sealed class ExportPathProviderTests
{
  [Fact]
  public void GetPaths_CreatesJsonSchemaAndExcelDirectories()
  {
    var directory = CreateTempDirectory();
    try
    {
      var provider = new ExportPathProvider(directory);

      provider.GetPaths("Character");

      Assert.True(Directory.Exists(Path.Combine(directory, "Json")));
      Assert.True(Directory.Exists(Path.Combine(directory, "Schema")));
      Assert.True(Directory.Exists(Path.Combine(directory, "Excel")));
    }
    finally
    {
      Directory.Delete(directory, true);
    }
  }

  [Fact]
  public void GetPaths_ReturnsExpectedOutputPaths()
  {
    var directory = CreateTempDirectory();
    try
    {
      var provider = new ExportPathProvider(directory);

      var paths = provider.GetPaths("Character");

      Assert.Equal(Path.Combine(directory, "Json", "Character.json"), paths.JsonPath);
      Assert.Equal(Path.Combine(directory, "Schema", "Character.schema.json"), paths.SchemaPath);
      Assert.Equal(Path.Combine(directory, "Excel", "Character.xlsx"), paths.XlsxPath);
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
