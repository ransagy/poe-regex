// Clipboard helper: writes text to the system clipboard using whichever tool is
// available (wl-copy > xclip > xsel > pbcopy > clip). Returns true on success.
import { execFile } from "node:child_process";

const TOOLS = ["wl-copy", "xclip", "xsel", "pbcopy", "clip"];

export function copyToClipboard(text) {
  return new Promise((resolve) => {
    const tryTool = (i) => {
      if (i >= TOOLS.length) return resolve(false);
      const tool = TOOLS[i];
      execFile(tool, [], { input: text }, (error) => {
        if (error) return tryTool(i + 1);
        resolve(true);
      });
    };
    tryTool(0);
  });
}
