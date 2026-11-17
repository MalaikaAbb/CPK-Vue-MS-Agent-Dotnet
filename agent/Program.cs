#pragma warning disable OPENAI001
using Microsoft.Extensions.AI;
                        using Microsoft.Agents.AI;

                        using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
                        using OpenAI;
                        using OpenAI.Chat;

                        var builder = WebApplication.CreateBuilder(args);

                        builder.Services.AddAGUIServer();
                        var app = builder.Build();

                        var openAiApiKey = builder.Configuration["OPENAI_API_KEY"]
                            ?? throw new InvalidOperationException("Set OPENAI_API_KEY");
                        var openAI = new OpenAIClient(openAiApiKey);

                        var agent = openAI.GetChatClient("gpt-6-luna")
    .AsIChatClient().AsBuilder()
    .ConfigureOptions(options => options.RawRepresentationFactory = _ =>
        new OpenAI.Chat.ChatCompletionOptions { ReasoningEffortLevel = new("none") })
    .Build().AsAIAgent(
                            name: "MyAgent",
                            instructions: "You are a helpful assistant.");


                        app.MapAGUIServer("/", agent);

// ── [harness] added at the user's request — NOT in the published Quickstart ──
// Tool Rendering's agent: the agent above, built the same way, plus the Tool
// Rendering page's GetWeather (WeatherTools.cs), mapped on its own path so the
// Quickstart's "/" agent is unchanged. The runtime serves it as `default` on
// /api/copilotkit-tool-rendering.
var weatherTools = new WeatherTools(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<WeatherTools>());
var weatherAgent = openAI.GetChatClient("gpt-6-luna")
    .AsIChatClient().AsBuilder()
    .ConfigureOptions(options => options.RawRepresentationFactory = _ =>
        new OpenAI.Chat.ChatCompletionOptions { ReasoningEffortLevel = new("none") })
    .Build().AsAIAgent(
        name: "MyAgent",
        instructions: "You are a helpful assistant.",
        tools: [weatherTools.CreateGetWeatherTool()]);
app.MapAGUIServer("/tool-rendering", weatherAgent);
// ── [harness] end ──

                        app.Run("http://localhost:8000");
