using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

/// <summary>
/// Represents a maintenance technician stored in the Cosmos DB "Technicians" container.
/// Partition key: department
/// </summary>
public sealed class Technician
{
  [JsonPropertyName("id")]
  [JsonProperty("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("name")]
  [JsonProperty("name")]
  public string Name { get; set; } = string.Empty;

  [JsonPropertyName("department")]
  [JsonProperty("department")]
  public string Department { get; set; } = string.Empty;  // partition key

  [JsonPropertyName("employeeId")]
  [JsonProperty("employeeId")]
  public string EmployeeId { get; set; } = string.Empty;

  [JsonPropertyName("skills")]
  [JsonProperty("skills")]
  public List<string> Skills { get; set; } = [];

  [JsonPropertyName("certifications")]
  [JsonProperty("certifications")]
  public List<string> Certifications { get; set; } = [];

  [JsonPropertyName("available")]
  [JsonProperty("available")]
  public bool Available { get; set; } = true;

  [JsonPropertyName("shift")]
  [JsonProperty("shift")]
  public string Shift { get; set; } = string.Empty;  // e.g. "morning" | "afternoon" | "night"

  [JsonPropertyName("experienceYears")]
  [JsonProperty("experienceYears")]
  public int ExperienceYears { get; set; }

  [JsonPropertyName("contactEmail")]
  [JsonProperty("contactEmail")]
  public string ContactEmail { get; set; } = string.Empty;
}
