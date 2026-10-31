Smart Travel Planning & Booking Assistant ✈️🏨
An enterprise multi-agent workflow system built with ASP.NET Core and the Microsoft Agent Framework. It coordinates destination research, itinerary option generation via domain tools, and financial auditing across specialized agent personas.

🌟 Architecture & Key Features
Sequential Agent Pipeline (AgentWorkflowBuilder.BuildSequential): Hand-offs between specialized agents are handled automatically, preserving message history and context between stages.
Separation of Concerns:
Researcher: Focuses exclusively on location insights and travel tips[cite: 1].
Planner: Focuses on logistics, using domain tools to look up pricing[cite: 1].
Auditor: Focuses on financial verification and policy compliance[cite: 1].
Dependency-Injected Tools (WithAITool): The planner agent invokes ITravelBookingService directly from the ASP.NET Core DI container[cite: 1].
Isolated Failure Domains: If the financial check flags an issue, previous research steps do not need to be re-run[cite: 1].

🏗️ Pipeline Flow
User Request ──► [Researcher Agent] ──► [Planner Agent + Tools] ──► [Auditor Agent] ──► Final Itinerary
🚀 Getting Started
Prerequisites
.NET 8.0 SDK or higher
Azure OpenAI or OpenAI API deployment[cite: 1].
Package Dependencies
PowerShell
dotnet add package Microsoft.Agents.AI --prerelease
dotnet add package Microsoft.Agents.AI.Hosting --prerelease
dotnet add package Microsoft.Agents.AI.Workflows --prerelease
dotnet add package Microsoft.Extensions.AI.OpenAI

Running the Service
Configure your endpoint in appsettings.json:
JSON
{
  "AzureOpenAI": {
    "Endpoint": "[https://your-resource.openai.azure.com/](https://your-resource.openai.azure.com/)",
    "Deployment": "gpt-4o"
  }
}

Start the ASP.NET Core server:
PowerShell
dotnet run

Test the workflow endpoint:
PowerShell
Invoke-RestMethod -Uri "http://localhost:5234/api/travel/plan" -Method POST -ContentType "application/json" -Body '{"destination": "Tokyo", "maxBudget": 1200, "days": 4}'


🛡️ Enterprise Financial & Compliance Approval Workflows
To eliminate "blind trust" in AI-generated travel plans, this application implements a database-backed Financial & Compliance Approval Workflow. Instead of relying purely on an agent's internal assumptions, trip budgets and parameters are cross-checked against hard corporate policy rules via Dependency Injection.

🏗️ Compliance Architecture & Workflow
Policy Service Layer (ICompliancePolicyService): Abstracted service interface implemented via SQLCompliancePolicyService to query internal company rules, tier-based destination allowances, and spending caps.
Compliance Auditor Agent: Specialized agent (compliance-auditor) equipped with a dynamic tool (check_company_policy) that evaluates the generated itinerary cost against live database records before issuing a verdict.
Automated Review & Dead-Letter Routing: If a trip request exceeds corporate thresholds or violates policy rules, the system flags the payload and routes it to a secure management review queue.
💻 Service Registration Example (Program.cs)
C#
// Register database-backed compliance policy services
builder.Services.AddSingleton<ICompliancePolicyService, SQLCompliancePolicyService>();

// Configure specialized compliance auditing agent with DI tool binding
builder.AddAIAgent(
    "compliance-auditor",
    instructions: "You are a corporate compliance auditor. You MUST use the check_company_policy tool to evaluate trip details against internal database rules.")
    .WithAITool(sp =>
    {
        var policyService = sp.GetRequiredService<ICompliancePolicyService>();
        return AIFunctionFactory.Create(
            async (string destination, decimal requestedBudget) => 
                await policyService.EvaluatePolicyAsync(destination, requestedBudget),
            "check_company_policy",
            "Cross-references trip budget and destination against internal company database compliance policies.");
    });
