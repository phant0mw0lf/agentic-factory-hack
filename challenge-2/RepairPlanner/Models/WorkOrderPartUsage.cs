using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Records a specific part and quantity needed to complete a WorkOrder.
/// Maps to entries in the partsUsed array inside a WorkOrder document.
/// </summary>
public sealed class WorkOrderPartUsage
{
  [JsonPropertyName("partId")]
  [JsonProperty("partId")]
  public string PartId { get; set; } = string.Empty;

  [JsonPropertyName("partNumber")]
  [JsonProperty("partNumber")]
  public string PartNumber { get; set; } = string.Empty;

  [JsonPropertyName("quantity")]
  [JsonProperty("quantity")]
  public int Quantity { get; set; } = 1;
}
