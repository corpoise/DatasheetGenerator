namespace DatasheetGenerator.Services;

using System.IO;
using System.Text;
using DatasheetGenerator.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

public sealed class SchemaGraphWriter
{
  private static readonly JsonSerializerSettings SerializerSettings = new()
  {
    ContractResolver = new CamelCasePropertyNamesContractResolver(),
    Formatting = Formatting.None
  };

  public void Write(SchemaGraphDto dto, string outputDirectory)
  {
    Directory.CreateDirectory(outputDirectory);
    var path = Path.Combine(outputDirectory, $"{dto.RootSchema}.graph.json");
    var json = JsonConvert.SerializeObject(dto, SerializerSettings);
    File.WriteAllText(path, json, Encoding.UTF8);
  }
}
