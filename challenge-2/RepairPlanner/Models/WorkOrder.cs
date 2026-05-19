using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// The primary output of the Repair Planner Agent.
/// Saved to the Cosmos DB "WorkOrders" container (partition key: status).
/// </summary>
public sealed class WorkOrder
{
  [JsonPropertyName("id")]
  [JsonProperty("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("workOrderNumber")]
  [JsonProperty("workOrderNumber")]
  public string WorkOrderNumber { get; set; } = string.Empty;

  [JsonPropertyName("machineId")]
  [JsonProperty("machineId")]
  public string MachineId { get; set; } = string.Empty;

  [JsonPropertyName("title")]
  [JsonProperty("title")]
  public string Title { get; set; } = string.Empty;

  [JsonPropertyName("description")]
  [JsonProperty("description")]
  public string Description { get; set; } = string.Empty;

  // "corrective" | "preventive" | "emergency"
  [JsonPropertyName("type")]
  [JsonProperty("type")]
  public string Type { get; set; } = "corrective";

  // "critical" | "high" | "medium" | "low"
  [JsonPropertyName("priority")]
  [JsonProperty("priority")]
  public string? Priority { get; set; }

  // "open" | "in-progress" | "completed" | "cancelled"  — also the Cosmos partition key
  [JsonPropertyName("status")]
  [JsonProperty("status")]
  public string Status { get; set; } = "open";

  // Technician id, or null if unassigned
  [JsonPropertyName("assignedTo")]
  [JsonProperty("assignedTo")]
  public string? AssignedTo { get; set; }

  // Total estimated duration in minutes (integer, not a string)
  [JsonPropertyName("estimatedDuration")]
  [JsonProperty("estimatedDuration")]
  public int EstimatedDuration { get; set; }

  [JsonPropertyName("notes")]
  [JsonProperty("notes")]
  public string Notes { get; set; } = string.Empty;

  [JsonPropertyName("faultType")]
  [JsonProperty("faultType")]
  public string FaultType { get; set; } = string.Empty;

  [JsonPropertyName("diagnosedFaultId")]
  [JsonProperty("diagnosedFaultId")]
  public string DiagnosedFaultId { get; set; } = string.Empty;

  [JsonPropertyName("partsUsed")]
  [JsonProperty("partsUsed")]
  public List<WorkOrderPartUsage> PartsUsed { get; set; } = [];

  [JsonPropertyName("tasks")]
  [JsonProperty("tasks")]
  public List<RepairTask> Tasks { get; set; } = [];

  [JsonPropertyName("createdAt")]
  [JsonProperty("createdAt")]
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

  [JsonPropertyName("updatedAt")]
  [JsonProperty("updatedAt")]
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
