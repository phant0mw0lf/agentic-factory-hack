using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

/// <summary>
/// Encapsulates all Cosmos DB data access for the Repair Planner Agent.
///
/// Containers and their partition keys:
///   Technicians    → /department
///   PartsInventory → /category
///   WorkOrders     → /status
/// </summary>
public sealed class CosmosDbService
{
  // Primary constructor — parameters are promoted to fields automatically
  // (like Python's __init__ with self.x = x, but more concise).
  private readonly CosmosClient _client;
  private readonly string _databaseName;
  private readonly ILogger<CosmosDbService> _logger;

  public CosmosDbService(string endpoint, string key, string databaseName, ILogger<CosmosDbService> logger)
  {
    // CosmosClient is thread-safe and should be shared for the lifetime of the app.
    _client = new CosmosClient(endpoint, key, new CosmosClientOptions
    {
      SerializerOptions = new CosmosSerializationOptions
      {
        // Use camelCase to match the JSON property names in Cosmos documents.
        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
      }
    });
    _databaseName = databaseName;
    _logger = logger;
  }

  // -------------------------------------------------------------------------
  // Technicians
  // -------------------------------------------------------------------------

  /// <summary>
  /// Returns all available technicians who have at least one of the required skills.
  /// Uses a cross-partition query (no partition key filter) because technicians
  /// span multiple departments.
  /// </summary>
  public async Task<List<Technician>> GetAvailableTechniciansBySkillsAsync(
      IReadOnlyList<string> requiredSkills,
      CancellationToken ct = default)
  {
    var container = _client.GetContainer(_databaseName, "Technicians");

    // Build an IN-list of skills for the SQL WHERE clause.
    // Parameters are used (never string interpolation) to avoid injection risks.
    var skillParams = requiredSkills
        .Select((s, i) => $"@skill{i}")
        .ToList();

    var query = $"""
            SELECT * FROM c
            WHERE c.available = true
              AND EXISTS (
                SELECT VALUE skill FROM skill IN c.skills
                WHERE skill IN ({string.Join(", ", skillParams)})
              )
            """;

    var queryDef = new QueryDefinition(query);
    for (int i = 0; i < requiredSkills.Count; i++)
      queryDef = queryDef.WithParameter($"@skill{i}", requiredSkills[i]);

    return await ExecuteQueryAsync<Technician>(container, queryDef, "technicians", ct);
  }

  // -------------------------------------------------------------------------
  // Parts inventory
  // -------------------------------------------------------------------------

  /// <summary>
  /// Fetches parts from the PartsInventory container matching the given part numbers.
  /// Returns only parts that are in stock (quantityInStock > 0).
  /// </summary>
  public async Task<List<Part>> GetPartsByPartNumbersAsync(
      IReadOnlyList<string> partNumbers,
      CancellationToken ct = default)
  {
    if (partNumbers.Count == 0)
      return [];

    var container = _client.GetContainer(_databaseName, "PartsInventory");

    var partParams = partNumbers
        .Select((p, i) => $"@part{i}")
        .ToList();

    var query = $"""
            SELECT * FROM c
            WHERE c.partNumber IN ({string.Join(", ", partParams)})
              AND c.quantityInStock > 0
            """;

    var queryDef = new QueryDefinition(query);
    for (int i = 0; i < partNumbers.Count; i++)
      queryDef = queryDef.WithParameter($"@part{i}", partNumbers[i]);

    return await ExecuteQueryAsync<Part>(container, queryDef, "parts", ct);
  }

  // -------------------------------------------------------------------------
  // Work orders
  // -------------------------------------------------------------------------

  /// <summary>
  /// Persists a new WorkOrder document to Cosmos DB.
  /// The partition key is the work order's status (e.g. "open").
  /// </summary>
  public async Task<WorkOrder> CreateWorkOrderAsync(WorkOrder workOrder, CancellationToken ct = default)
  {
    // Ensure the document has a unique id before saving.
    if (string.IsNullOrEmpty(workOrder.Id))
      workOrder.Id = Guid.NewGuid().ToString();

    var container = _client.GetContainer(_databaseName, "WorkOrders");

    try
    {
      _logger.LogInformation(
          "Creating work order {WorkOrderNumber} for machine {MachineId}",
          workOrder.WorkOrderNumber, workOrder.MachineId);

      // CreateItemAsync is idempotent when the id is unique.
      // The partition key must match the container's partition key path (/status).
      var response = await container.CreateItemAsync(
          workOrder,
          new PartitionKey(workOrder.Status),
          cancellationToken: ct);

      _logger.LogInformation(
          "Work order {WorkOrderNumber} saved (RU cost: {RU})",
          workOrder.WorkOrderNumber, response.RequestCharge);

      return response.Resource;
    }
    catch (CosmosException ex)
    {
      _logger.LogError(ex,
          "Failed to save work order {WorkOrderNumber}: {StatusCode}",
          workOrder.WorkOrderNumber, ex.StatusCode);
      throw;
    }
  }

  // -------------------------------------------------------------------------
  // Internal helpers
  // -------------------------------------------------------------------------

  /// <summary>
  /// Iterates all pages of a Cosmos DB query and collects the results.
  /// Logs a warning and returns an empty list on query failure rather than crashing.
  /// </summary>
  private async Task<List<T>> ExecuteQueryAsync<T>(
      Container container,
      QueryDefinition queryDef,
      string entityLabel,
      CancellationToken ct)
  {
    var results = new List<T>();

    try
    {
      // GetItemQueryIterator returns results in pages; we loop until exhausted.
      using var feed = container.GetItemQueryIterator<T>(queryDef);

      while (feed.HasMoreResults)
      {
        var page = await feed.ReadNextAsync(ct);
        results.AddRange(page);
      }

      _logger.LogInformation("Fetched {Count} {Entity} from Cosmos DB", results.Count, entityLabel);
    }
    catch (CosmosException ex)
    {
      _logger.LogError(ex,
          "Cosmos DB query for {Entity} failed: {StatusCode}", entityLabel, ex.StatusCode);
      // Return empty rather than propagating — the agent can still run with reduced context.
    }

    return results;
  }
}
