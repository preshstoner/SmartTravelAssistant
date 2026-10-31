using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Azure.Identity;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks; 


var builder = WebApplication.CreateBuilder(args);

// Enable CORS for local UI hosting
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// Register the underlying Chat Client (IChatClient)
builder.Services.AddSingleton<IChatClient>(sp =>
{
    // Replace with your Azure OpenAi / OpenAI endpoint configuration[cite: 1]
    var client = new AzureOpenAIClient(
        new Uri(builder.Configuration["AzureOpenAI:Endpoint"] ?? "https://your-openai-endpoint.openai.azure.com/"),
        new DefaultAzureCredential()).GetChatClient(builder.Configuration["AzureOpenAI: Deployment"] ?? "gpt-4o");

    return client.AsIChatClient();
});


// Register Domain Services for Tools[cite: 1]
builder.Services.AddSingleton<ITravelBookingService, TravelBookingService>();

// Register Individual Specialized Agents[cite: 1]
builder.AddAIAgent(
    "destination-researcher",
    instructions: "You are a travel research specialist. Gather city insights, best times to visit, local customs, and top attractions for the requested destination. Keep it concise. ");

//  Itinerary Planner Agent (Equipped with DI Tool)[cite: 1]
builder.AddAIAgent(
    "itinerary-planner",
    instructions: "You are an itinerary planning specialist. Create a daily breakdown including flight and hotel recommendations. You MUST use the search_travel_options tool to fetch estimates.")
    .WithAITool(sp =>
    {
        var bookingService = sp.GetRequiredService<ITravelBookingService>();
        return AIFunctionFactory.Create(
            async (string destination, decimal budget) => await bookingService.SearchFlightsAndHotelsAsync(destination, budget),
            "search_travel_options",
            "Search available flight and hotel package estimates for a given destination and budget.");
    });


builder.AddAIAgent(
    "budget-auditor",
    instructions: "You are a finanicial travel auditor. Compare the total planned cost against the user's budget. Return an APPROVED summary if within budget, or flag exact cost overruns if it exceeds the limit.");

// Build the Sequential Agent Workflow Pipeline (Registered with key "smart-travel-assistant")
builder.AddWorkflow("smart-travel-assistant", (sp, key) =>
{
    var researcher = sp.GetRequiredKeyedService<AIAgent>("destination-researcher");
    var planner = sp.GetRequiredKeyedService<AIAgent>("itinerary-planner");
    var auditor = sp.GetRequiredKeyedService<AIAgent>("budget-auditor");

    return AgentWorkflowBuilder.BuildSequential(key, [researcher, planner, auditor]);
});

var app = builder.Build();

// Optional: Automatically serve index.html when opening http://localhost:5234/
app.UseDefaultFiles();

// Enable serving HTML, CSS, and JS files from the wwwroot folder
app.UseStaticFiles();

// Your CORS policy (if needed)
app.UseCors("AllowAll");


// Minimal API Endpoint exposing the Multi-Agent Pipeline
app.MapPost("/api/travel/stream", async (
    [FromKeyedServices("smart-travel-assistant")] Workflow workflow,
    [FromBody] TravelRequest request, // Explicitly mark this as coming from the request body
    HttpContext context,
    CancellationToken ct) =>
{
    // Set SSE headers
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    try
    {
        string fullPrompt = $"Destination: {request.Destination}. Total Target Budget: ${request.MaxBudget}. Trip Duration: {request.Days} days.";

        var messages = new List<ChatMessage> { new(ChatRole.User, fullPrompt) };

        //Kick off workflow streaming execution
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, messages, cancellationToken: ct);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? lastExecutorId = null;

        // Watch real-time events as each agent executes in sequence
        await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
        {
            if (evt is AgentResponseUpdateEvent update)
            {
                if (update.Update?.Text != null)
                {
                    // Print agent name header when switching between Researcher, Planner, and Auditor
                    if (update.ExecutorId != lastExecutorId)
                    {
                        lastExecutorId = update.ExecutorId;
                        var agentHeader = $"\\n\\n[{lastExecutorId}]:\\n";
                        await context.Response.WriteAsync($"data: {agentHeader}\n\n", ct);
                    }

                    var sanitized = update.Update.Text.Replace("\r", "").Replace("\n", "\\n");
                    await context.Response.WriteAsync($"data: {sanitized}\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                }
            }
            else if (evt is WorkflowOutputEvent)
            {
                break;
            }
        }

    }
    catch (Exception ex)
    {
        // 🔍 This will print any hidden exception straight to your UI dashboard!
        var errorDetails = $"\\n\\n[BACKEND EXCEPTION]: {ex.Message.Replace("\n", " ")}\\n";
        await context.Response.WriteAsync($"data: {errorDetails}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }
    // Signal completion
    await context.Response.WriteAsync("data: [DONE]\n\n", ct);
    await context.Response.Body.FlushAsync(ct);

});

app.Run();


// =======================================================
// TYPE DECLARATIONS MUST BE AT THE VERY BOTTOM
// =======================================================

public interface ITravelBookingService
{
    Task<string> SearchFlightsAndHotelsAsync(string destination, decimal maxBudget);
}

public class TravelBookingService : ITravelBookingService
{
    public Task<string> SearchFlightsAndHotelsAsync(string destination, decimal maxBudget)
    {
        //Mocked response from a real flight/hotel GDS API
        return Task.FromResult(
            $"[API Match for {destination}] Flight: $450 round-trip. Hotel: 'Grand Central Inn' at $120/night. Estimated 3-night total: $810.");
    }
}

public record TravelRequest(string Destination, decimal MaxBudget, int Days);

