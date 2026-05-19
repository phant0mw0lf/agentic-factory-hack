using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Represents a spare part in the Cosmos DB "PartsInventory" container.
/// Partition key: category
/// </summary>
public sealed class Part
{
  [JsonPropertyName("id")]
  [JsonProperty("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("partNumber")]
  [JsonProperty("partNumber")]
  public string PartNumber { get; set; } = string.Empty;

  [JsonPropertyName("name")]
  [JsonProperty("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("category")]
  [JsonProperty("category")]
  public string Category { get; set; } = string.Empty;  // partition key

  [JsonPropertyName("description")]
  [JsonProperty("description")]
  public string Description { get; set; } = string.Empty;

  [JsonPropertyName("quantityInStock")]
  [JsonProperty("quantityInStock")]
  public int QuantityInStock { get; set; }

  [JsonPropertyName("unitCost")]
  [JsonProperty("unitCost")]
  public decimal UnitCost { get; set; }

  [JsonPropertyName("supplier")]
  [JsonProperty("supplier")]
  public string Supplier { get; set; } = string.Empty;

  [JsonPropertyName("leadTimeDays")]
  [JsonProperty("leadTimeDays")]
  public int LeadTimeDays { get; set; }

  [JsonPropertyName("compatibleMachines")]
  [JsonProperty("compatibleMachines")]
  public List<string> CompatibleMachines { get; set; } = [];
}
