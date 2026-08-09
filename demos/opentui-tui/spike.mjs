// Phase 2.1 spike: de-risk OpenTUI under Node 26.4 + --experimental-ffi.
// Renders a minimal layout (Text + Input + Select + Box), then exits cleanly
// after a couple of seconds so it can be verified non-interactively.
import { createCliRenderer, Box, Text, Input, Select } from "@opentui/core";

const renderer = await createCliRenderer({
  exitOnCtrlC: true,
});

renderer.root.add(
  Box(
    { flexDirection: "column", borderStyle: "rounded", padding: 1, gap: 1 },
    Text({ content: "poe-regex spike", fg: "#00FF00" }),
    Input({ value: "maximum life", placeholder: "mod search..." }),
    Select({
      options: [
        { name: "poe1", description: "Path of Exile 1" },
        { name: "poe2", description: "Path of Exile 2" },
      ],
    }),
  ),
);

// Let it draw, then exit cleanly so automated verification does not hang.
setTimeout(() => {
  renderer.destroy();
  process.exit(0);
}, 2000);
