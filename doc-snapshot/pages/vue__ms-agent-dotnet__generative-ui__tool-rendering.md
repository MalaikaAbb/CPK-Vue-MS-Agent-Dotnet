# Tool Call Rendering

> Render your agent's tool calls with custom UI components.

## What is this?

Tools are how an LLM invokes predefined, typically-deterministic functions. Tool rendering lets you decide how each of those tool calls appears in the chat. Instead of showing raw JSON, you register a Vue component that draws a branded card for the call (arguments, live status, and the eventual result). This is the **Generative UI** variant CopilotKit calls **tool rendering**.

<Callout type="info">
  **Free course:** See this pattern built end-to-end in [Build Interactive Agents with Generative UI](https://www.deeplearning.ai/short-courses/build-interactive-agents-with-generative-ui/) — a free DeepLearning.AI short course taught by CopilotKit's CEO covering the full Generative UI spectrum (Controlled, Declarative, and Open-Ended).
</Callout>

## When should I use this?

Render tool calls when you want to:

- Show users exactly what tools the agent is invoking and with what arguments
- Display live progress indicators while a tool executes
- Render rich, polished results once a tool completes
- Give tool-heavy agents a transparent, on-brand chat experience

## Default tool rendering (zero-config)

The simplest entry point is `useDefaultRenderTool()` with no arguments. CopilotKit registers its built-in default renderer as the `*` wildcard: every tool call renders as a status card (tool name, live **Running → Done** state, collapsible arguments/result) without you writing any UI.

Without this composable the runtime has no `*` renderer and tool calls are invisible; the user only sees the assistant's final text summary.

```vue title="src/components/AgentExperience.vue"
<script setup lang="ts">
import { CopilotChat, useDefaultRenderTool } from "@copilotkit/vue/v2";

useDefaultRenderTool();
</script>

<template><CopilotChat /></template>
```

## Custom catch-all

Once you want on-brand chrome, pass a `render` component or function to `useDefaultRenderTool`. It's a convenience wrapper around `useRenderTool({ name: "*", ... })`: one wildcard renderer handles every tool call, named or not.

```ts title="src/components/AgentExperience.vue"
useDefaultRenderTool({ render: BrandedToolCard });
```

## Per-tool renderers

The most expressive path is one renderer per tool name. Each renderer receives the tool's parsed arguments, a live `status`, and, once the agent returns, the `result`.

```vue title="src/components/WeatherToolCard.vue"
<script setup lang="ts">
defineProps<{
  parameters: { city?: string };
  status: "inProgress" | "executing" | "complete";
  result?: string;
}>();
</script>

<template>
  <p v-if="status !== 'complete'">Checking {{ parameters.city ?? "…" }}</p>
  <article v-else>
    <strong>{{ parameters.city }}</strong>
    <pre>{{ result }}</pre>
  </article>
</template>
```

```vue title="src/components/AgentExperience.vue"
<script setup lang="ts">
import { z } from "zod";
import { CopilotChat, useDefaultRenderTool, useRenderTool } from "@copilotkit/vue/v2";
import WeatherToolCard from "./WeatherToolCard.vue";

useRenderTool({
  name: "get_weather",
  parameters: z.object({ city: z.string() }),
  render: WeatherToolCard,
});

useDefaultRenderTool(); // wildcard fallback for every other tool
</script>

<template><CopilotChat /></template>
```

`parameters` is partial while arguments stream. Read required fields only after the status advances from `inProgress`. The result is a string when status is `complete`; parse it only if your backend deliberately returns a documented serialized shape.

<Callout type="info">
  The `name` you pass to `useRenderTool` must match the tool name the agent exposes; that's how the runtime routes the call to your component.
</Callout>

Per-tool renderers compose with a catch-all: named renderers claim the "interesting" tools and a wildcard handles everything else. The example registers the built-in wildcard after the named `get_weather` renderer.

If the renderer closes over reactive values, pass Vue watch sources as the second argument:

```ts
useRenderTool({ name: "get_weather", parameters, render: WeatherToolCard }, [locale]);
```

Finally, mount the registration component beneath the provider:

```vue title="src/App.vue"
<script setup lang="ts">
import { CopilotKitProvider } from "@copilotkit/vue/v2";
import AgentExperience from "./components/AgentExperience.vue";
</script>

<template>
  <CopilotKitProvider runtime-url="/api/copilotkit"><AgentExperience /></CopilotKitProvider>
</template>
```

## The backend tool definition

The frontend renderer only sees what the agent sends down. The matching backend must expose a tool named `get_weather`, return structured data, and let the frontend renderer with the same name paint the card. The Vue renderer does not create or execute that backend tool.

<!-- setup skipped: tool-rendering-setup is not bundled for ms-agent-dotnet -->

```csharp
// agent/Program.cs
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using System.ClientModel;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Add(SalesAgentSerializerContext.Default);
    // Serialize our enum types (SalesStage, Currency, FlightStatus) as their
    // member name strings rather than numeric ordinals. This keeps the wire
    // format human-readable and stable across enum re-ordering.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddAGUI();
// STOPGAP: IHttpContextAccessor lets AimockHeaderPolicy read the current
// request's forwarded x-* headers (stashed on HttpContext.Items by
// AimockHeaderMiddleware) at outbound-LLM-call time. HttpContext flows across
// the AG-UI SSE-pump ExecutionContext boundary, unlike a middleware-set
// AsyncLocal. TODO(copilotkit-sdk-dotnet): migrate to SDK-level header propagation.
builder.Services.AddHttpContextAccessor();

WebApplication app = builder.Build();

// STOPGAP: seed the static accessor the outbound header-forwarding policy reads
// (the policy is created without DI, mirroring CvDiag.Logger).
AimockHeaderPolicy.HttpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();

// STOPGAP: Extract x-* prefixed headers from incoming AG-UI requests onto HttpContext.Items
// so AimockHeaderPolicy can forward them to outgoing OpenAI calls.
// TODO(copilotkit-sdk-dotnet): migrate to SDK-level header propagation
app.UseMiddleware<AimockHeaderMiddleware>();

// CVDIAG: backend flap-observability emitter (plan unit L1-F; spec §3). OFF by
// default (CVDIAG_BACKEND_EMITTER=on to arm). Seed the static singleton the
// outbound LLM policy reads (created without DI), then register the
// request-pipeline instrumentation AFTER AimockHeaderMiddleware so the forwarded
// x-* correlation headers are already stashed on HttpContext.Items.
CvdiagBackend.Instance = new CvdiagBackend();
app.UseMiddleware<CvdiagInstrumentationMiddleware>();

// Create the agent factory and map the AG-UI agent endpoint
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
// CVDIAG: seed the static logger used by AimockHeaderPolicy (created without DI)
// to emit the outbound-LLM header-forwarding breadcrumb.
CvDiag.Logger = loggerFactory.CreateLogger("CvDiag");
var jsonOptions = app.Services.GetRequiredService<IOptions<JsonOptions>>();
var agentFactory = new SalesAgentFactory(builder.Configuration, loggerFactory, jsonOptions.Value.SerializerOptions);
app.MapAGUI("/", agentFactory.CreateSalesAgent());

var d5ParityFactory = new D5ParityAgentFactory(builder.Configuration, loggerFactory, jsonOptions.Value.SerializerOptions);
app.MapAGUI("/headless-complete", d5ParityFactory.CreateHeadlessCompleteAgent());
app.MapAGUI("/voice", d5ParityFactory.CreateVoiceAgent());
app.MapAGUI("/gen-ui-agent", d5ParityFactory.CreateGenUiAgent());
app.MapAGUI("/gen-ui-tool-based", d5ParityFactory.CreateGenUiToolBasedAgent());
app.MapAGUI("/shared-state-streaming", d5ParityFactory.CreateSharedStateStreamingAgent());
app.MapAGUI("/readonly-state-agent-context", d5ParityFactory.CreateReadonlyStateAgentContext());
app.MapAGUI("/tool-rendering", d5ParityFactory.CreateToolRenderingAgent(reasoning: false));
app.MapAGUI("/tool-rendering-reasoning-chain", d5ParityFactory.CreateToolRenderingAgent(reasoning: true));

// Interrupt-adapted agent: mounted on its own path so the Next.js runtime
// can proxy the `gen-ui-interrupt` and `interrupt-headless` demo names to
// it. The two demos share this single backend — the differentiation happens
// on the frontend (in-chat picker vs. headless/app-surface picker).
var interruptAgentFactory = new InterruptAgentFactory(builder.Configuration, loggerFactory, jsonOptions.Value.SerializerOptions);
app.MapAGUI("/interrupt-adapted", interruptAgentFactory.CreateInterruptAgent());

// Multimodal demo agent (vision-capable gpt-6-luna, no tools).
// The Microsoft AG-UI ASP.NET adapter currently rejects AG-UI content arrays
// before the agent can see image/document parts, so this one endpoint parses
// the request body directly and emits the small AG-UI SSE event subset the
// chat UI needs for text streaming.
app.MapPost("/multimodal", (HttpContext context) => MultimodalEndpoint.HandleAsync(
    context,
    agentFactory.CreateMultimodalChatClient(),
    loggerFactory.CreateLogger("MultimodalEndpoint")));

// A2UI Error Recovery demo. Backend-owned render->validate->retry loop with a
// graceful a2ui_recovery_exhausted fallback. Mounted as a raw SSE endpoint (NOT
// MapAGUI): the recovery-exhausted card only renders from an a2ui-surface
// ACTIVITY_SNAPSHOT carrying status:"failed", which the MS Agent Framework AG-UI
// adapter cannot emit — so this endpoint hand-writes the AG-UI SSE stream, the
// same adapter-bypass pattern used by /multimodal. See agent/RecoveryAgent.cs.
app.MapPost("/a2ui-recovery", (HttpContext context) => RecoveryAgent.HandleAsync(
    context,
    builder.Configuration,
    loggerFactory.CreateLogger("RecoveryAgent")));

// Beautiful Chat flagship demo.
app.MapAGUI("/beautiful-chat", agentFactory.CreateBeautifulChatAgent());

// Agent Config demo — wraps a basic ChatClientAgent in AgentConfigAgent.
app.MapAGUI("/agent-config", agentFactory.CreateAgentConfigAgent());

// Reasoning demo — wraps a basic ChatClientAgent in ReasoningAgent via a
// static factory that builds its own chat client off the shared OpenAI client.
app.MapAGUI("/reasoning", agentFactory.CreateReasoningAgent());

// Declarative Gen UI (instance factory — builds its own chat client).
var declarativeGenUiAgent = new DeclarativeGenUiAgent(builder.Configuration, loggerFactory, jsonOptions.Value.SerializerOptions);
app.MapAGUI("/declarative-gen-ui", declarativeGenUiAgent.Create());

// A2UI fixed-schema demo (instance factory).
var a2uiFixedSchemaAgent = new A2uiFixedSchemaAgent(builder.Configuration, loggerFactory, jsonOptions.Value.SerializerOptions);
app.MapAGUI("/a2ui-fixed-schema", a2uiFixedSchemaAgent.Create());

// Open Generative UI — basic + advanced.
var openGenUiFactory = new OpenGenUiAgentFactory(builder.Configuration);
app.MapAGUI("/open-gen-ui", openGenUiFactory.CreateAgent());
var openGenUiAdvancedFactory = new OpenGenUiAdvancedAgentFactory(builder.Configuration);
app.MapAGUI("/open-gen-ui-advanced", openGenUiAdvancedFactory.CreateAgent());

// BYOC demos (hashbrown + json-render).
var byocHashbrownFactory = new ByocHashbrownAgentFactory(builder.Configuration, loggerFactory);
app.MapAGUI("/byoc-hashbrown", byocHashbrownFactory.CreateAgent());
var byocJsonRenderFactory = new ByocJsonRenderAgentFactory(builder.Configuration, loggerFactory);
app.MapAGUI("/byoc-json-render", byocJsonRenderFactory.CreateAgent());

// MCP Apps demo.
var mcpAppsFactory = new McpAppsAgentFactory(builder.Configuration, loggerFactory);
app.MapAGUI("/mcp-apps", mcpAppsFactory.CreateMcpAppsAgent());

// In-app HITL demo.
var hitlInAppFactory = new HitlInAppAgentFactory(builder.Configuration, loggerFactory);
app.MapAGUI("/hitl-in-app", hitlInAppFactory.CreateHitlInAppAgent());

// In-chat HITL demo (useHumanInTheLoop). The `book_call` tool is defined
// entirely on the frontend via the hook; this backend is a plain
// ChatClientAgent with a system prompt that nudges the model to call it.
// See agent/HitlInChatAgent.cs.
var hitlInChatFactory = new HitlInChatAgentFactory(builder.Configuration, loggerFactory);
app.MapAGUI("/hitl-in-chat", hitlInChatFactory.CreateHitlInChatAgent());

// Shared State (Read + Write) demo. UI owns `preferences`, agent owns
// `notes` via a `set_notes` tool. See agent/SharedStateReadWriteAgent.cs
// for the pattern.
var sharedStateReadWriteFactory = new SharedStateReadWriteAgentFactory(builder.Configuration, loggerFactory, jsonOptions.Value.SerializerOptions);
app.MapAGUI("/shared-state-read-write", sharedStateReadWriteFactory.CreateAgent());

// Sub-Agents demo. Supervisor delegates to research / writing / critique
// sub-agents via tools, recording each delegation in shared state for the
// UI's live delegation log. See agent/SubagentsAgent.cs.
var subagentsFactory = new SubagentsAgentFactory(builder.Configuration, loggerFactory, jsonOptions.Value.SerializerOptions);
app.MapAGUI("/subagents", subagentsFactory.CreateAgent());

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

// =================
// State Management
// =================

// Stage of a deal in the sales pipeline. Modeled as an enum so callers and
// the LLM's structured output both get a closed set of legal values, rather
// than a free-form string that can drift. Serialized as the
// enum member name via JsonStringEnumConverter on the JsonSerializerOptions.
public enum SalesStage
{
    Prospect,
    Qualified,
    Proposal,
    Negotiation,
    ClosedWon,
    ClosedLost,
}

// Currency code for deal values. Small closed set covers the demo use cases.
// Previously `Value` was an `int` with no currency indication at all; we now
// carry currency + decimal amount together.
public enum Currency
{
    USD,
    EUR,
    GBP,
    JPY,
}

public record SalesTodo
{
    /// <summary>
    /// The stable identifier for this todo.
    /// </summary>
    /// <remarks>
    /// The empty string is a load-bearing sentinel meaning "no id yet;
    /// server should assign one". <see cref="SalesState.ReplaceTodos"/>
    /// backfills any todo with <c>Id == ""</c> by generating a fresh Guid
    /// (see that method's documentation). Callers that want to express
    /// "pending, please assign" should use <see cref="NewPending"/> rather
    /// than constructing with an arbitrary placeholder string.
    ///
    /// <see langword="required"/> is retained for compile-time presence so
    /// callers have to acknowledge the id contract, but runtime validation
    /// does NOT reject the empty-string sentinel — that would break the
    /// server-assigned-id path described above.
    /// </remarks>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Factory for "pending" todos: creates a SalesTodo with a
    /// freshly-generated Guid-derived id so the empty-string sentinel never
    /// leaks into code that doesn't understand the backfill contract.
    /// </summary>
    public static SalesTodo NewPending(
        string title = "",
        SalesStage stage = SalesStage.Prospect,
        decimal value = 0m,
        Currency currency = Currency.USD,
        DateOnly? dueDate = null,
        string assignee = "") => new()
        {
            // 16 hex chars = 64 bits of entropy. 8 chars was ~32 bits and
            // has a non-trivial collision risk at tens of thousands of
            // todos; 16 pushes collision risk well past demo scale.
            Id = Guid.NewGuid().ToString("n")[..16],
            Title = title,
            Stage = stage,
            Value = value,
            Currency = currency,
            DueDate = dueDate,
            Assignee = assignee,
        };

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("stage")]
    public SalesStage Stage { get; init; } = SalesStage.Prospect;

    // Deal value as a decimal (money) with explicit currency. Previously an
    // `int` with no sign or currency semantics. The init accessor validates
    // non-negative — negative deal values are not a legal business state in
    // this demo.
    [JsonPropertyName("value")]
    public decimal Value
    {
        get => _value;
        init
        {
            if (value < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "SalesTodo.Value must be non-negative.");
            }
            _value = value;
        }
    }
    private readonly decimal _value;

    [JsonPropertyName("currency")]
    public Currency Currency { get; init; } = Currency.USD;

    // Nullable DateOnly — previously a free-form string that accepted any
    // input. System.Text.Json serializes DateOnly as ISO-8601 "YYYY-MM-DD".
    [JsonPropertyName("dueDate")]
    public DateOnly? DueDate { get; init; }

    [JsonPropertyName("assignee")]
    public string Assignee { get; init; } = "";

    /// <summary>
    /// Whether this deal is finished (won or lost). Derived from
    /// <see cref="Stage"/> so that the pair cannot disagree: a Prospect deal
    /// cannot be "completed", and a ClosedWon/ClosedLost deal cannot be
    /// "incomplete". Previously <c>Completed</c> was an independent bool and
    /// contradictions like <c>{Stage=ClosedWon, Completed=false}</c> were
    /// representable.
    /// </summary>
    [JsonPropertyName("completed")]
    public bool Completed => Stage is SalesStage.ClosedWon or SalesStage.ClosedLost;
}

// SalesState is the server-side in-memory store, SalesStateSnapshot is the
// wire-format JSON Schema sent to the model. Previously both carried near-
// identical List<SalesTodo>. We consolidate: SalesState holds a
// read-only list behind an encapsulated replacement API, and
// SalesStateSnapshot is a minimal record that wraps the same list for
// serialization.
public sealed class SalesState
{
    private IReadOnlyList<SalesTodo> _todos = Array.Empty<SalesTodo>();

    /// <summary>
    /// Current published todo list. Reads are lock-free: reference reads of
    /// a field are atomic on .NET, and the single writer
    /// (<see cref="ReplaceTodos"/>) publishes a new fully-materialized list
    /// by a single reference assignment. We use <see cref="Volatile.Read{T}"/>
    /// to prevent the JIT from hoisting the read past a synchronization
    /// boundary on the reader side.
    /// </summary>
    public IReadOnlyList<SalesTodo> Todos => Volatile.Read(ref _todos);

    /// <summary>
    /// Atomically replaces the todo list, backfilling any todo whose
    /// <see cref="SalesTodo.Id"/> is empty (or null) with a freshly-generated
    /// Guid-derived id. This is the explicit contract for callers that want
    /// server-assigned ids: pass a SalesTodo with <c>Id = ""</c> and this
    /// method generates a stable id for it. Non-empty ids are preserved as-is.
    /// </summary>
    /// <remarks>
    /// Generated ids are 16 hex chars (64 bits of entropy), derived from a
    /// fresh <see cref="Guid"/>. The write is a single reference assignment
    /// via <see cref="Volatile.Write{T}"/>, which is atomic and visible to
    /// readers without a lock.
    /// </remarks>
    public void ReplaceTodos(IEnumerable<SalesTodo> todos)
    {
        ArgumentNullException.ThrowIfNull(todos);
        var materialized = todos.Select(t => t with
        {
            // 16 hex chars = 64 bits. Previously 8 (32 bits) had a non-
            // trivial collision probability at tens of thousands of todos.
            Id = string.IsNullOrEmpty(t.Id) ? Guid.NewGuid().ToString("n")[..16] : t.Id,
        }).ToArray();

        Volatile.Write(ref _todos, materialized);
    }
}

// =================
// Flight Data
// =================

// Flight operational status. StatusColor was previously a separate string
// field that could disagree with Status; we now derive color
// from this enum deterministically in FlightInfo.StatusColor.
public enum FlightStatus
{
    OnTime,
    Delayed,
    Cancelled,
    Boarding,
}

public record FlightInfo
{
    [JsonPropertyName("airline")]
    public string Airline { get; init; } = "";

    [JsonPropertyName("airlineLogo")]
    public string AirlineLogo { get; init; } = "";

    [JsonPropertyName("flightNumber")]
    public string FlightNumber { get; init; } = "";

    [JsonPropertyName("origin")]
    public string Origin { get; init; } = "";

    [JsonPropertyName("destination")]
    public string Destination { get; init; } = "";

    [JsonPropertyName("date")]
    public string Date { get; init; } = "";

    [JsonPropertyName("departureTime")]
    public string DepartureTime { get; init; } = "";

    [JsonPropertyName("arrivalTime")]
    public string ArrivalTime { get; init; } = "";

    [JsonPropertyName("duration")]
    public string Duration { get; init; } = "";

    // Status as enum. Previously `Status` and `StatusColor` were
    // independent free-form strings that could disagree (e.g. "On Time" with
    // color "red"). Now StatusColor is derived from Status and the pair is
    // guaranteed consistent.
    [JsonPropertyName("status")]
    public FlightStatus Status { get; init; } = FlightStatus.OnTime;

    [JsonPropertyName("statusColor")]
    public string StatusColor => Status switch
    {
        FlightStatus.OnTime => "green",
        FlightStatus.Delayed => "yellow",
        FlightStatus.Cancelled => "red",
        FlightStatus.Boarding => "blue",
        _ => "gray",
    };

    // Price as decimal (money) + separate Currency enum. The
    // old shape carried both a display string like "$342" AND a currency
    // code "USD" — redundant and easy to get out of sync.
    [JsonPropertyName("price")]
    public decimal Price { get; init; }

    [JsonPropertyName("currency")]
    public Currency Currency { get; init; } = Currency.USD;
}

// =================
// Agent Factory
// =================
public class SalesAgentFactory
{
    private readonly IConfiguration _configuration;
    private readonly SalesState _state;
    private readonly OpenAIClient _openAiClient;
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public SalesAgentFactory(IConfiguration configuration, ILoggerFactory loggerFactory, JsonSerializerOptions jsonSerializerOptions)
    {
        _configuration = configuration;
        _state = new();
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SalesAgentFactory>();
        _jsonSerializerOptions = jsonSerializerOptions;

        var apiKey = ApiKeyResolver.ResolveApiKey(_configuration);

        // Log the resolved OpenAI endpoint at startup so operators can tell
        // whether we're hitting a custom OPENAI_BASE_URL or falling back to the
        // GitHub Models / Azure default. Previously the fallback was silent.
        var endpoint = ApiKeyResolver.ResolveEndpoint(_configuration);
        _logger.LogInformation("Using OpenAI endpoint: {Endpoint}", endpoint);

        _openAiClient = new(
            new ApiKeyCredential(apiKey),
            AimockHeaderPolicy.CreateOpenAIClientOptions(endpoint));
    }

    public AIAgent CreateSalesAgent()
    {
        var chatClient = _openAiClient.GetChatClient("gpt-6-luna").AsLunaChatClient();

        var chatClientAgent = new ChatClientAgent(
            chatClient,
            name: "SalesAgent",
            instructions: @"A helpful assistant that helps manage a sales pipeline.
            You have tools available to get, update, and query sales data.
            You can search for flights and generate dynamic UI.
            When discussing deals or the pipeline, ALWAYS use the get_sales_todos tool to see the current state before mentioning, updating, or discussing deals with the user.",
            tools: [
                AIFunctionFactory.Create(GetSalesTodos, options: new() { Name = "get_sales_todos", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(ManageSalesTodos, options: new() { Name = "manage_sales_todos", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(QueryData, options: new() { Name = "query_data", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(GetWeather, options: new() { Name = "get_weather", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(SearchFlights, options: new() { Name = "search_flights", SerializerOptions = _jsonSerializerOptions }),
                AIFunctionFactory.Create(GenerateA2ui, options: new() { Name = "generate_a2ui", SerializerOptions = _jsonSerializerOptions })
            ]);

        return new SharedStateAgent(chatClientAgent, _jsonSerializerOptions, _loggerFactory.CreateLogger<SharedStateAgent>());
    }

    // Factory method for the Multimodal demo's vision-capable agent. Reuses
    // the shared OpenAIClient so we don't re-resolve credentials for each
    // mount. No tools — the chat model consumes attachments natively.
    public AIAgent CreateMultimodalAgent() => MultimodalAgentFactory.Create(_openAiClient);

    public IChatClient CreateMultimodalChatClient() =>
        _openAiClient.GetChatClient("gpt-6-luna").AsLunaChatClient();

    // Factory method for the Beautiful Chat flagship demo. Holds its own
    // per-factory tool surface + in-memory todo store so it doesn't
    // interfere with the sales pipeline state owned by the main agent.
    public AIAgent CreateBeautifulChatAgent()
    {
        var factory = new BeautifulChatAgentFactory(
            _configuration,
            _openAiClient,
            _jsonSerializerOptions,
            _loggerFactory.CreateLogger<BeautifulChatAgentFactory>());
        return factory.Create();
    }

    // Factory method for the Agent Config demo. Wraps a neutral ChatClientAgent
    // (no tools) in AgentConfigAgent so the tone/expertise/responseLength
    // directives read from AG-UI shared state steer the inner model per-turn.
    public AIAgent CreateAgentConfigAgent()
    {
        var chatClient = _openAiClient.GetChatClient("gpt-6-luna").AsLunaChatClient();
        var inner = new ChatClientAgent(
            chatClient,
            name: "AgentConfigInner",
            instructions: "You are a helpful assistant. Follow the tone, expertise, and response-length directives in the system message for each turn.",
            tools: []);
        return new AgentConfigAgent(inner, _loggerFactory.CreateLogger<AgentConfigAgent>());
    }

    // Factory method for the Reasoning demo. Delegates to the static
    // ReasoningAgentFactory.Create(...) which expects an IChatClient +
    // ILoggerFactory and wraps a ChatClientAgent in a DelegatingAIAgent that
    // surfaces reasoning-chain events.
    public AIAgent CreateReasoningAgent()
    {
        var chatClient = _openAiClient.GetChatClient("gpt-6-luna").AsLunaChatClient();
        return ReasoningAgentFactory.Create(chatClient, _loggerFactory);
    }

    // =================
    // Tools
    // =================

    [Description("Get the current sales pipeline")]
    private List<SalesTodo> GetSalesTodos()
    {
        var todos = _state.Todos;
        _logger.LogInformation("Getting sales todos: {Count} items", todos.Count);
        // Return a snapshot list copy — callers (AIFunctionFactory) serialize
        // this and we don't want concurrent ReplaceTodos mutating mid-serialize.
        return todos.ToList();
    }

    [Description("Update the sales pipeline")]
    private string ManageSalesTodos([Description("The updated list of sales todos")] List<SalesTodo> todos)
    {
        ArgumentNullException.ThrowIfNull(todos);
        _logger.LogInformation("Updating sales todos: {Count} items", todos.Count);
        _state.ReplaceTodos(todos);
        return "Pipeline updated";
    }

    [Description("Query financial data for charts")]
    private string QueryData([Description("The query to run")] string query)
    {
        _logger.LogInformation("Querying data: {Query}", query);
        var categories = new[] { "Engineering", "Marketing", "Sales", "Support", "Design" };
        var random = new Random();
        var results = categories.Select(c => new { category = c, value = random.Next(10000, 100000), quarter = "Q1 2026" });
        return JsonSerializer.Serialize(results);
    }

    [Description("Get the weather for a given location. Ensure location is fully spelled out.")]
    private WeatherInfo GetWeather([Description("The location to get the weather for")] string location)
    {
        _logger.LogInformation("Getting weather for: {Location}", location);
        return new()
        {
            City = location,
            Temperature = 20,
            Conditions = "sunny",
            Humidity = 50,
            WindSpeed = 10,
            FeelsLike = 25
        };
    }
```

<IntegrationGrid path="generative-ui/tool-rendering" />
