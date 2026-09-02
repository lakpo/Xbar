using System.Text.Json;
using System.Text.Json.Serialization;
using Xbar.ExplorerIntegration;
using Xbar.Shortcuts;
using Xbar.Update;

namespace Xbar.Core;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ShortcutList))]
[JsonSerializable(typeof(FolderHistoryFile))]
[JsonSerializable(typeof(GitHubRelease))]
public partial class JsonContext : JsonSerializerContext;
