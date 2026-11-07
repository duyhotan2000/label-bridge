using System.Text;
using System.Text.Json.Nodes;
using LabelBridge.Common;

namespace LabelBridge.Tests.Common;

public class SettingsFileTests
{
    readonly string _path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "appsettings.json");

    [Fact]
    public void Update_FileWithBom_PreservesOtherKeys()
    {
        const string json = """
            {
              "Logging": { "LogLevel": { "Default": "Information" } },
              "Bridge": {
                "PrinterName": "XP-470B Label (Chicken)",
                "PrinterHost": "192.168.2.102",
                "PrinterPort": 9100,
                "Output": "tspl",
                "SaveJobs": false,
                "Tspl": { "Density": 8 }
              }
            }
            """;
        File.WriteAllText(_path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        SettingsFile.Update(_path, new PrinterSettings("192.168.2.50", 9101, "Shop Label"));

        var root = JsonNode.Parse(File.ReadAllText(_path))!;
        Assert.Equal("192.168.2.50", (string?)root["Bridge"]!["PrinterHost"]);
        Assert.Equal(9101, (int)root["Bridge"]!["PrinterPort"]!);
        Assert.Equal("Shop Label", (string?)root["Bridge"]!["PrinterName"]);
        Assert.Equal("tspl", (string?)root["Bridge"]!["Output"]);
        Assert.False((bool)root["Bridge"]!["SaveJobs"]!);
        Assert.Equal(8, (int)root["Bridge"]!["Tspl"]!["Density"]!);
        Assert.Equal("Information", (string?)root["Logging"]!["LogLevel"]!["Default"]);
        Assert.Equal(new PrinterSettings("192.168.2.50", 9101, "Shop Label"), SettingsFile.Read(_path));
        Assert.Equal("tspl", SettingsFile.OutputMode(_path));
    }

    [Fact]
    public void Read_FileWithBom_Works()
    {
        File.WriteAllText(_path, """{"Bridge":{"PrinterHost":"10.0.0.9"}}""", new UTF8Encoding(true));

        Assert.Equal("10.0.0.9", SettingsFile.Read(_path)!.Host);
    }

    [Fact]
    public void Read_MissingFile_ReturnsNull()
    {
        Assert.Null(SettingsFile.Read(_path));
        Assert.Null(SettingsFile.OutputMode(_path));
    }

    [Fact]
    public void Read_MissingKeys_Defaults()
    {
        File.WriteAllText(_path, """{"Bridge":{}}""");

        Assert.Equal(new PrinterSettings("192.168.1.100", 9100, "LabelBridge Printer"), SettingsFile.Read(_path));
    }

    [Fact]
    public void Update_MissingFile_Creates()
    {
        SettingsFile.Update(_path, new PrinterSettings("192.168.2.102", 9100, "LabelBridge Printer"));

        Assert.Equal(new PrinterSettings("192.168.2.102", 9100, "LabelBridge Printer"), SettingsFile.Read(_path));
        Assert.NotEqual(0xEF, File.ReadAllBytes(_path)[0]);
    }
}
