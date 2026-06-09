using YuoCore.Config;
using System.Text.Json;

namespace YuoCore.Config.Tests;

public sealed class YuoStartConfigTests
{
    [Fact]
    public async Task LoaderParsesValidStartConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"yuocore-start-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
        {
          "nodeId": "dev-node",
          "processId": "game-1",
          "processKind": "Game",
          "innerEndpoint": "tcp://127.0.0.1:7001",
          "metadata": { "zone": "1" },
          "services": [
            { "name": "gateway", "kind": "Gateway", "endpoint": "ws://127.0.0.1:8080/ws", "metadata": { "public": "true" } }
          ],
          "fibers": [
            { "id": 1, "name": "GameShard-0", "kind": "Game", "shardId": 0, "setWorldAsDefault": true, "metadata": { "map": "1001" } },
            { "id": 2, "name": "Location", "kind": "Location", "shardId": 0 }
          ]
        }
        """);

        try
        {
            var config = await YuoStartConfigLoader.LoadAsync(path);
            var validation = YuoStartConfigLoader.Validate(config);

            Assert.True(validation.IsSuccess);
            Assert.Equal("dev-node", config.NodeId);
            Assert.Equal("Game", config.ProcessKind);
            Assert.Equal("1", config.Metadata["zone"]);
            Assert.Equal("true", config.Services[0].Metadata["public"]);
            Assert.Equal("1001", config.Fibers[0].Metadata["map"]);
            Assert.Equal(2, config.Fibers.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ValidateRejectsDuplicateFiberId()
    {
        var config = new YuoStartConfig
        {
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "a" },
                new YuoFiberConfig { Id = 1, Name = "b" }
            ]
        };

        var result = YuoStartConfigLoader.Validate(config);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateRejectsDuplicateServiceName()
    {
        var config = new YuoStartConfig
        {
            Services =
            [
                new YuoServiceConfig { Name = "gateway", Kind = "Gateway" },
                new YuoServiceConfig { Name = "gateway", Kind = "Gateway" }
            ],
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "main" }
            ]
        };

        var result = YuoStartConfigLoader.Validate(config);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateRejectsInvalidMailboxBudget()
    {
        var config = new YuoStartConfig
        {
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "main", MaxMailboxMessagesPerTick = 0 }
            ]
        };

        var result = YuoStartConfigLoader.Validate(config);

        Assert.False(result.IsSuccess);
        Assert.Contains("max mailbox messages", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateRejectsNullConfig()
    {
        var result = YuoStartConfigLoader.Validate(null);

        Assert.False(result.IsSuccess);
        Assert.Contains("Start config", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateRejectsNullCollectionsAndItems()
    {
        var nullServices = new YuoStartConfig
        {
            Services = null!,
            Fibers = [new YuoFiberConfig { Id = 1, Name = "main" }]
        };
        var nullFiberItem = new YuoStartConfig
        {
            Fibers = [null!]
        };
        var nullServiceItem = new YuoStartConfig
        {
            Services = [null!],
            Fibers = [new YuoFiberConfig { Id = 1, Name = "main" }]
        };

        var servicesResult = YuoStartConfigLoader.Validate(nullServices);
        var fiberItemResult = YuoStartConfigLoader.Validate(nullFiberItem);
        var serviceItemResult = YuoStartConfigLoader.Validate(nullServiceItem);

        Assert.False(servicesResult.IsSuccess);
        Assert.Contains("Services collection", servicesResult.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(fiberItemResult.IsSuccess);
        Assert.Contains("Fiber config item", fiberItemResult.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(serviceItemResult.IsSuccess);
        Assert.Contains("Service config item", serviceItemResult.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateRejectsNullMetadataCollections()
    {
        var nullRootMetadata = new YuoStartConfig
        {
            Metadata = null!,
            Fibers = [new YuoFiberConfig { Id = 1, Name = "main" }]
        };
        var nullServiceMetadata = new YuoStartConfig
        {
            Services = [new YuoServiceConfig { Name = "gateway", Kind = "Gateway", Metadata = null! }],
            Fibers = [new YuoFiberConfig { Id = 1, Name = "main" }]
        };
        var nullFiberMetadata = new YuoStartConfig
        {
            Fibers = [new YuoFiberConfig { Id = 1, Name = "main", Metadata = null! }]
        };

        var rootResult = YuoStartConfigLoader.Validate(nullRootMetadata);
        var serviceResult = YuoStartConfigLoader.Validate(nullServiceMetadata);
        var fiberResult = YuoStartConfigLoader.Validate(nullFiberMetadata);

        Assert.False(rootResult.IsSuccess);
        Assert.Contains("Metadata collection", rootResult.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(serviceResult.IsSuccess);
        Assert.Contains("metadata collection", serviceResult.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(fiberResult.IsSuccess);
        Assert.Contains("metadata collection", fiberResult.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateRejectsInvalidMetadataEntries()
    {
        var emptyKey = new YuoStartConfig
        {
            Metadata = new Dictionary<string, string> { [""] = "1" },
            Fibers = [new YuoFiberConfig { Id = 1, Name = "main" }]
        };
        var nullValue = new YuoStartConfig
        {
            Fibers =
            [
                new YuoFiberConfig
                {
                    Id = 1,
                    Name = "main",
                    Metadata = new Dictionary<string, string> { ["zone"] = null! }
                }
            ]
        };

        var emptyKeyResult = YuoStartConfigLoader.Validate(emptyKey);
        var nullValueResult = YuoStartConfigLoader.Validate(nullValue);

        Assert.False(emptyKeyResult.IsSuccess);
        Assert.Contains("key cannot be empty", emptyKeyResult.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(nullValueResult.IsSuccess);
        Assert.Contains("value cannot be null", nullValueResult.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartConfigSchemaRequiresRuntimeRequiredFields()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindSchemaPath("startconfig.schema.json")));
        var required = document.RootElement
            .GetProperty("required")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("nodeId", required);
        Assert.Contains("processId", required);
        Assert.Contains("processKind", required);
        Assert.Contains("fibers", required);
    }

    [Fact]
    public void StartConfigSchemaRejectsEmptyMetadataPropertyNames()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindSchemaPath("startconfig.schema.json")));
        var rootProperties = document.RootElement.GetProperty("properties");

        AssertMetadataPropertyNamesHaveMinLength(rootProperties.GetProperty("metadata"));
        AssertMetadataPropertyNamesHaveMinLength(rootProperties
            .GetProperty("services")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("metadata"));
        AssertMetadataPropertyNamesHaveMinLength(rootProperties
            .GetProperty("fibers")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("metadata"));
    }

    [Fact]
    public void StartConfigSchemaCoversCurrentServiceAndFiberFields()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindSchemaPath("startconfig.schema.json")));
        var rootProperties = document.RootElement.GetProperty("properties");
        var serviceProperties = rootProperties
            .GetProperty("services")
            .GetProperty("items")
            .GetProperty("properties");
        var fiberProperties = rootProperties
            .GetProperty("fibers")
            .GetProperty("items")
            .GetProperty("properties");

        AssertSchemaPropertiesMatchDto<YuoStartConfig>(rootProperties, "root");
        AssertSchemaPropertiesMatchDto<YuoServiceConfig>(serviceProperties, "service");
        AssertSchemaPropertiesMatchDto<YuoFiberConfig>(fiberProperties, "fiber");
    }

    private static string FindSchemaPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "schemas", fileName);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot find schema file: {fileName}");
    }

    private static void AssertSchemaPropertiesMatchDto<T>(JsonElement schemaProperties, string scope)
    {
        var expected = typeof(T)
            .GetProperties()
            .Select(property => ToJsonPropertyName(property.Name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = schemaProperties
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);

        foreach (var property in expected)
        {
            Assert.True(schemaProperties.TryGetProperty(property, out _), $"missing {scope} schema property: {property}");
        }
    }

    private static string ToJsonPropertyName(string propertyName)
    {
        return string.IsNullOrEmpty(propertyName)
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
    }

    private static void AssertMetadataPropertyNamesHaveMinLength(JsonElement metadataSchema)
    {
        Assert.Equal(1, metadataSchema
            .GetProperty("propertyNames")
            .GetProperty("minLength")
            .GetInt32());
    }
}
