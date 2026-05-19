using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlanner;
using RepairPlanner.Models;
using RepairPlanner.Services;

// ---------------------------------------------------------------------------
// 1. Read required environment variables
//    Set these before running:
//      export AZURE_AI_PROJECT_ENDPOINT="https://<your-project>.api.azureml.ms"
//      export MODEL_DEPLOYMENT_NAME="gpt-4o"
//      export COSMOS_ENDPOINT="https://<your-account>.documents.azure.com:443/"
//      export COSMOS_KEY="<your-key>"
//      export COSMOS_DATABASE_NAME="factory-hack"
// ---------------------------------------------------------------------------
static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Missing required environment variable: {name}");

var projectEndpoint = RequireEnv("AZURE_AI_PROJECT_ENDPOINT");
var modelDeployment = RequireEnv("MODEL_DEPLOYMENT_NAME");
var cosmosEndpoint = RequireEnv("COSMOS_ENDPOINT");
var cosmosKey = RequireEnv("COSMOS_KEY");
var cosmosDatabaseName = RequireEnv("COSMOS_DATABASE_NAME");

// ---------------------------------------------------------------------------
// 2. Set up logging + dependency injection
//    ServiceCollection is like Python's dependency injection container.
//    await using disposes everything automatically when the block exits
//    (like Python's "async with").
// ---------------------------------------------------------------------------
var services = new ServiceCollection();

services.AddLogging(builder =>
    builder.AddConsole().SetMinimumLevel(LogLevel.Information));

services.AddSingleton<IFaultMappingService, FaultMappingService>();

services.AddSingleton(sp =>
    new CosmosDbService(
        cosmosEndpoint,
        cosmosKey,
        cosmosDatabaseName,
        sp.GetRequiredService<ILogger<CosmosDbService>>()));

services.AddSingleton(sp =>
    new RepairPlannerAgent(
        projectEndpoint,
        modelDeployment,
        sp.GetRequiredService<CosmosDbService>(),
        sp.GetRequiredService<IFaultMappingService>(),
        sp.GetRequiredService<ILogger<RepairPlannerAgent>>()));

// await using — like Python's "async with": disposes the provider when done.
await using var provider = services.BuildServiceProvider();

var logger = provider.GetRequiredService<ILogger<Program>>();
var agent = provider.GetRequiredService<RepairPlannerAgent>();

// ---------------------------------------------------------------------------
// 3. Register the Foundry Prompt Agent (idempotent — safe to run every time)
// ---------------------------------------------------------------------------
await agent.EnsureAgentVersionAsync();

// ---------------------------------------------------------------------------
// 4. Define a sample diagnosed fault (in production this comes from the
//    Fault Diagnosis Agent upstream in the pipeline)
// ---------------------------------------------------------------------------
var sampleFault = new DiagnosedFault
{
  Id = Guid.NewGuid().ToString(),
  MachineId = "MACHINE-TC-001",
  MachineName = "Tire Curing Press #1",
  FaultType = "curing_temperature_excessive",
  Severity = "high",
  Confidence = 0.92,
  Description = "Curing press temperature exceeded threshold by 18°C for 12 minutes. " +
                       "Possible heater element failure or thermocouple drift.",
  RecommendedAction = "Inspect heater elements and thermocouple sensors. " +
                        "Replace faulty components and recalibrate temperature control system.",
  DiagnosedAt = DateTime.UtcNow,
};

logger.LogInformation("Sample fault: {FaultType} on {MachineName} (severity: {Severity})",
    sampleFault.FaultType, sampleFault.MachineName, sampleFault.Severity);

// ---------------------------------------------------------------------------
// 5. Run the repair planning workflow
// ---------------------------------------------------------------------------
try
{
  var workOrder = await agent.PlanAndCreateWorkOrderAsync(sampleFault);

  Console.WriteLine();
  Console.WriteLine("=== Work Order Created ===");
  Console.WriteLine($"  Number    : {workOrder.WorkOrderNumber}");
  Console.WriteLine($"  Title     : {workOrder.Title}");
  Console.WriteLine($"  Machine   : {workOrder.MachineId}");
  Console.WriteLine($"  Priority  : {workOrder.Priority}");
  Console.WriteLine($"  Type      : {workOrder.Type}");
  Console.WriteLine($"  Assigned  : {workOrder.AssignedTo ?? "(unassigned)"}");
  Console.WriteLine($"  Est. Time : {workOrder.EstimatedDuration} minutes");
  Console.WriteLine($"  Tasks     : {workOrder.Tasks.Count}");
  Console.WriteLine($"  Parts     : {workOrder.PartsUsed.Count}");
  Console.WriteLine($"  Status    : {workOrder.Status}");
  Console.WriteLine($"  Cosmos ID : {workOrder.Id}");

  if (workOrder.Tasks.Count > 0)
  {
    Console.WriteLine();
    Console.WriteLine("  Task breakdown:");
    foreach (var task in workOrder.Tasks)
      Console.WriteLine($"    {task.Sequence}. [{task.EstimatedDurationMinutes}m] {task.Title}");
  }
}
catch (Exception ex)
{
  logger.LogError(ex, "Repair planning failed.");
  return 1;
}

return 0;

