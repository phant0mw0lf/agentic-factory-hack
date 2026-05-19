using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;
using RepairPlanner.Services;

namespace RepairPlanner;

/// <summary>
/// Orchestrates the full repair-planning workflow:
///   1. Register a Foundry Prompt Agent (idempotent on every startup)
///   2. Look up required skills + parts from FaultMappingService
///   3. Query Cosmos DB for available technicians and in-stock parts
///   4. Invoke the Foundry Agent to generate a structured WorkOrder
///   5. Persist the WorkOrder back to Cosmos DB
/// </summary>
public sealed class RepairPlannerAgent
{
  private const string AgentName = "RepairPlannerAgent";

  // System prompt sent to the Foundry Prompt Agent.
  // Keep it concise — smaller models do better with focused instructions.
  private const string AgentInstructions = """
        You are a Repair Planner Agent for tire manufacturing equipment.
        Generate a repair plan with tasks, timeline, and resource allocation.
        Return the response as valid JSON matching the WorkOrder schema.

        Output JSON with these fields:
        - workOrderNumber, machineId, title, description
        - type: "corrective" | "preventive" | "emergency"
        - priority: "critical" | "high" | "medium" | "low"
        - status, assignedTo (technician id or null), notes
        - estimatedDuration: integer (minutes, e.g. 90 — NOT "90 minutes")
        - partsUsed: [{ partId, partNumber, quantity }]
        - tasks: [{ sequence, title, description, estimatedDurationMinutes (integer), requiredSkills, safetyNotes }]

        IMPORTANT: All duration fields must be integers representing minutes (e.g. 90), not strings.

        Rules:
        - Assign the most qualified available technician (highest skill overlap + experience)
        - Include only relevant parts; empty array if none needed
        - Tasks must be sequentially ordered and fully actionable
        - Return ONLY the JSON object — no markdown fences, no extra text
        """;

