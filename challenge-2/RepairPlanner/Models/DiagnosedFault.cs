using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Represents a fault diagnosed by the upstream Fault Diagnosis Agent.
/// This is the input to the Repair Planner Agent.
/// </summary>
public sealed class DiagnosedFault
{
  // Both [JsonPropertyName] (System.Text.Json) and [JsonProperty] (Newtonsoft.Json)
  // are needed: STJ is used when deserializing LLM responses,
  // Newtonsoft is used by the Cosmos DB SDK.

  [JsonPropertyName("id")]
  [JsonProperty("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("machineId")]
  [JsonProperty("machineId")]
  public string MachineId { get; set; } = string.Empty;

  [JsonPropertyName("machineName")]
  [JsonProperty("machineName")]
  public string MachineName { get; set; } = string.Empty;

  [JsonPropertyName("faultType")]
  [JsonProperty("faultType")]
  public string FaultType { get; set; } = string.Empty;

  [JsonPropertyName("severity")]
  [JsonProperty("severity")]
  public string Severity { get; set; } = string.Empty;  // "critical" | "high" | "medium" | "low"

  [JsonPropertyName("confidence")]
  [JsonProperty("confidence")]
  public double Confidence { get; set; }

  [JsonPropertyName("description")]
  [JsonProperty("description")]
  public string Description { get; set; } = string.Empty;

  [JsonPropertyName("recommendedAction")]
  [JsonProperty("recommendedAction")]
  public string RecommendedAction { get; set; } = string.Empty;

  [JsonPropertyName("diagnosedAt")]
  [JsonProperty("diagnosedAt")]
  public DateTime DiagnosedAt { get; set; } = DateTime.UtcNow;
}
