<script setup lang="ts">
import { CopilotKitProvider } from "@copilotkit/vue/v2";
import AgentExperience from "./components/AgentExperience.vue";
</script>

<template>
  <!-- [harness] published: runtime-url="/api/copilotkit" — this page's agent (the Quickstart agent + the page's GetWeather, mapped at /tool-rendering in agent/Program.cs) is served as `default` on /api/copilotkit-tool-rendering (README §9) -->
  <CopilotKitProvider runtime-url="/api/copilotkit-tool-rendering"><AgentExperience /></CopilotKitProvider>
</template>
