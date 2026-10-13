using System.Text.Json.Serialization;
using AssettoServer.Shared.Network.Http.Responses;

namespace AssettoServer.Network.Http;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(InfoResponse))]
[JsonSerializable(typeof(DetailResponse))]
[JsonSerializable(typeof(EntryListResponse))]
// A rota da liga: ver LigaController.
[JsonSerializable(typeof(AoVivoResponse))]
internal partial class JsonSourceGenerationContext : JsonSerializerContext;
