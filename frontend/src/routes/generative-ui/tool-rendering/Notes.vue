<script setup lang="ts">
import Callout from "@/components/harness/Callout.vue";
import Panel from "@/components/harness/Panel.vue";
import PublishedCode from "@/components/harness/PublishedCode.vue";
import RouteHeader from "@/components/harness/RouteHeader.vue";
import SourceCode from "@/components/harness/SourceCode.vue";
import TryIt from "@/components/harness/TryIt.vue";
const D = "frontend/src/routes/generative-ui/tool-rendering/demo-chat";
</script>

<template>
  <RouteHeader path="/generative-ui/tool-rendering" />
  <Panel title="What it demonstrates">
    <p class="text-sm leading-relaxed text-slate-700 dark:text-slate-300">
      Three ways to draw a tool call — the built-in <code>*</code> renderer, a custom catch-all, and a per-tool <code>useRenderTool</code> for
      <code>get_weather</code> — for the page's backend tool, <code>get_weather</code>, which no served agent has (a gap, see below).
    </p>
    <div class="mt-4">
      <TryIt :prompts="['What is the weather in Tokyo?']" expect="Per-tool: “Checking Tokyo” while the call runs, then the city and the tool's JSON result (20°, sunny, from the page's GetWeather). Zero-config: a status card with arguments and result. Catch-all: the BrandedToolCard stand-in." fail="A prose answer and no card: check the agent (dotnet run in agent/) is the current build with the /tool-rendering mapping, and the samples use /api/copilotkit-tool-rendering." />
    </div>
  </Panel>
  <Panel title="Published files, mounted verbatim">
    <div class="space-y-4">
      <SourceCode :file="`${D}/default/components/AgentExperience.vue`" />
      <SourceCode :file="`${D}/per-tool/components/WeatherToolCard.vue`" />
      <SourceCode :file="`${D}/per-tool/components/AgentExperience.vue`" />
      <SourceCode :file="`${D}/per-tool/App.vue`" note="The same published App.vue is copied into each variant directory; runtime-url changed to /api/copilotkit-tool-rendering (published value kept as a comment)." />
    </div>
  </Panel>
  <Panel title="The backend — the page's GetWeather, harness-built agent (at your request)">
    <div class="space-y-4">
      <SourceCode file="agent/WeatherTools.cs" note="GetWeather verbatim from the page's agent/Program.cs (generated); the class around it and the WeatherInfo record are flagged harness code." />
      <SourceCode file="agent/Program.cs" note="The Quickstart's Program.cs with one flagged block after its MapAGUIServer line: the same agent plus tools: [get_weather], mapped at /tool-rendering." />
      <SourceCode file="backend/runtimes/tool-rendering.ts" />
      <PublishedCode page="generative-ui__tool-rendering" :index="6" note="The page's whole agent/Program.cs (550 lines), as published: the showcase server, which cannot build — GetWeather and its AIFunctionFactory.Create line are taken from here." />
    </div>
  </Panel>
  <Callout tone="harness" title="What the harness built for this page (README §9.4)">
    <p>· <strong>The tool:</strong> the page's <code>GetWeather</code> method, copied verbatim out of its unbuildable <code>agent/Program.cs</code>, attached the way the page attaches it (<code>AIFunctionFactory.Create(GetWeather, options: new() { Name = "get_weather", … })</code>). The page's <code>SerializerOptions</code> come from that unbuildable host, so they are left out.</p>
    <p>· <strong><code>WeatherInfo</code> (stand-in):</strong> <code>GetWeather</code> returns it, but no page defines it. Minimal record with exactly the six properties the method sets.</p>
    <p>· <strong>The agent (one flagged edit to the Quickstart's <code>Program.cs</code>, your choice):</strong> after its <code>app.MapAGUIServer("/", agent);</code> line, a second agent built the same way plus that one tool, mapped at <code>/tool-rendering</code> in the same server. The Quickstart's own <code>/</code> agent is unchanged (verified: its model request carries no tools). The runtime serves the new path as <code>default</code> on <code>/api/copilotkit-tool-rendering</code>; the three <code>App.vue</code> copies point there.</p>
    <p>· Verified with a local fake model that calls <code>get_weather</code>: the run streamed <code>TOOL_CALL_START/ARGS/END</code> and a <code>TOOL_CALL_RESULT</code> carrying the method's JSON (Tokyo, 20, sunny…). Not yet driven against a live model.</p>
  </Callout>
  <Panel title="Custom catch-all" description="The page prints only one line for this file.">
    <div class="space-y-4">
      <PublishedCode page="generative-ui__tool-rendering" :index="1" />
      <SourceCode :file="`${D}/catch-all/components/AgentExperience.vue`" />
      <SourceCode :file="`${D}/catch-all/components/BrandedToolCard.vue`" />
    </div>
  </Panel>
  <Panel title="Published, not mounted">
    <PublishedCode page="generative-ui__tool-rendering" :index="4" note="Fragment: `parameters` and `locale` are never declared." />
  </Panel>
  <Callout tone="harness" title="What the harness added">
    <p>· The catch-all <code>AgentExperience.vue</code> wraps the published line; <code>BrandedToolCard</code> is a stand-in — never published.</p>
  </Callout>
</template>
