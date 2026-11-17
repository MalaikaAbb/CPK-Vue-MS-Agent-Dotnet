import { CopilotRuntime } from "@copilotkit/runtime/v2";
import { HttpAgent } from "@ag-ui/client";

/**
 * [harness] The runtime behind /generative-ui/tool-rendering, mounted at
 * /api/copilotkit-tool-rendering (at the user's request).
 *
 * The Tool Rendering samples use no agent-id, i.e. `default` — the id every
 * other route uses for the Quickstart agent. To give this page an agent that
 * has the page's published GetWeather, `default` here is the landing page's
 * HttpAgent pointed at <AGENT_URL>tool-rendering, where agent/Program.cs
 * (one flagged addition) maps the Quickstart's agent plus that one tool. The
 * page's three App.vue copies have their runtime-url changed to this path,
 * with the published value kept as a comment.
 */
export function createToolRenderingRuntime(): CopilotRuntime {
  return new CopilotRuntime({
    agents: {
      default: new HttpAgent({ url: new URL("tool-rendering", process.env.AGENT_URL!).toString() }),
    },
  });
}