  // JsonSerializerOptions used to parse the LLM's JSON response.
  // AllowReadingFromString handles cases where the model returns "90" instead of 90.
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
  };

  private readonly AIProjectClient _projectClient;
  private readonly CosmosDbService _cosmosDb;
  private readonly IFaultMappingService _faultMapping;
  private readonly string _modelDeploymentName;
  private readonly ILogger<RepairPlannerAgent> _logger;

  // Primary constructor — C# promotes these parameters to private fields automatically.
  // Equivalent to Python's __init__ assigning self.x = x for each parameter.
  public RepairPlannerAgent(
      string projectEndpoint,
      string modelDeploymentName,
      CosmosDbService cosmosDb,
      IFaultMappingService faultMapping,
      ILogger<RepairPlannerAgent> logger)
  {
    // DefaultAzureCredential tries managed identity, env vars, VS Code login, etc.
    _projectClient = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential());
    _modelDeploymentName = modelDeploymentName;
    _cosmosDb = cosmosDb;
    _faultMapping = faultMapping;
    _logger = logger;
  }

  // -------------------------------------------------------------------------
  // Agent registration
  // -------------------------------------------------------------------------

  /// <summary>
  /// Registers (or updates) the Prompt Agent definition in Azure AI Foundry.
  /// Safe to call on every startup — CreateAgentVersionAsync is idempotent.
  /// </summary>
  public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
  {
    _logger.LogInformation("Registering Foundry agent '{AgentName}'...", AgentName);

    var definition = new PromptAgentDefinition(model: _modelDeploymentName)
    {
      Instructions = AgentInstructions
    };

    await _projectClient.Agents.CreateAgentVersionAsync(
        AgentName,
        new AgentVersionCreationOptions(definition),
        ct);

    _logger.LogInformation("Agent '{AgentName}' registered successfully.", AgentName);
  }

  // -------------------------------------------------------------------------
  // Main workflow
  // -------------------------------------------------------------------------

  /// <summary>
  /// Runs the full repair planning workflow for a diagnosed fault and
  /// returns the persisted WorkOrder.
  /// </summary>
  public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(
      DiagnosedFault fault,
      CancellationToken ct = default)
  {
    _logger.LogInformation(
        "Planning repair for fault '{FaultType}' on machine '{MachineId}'",
        fault.FaultType, fault.MachineId);

    // Step 1: Determine required skills and part numbers from static mappings.
    var requiredSkills = _faultMapping.GetRequiredSkills(fault.FaultType);
    var requiredPartNumbers = _faultMapping.GetRequiredParts(fault.FaultType);

    _logger.LogInformation(
        "Required skills: [{Skills}]  |  Required parts: [{Parts}]",
        string.Join(", ", requiredSkills),
        string.Join(", ", requiredPartNumbers));

    // Step 2: Fetch matching data from Cosmos DB in parallel.
    // Task.WhenAll runs both queries concurrently — like asyncio.gather() in Python.
    var (technicians, parts) = await FetchResourcesAsync(requiredSkills, requiredPartNumbers, ct);

    // Step 3: Build the user prompt that gives the LLM full context.
    var userPrompt = BuildPrompt(fault, technicians, parts, requiredSkills);

    // Step 4: Invoke the Foundry Agent.
    var workOrder = await InvokeAgentAsync(userPrompt, fault, ct);

    // Step 5: Persist to Cosmos DB.
    var saved = await _cosmosDb.CreateWorkOrderAsync(workOrder, ct);

    _logger.LogInformation(
        "Work order '{WorkOrderNumber}' created successfully (id: {Id})",
        saved.WorkOrderNumber, saved.Id);

    return saved;
  }

  // -------------------------------------------------------------------------
  // Private helpers
  // -------------------------------------------------------------------------

  private async Task<(List<Models.Technician> technicians, List<Models.Part> parts)>
      FetchResourcesAsync(
          IReadOnlyList<string> requiredSkills,
          IReadOnlyList<string> requiredPartNumbers,
          CancellationToken ct)
  {
    var techTask = _cosmosDb.GetAvailableTechniciansBySkillsAsync(requiredSkills, ct);
    var partsTask = _cosmosDb.GetPartsByPartNumbersAsync(requiredPartNumbers, ct);

    await Task.WhenAll(techTask, partsTask);

    return (techTask.Result, partsTask.Result);
  }

  private static string BuildPrompt(
      DiagnosedFault fault,
      List<Models.Technician> technicians,
      List<Models.Part> parts,
      IReadOnlyList<string> requiredSkills)
  {
    // Serialize supporting data as JSON so the LLM gets structured context.
    var techJson = JsonSerializer.Serialize(
        technicians.Select(t => new
        {
          t.Id,
          t.Name,
          t.Skills,
          t.ExperienceYears,
          t.Available,
          t.Shift
        }),
        JsonOptions);

    var partsJson = JsonSerializer.Serialize(
        parts.Select(p => new
        {
          p.Id,
          p.PartNumber,
          p.Name,
          p.QuantityInStock
        }),
        JsonOptions);

    return $"""
            Diagnosed Fault:
            - Machine ID   : {fault.MachineId}
            - Machine Name : {fault.MachineName}
            - Fault Type   : {fault.FaultType}
            - Severity     : {fault.Severity}
            - Description  : {fault.Description}
            - Recommended  : {fault.RecommendedAction}
            - Diagnosed At : {fault.DiagnosedAt:O}

            Required Skills: {string.Join(", ", requiredSkills)}

            Available Technicians (JSON):
            {techJson}

            Available Parts (JSON):
            {partsJson}

            Generate a complete WorkOrder JSON for this fault.
            """;
  }

  private async Task<WorkOrder> InvokeAgentAsync(
      string userPrompt,
      DiagnosedFault fault,
      CancellationToken ct)
  {
    _logger.LogInformation("Invoking Foundry agent '{AgentName}'...", AgentName);

    // GetAIAgent retrieves the registered agent by name.
    // RunAsync sends the user prompt and returns the agent's text response.
    // thread: null means a fresh conversation thread each time.
    var agent = _projectClient.GetAIAgent(name: AgentName);
    var response = await agent.RunAsync(userPrompt, thread: null, options: null, cancellationToken: ct);

    var rawJson = response.Text ?? string.Empty;
    _logger.LogDebug("Agent raw response:\n{Response}", rawJson);

    return ParseWorkOrder(rawJson, fault);
  }

  private WorkOrder ParseWorkOrder(string rawJson, DiagnosedFault fault)
  {
    // Strip markdown code fences if the model included them despite instructions.
    var json = rawJson.Trim();
    if (json.StartsWith("```"))
    {
      var start = json.IndexOf('\n') + 1;
      var end = json.LastIndexOf("```");
      if (end > start)
        json = json[start..end].Trim();
    }

    WorkOrder? workOrder = null;

    try
    {
      workOrder = JsonSerializer.Deserialize<WorkOrder>(json, JsonOptions);
    }
    catch (JsonException ex)
    {
      _logger.LogError(ex, "Failed to deserialize WorkOrder JSON from agent response.");
    }

    // ??= means "assign only if currently null" (like Python's: x = x or default).
    workOrder ??= new WorkOrder();

    // Apply defaults and link back to the source fault.
    workOrder.Id = string.IsNullOrEmpty(workOrder.Id) ? Guid.NewGuid().ToString() : workOrder.Id;
    workOrder.DiagnosedFaultId = fault.Id;
    workOrder.FaultType = fault.FaultType;
    workOrder.MachineId = string.IsNullOrEmpty(workOrder.MachineId) ? fault.MachineId : workOrder.MachineId;
    workOrder.Status = string.IsNullOrEmpty(workOrder.Status) ? "open" : workOrder.Status;
    workOrder.Priority ??= fault.Severity;   // fall back to fault severity if agent omitted it
    workOrder.CreatedAt = DateTime.UtcNow;
    workOrder.UpdatedAt = DateTime.UtcNow;

    if (string.IsNullOrEmpty(workOrder.WorkOrderNumber))
      workOrder.WorkOrderNumber = $"WO-{DateTime.UtcNow:yyyyMMddHHmmss}";

    return workOrder;
  }
}
