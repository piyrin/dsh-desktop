import { flushCompileCache } from "node:module";
import { pathToFileURL } from "node:url";

const entry = process.env.DSH_DESKTOP_ENTRY;
const port = process.env.DSH_DESKTOP_PORT || "8080";
if (!entry) throw new Error("DSH_DESKTOP_ENTRY is not set");

process.argv = [process.execPath, entry, "--profile", "web", "--port", port];
await import(pathToFileURL(entry).href);
flushCompileCache();
