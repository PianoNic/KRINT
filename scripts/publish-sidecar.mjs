// Publishes KRINT.API as a self-contained single-file executable and drops it into
// src-tauri/binaries/ with the Tauri target-triple suffix that `externalBin` expects.
//
// Run automatically by Tauri's `beforeBuildCommand`, or manually:
//   node scripts/publish-sidecar.mjs
//
// Override the .NET runtime identifier (RID) when cross-publishing, e.g.:
//   RID=osx-arm64 node scripts/publish-sidecar.mjs

import { execSync } from 'node:child_process';
import { existsSync, mkdirSync, copyFileSync, rmSync, cpSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(__dirname, '..');

// ---- 1. Build the Angular SPA and stage it as a Tauri resource (wwwroot) ----------------
// The desktop sidecar serves the SPA itself (Production MapFallbackToFile), but a single-file
// publish can't embed the loose static files - so we bundle them as a Tauri resource and point
// the API's content root at it (see src-tauri/src/lib.rs).
const frontendDir = join(repoRoot, 'src', 'KRINT.Frontend');
const frontendBuild = join(repoRoot, 'src-tauri', 'binaries', 'frontend-build');
const wwwroot = join(repoRoot, 'src-tauri', 'resources', 'wwwroot');

console.log('[publish-sidecar] building frontend (bun)');
execSync('bun install --frozen-lockfile', { cwd: frontendDir, stdio: 'inherit' });
execSync(`bun run build:agent --output-path "${frontendBuild}"`, { cwd: frontendDir, stdio: 'inherit' });

rmSync(wwwroot, { recursive: true, force: true });
mkdirSync(wwwroot, { recursive: true });
cpSync(join(frontendBuild, 'browser'), wwwroot, { recursive: true });
console.log(`[publish-sidecar] SPA -> ${wwwroot}`);

// ---- 2. Publish KRINT.API as a self-contained single-file sidecar -----------------------

// Tauri target triple (what externalBin appends). e.g. x86_64-pc-windows-msvc
const triple = execSync('rustc --print host-tuple').toString().trim();
const isWindows = triple.includes('windows');
const ext = isWindows ? '.exe' : '';

// Map the Tauri triple -> .NET RID (override via env RID).
const ridMap = {
  'x86_64-pc-windows-msvc': 'win-x64',
  'aarch64-pc-windows-msvc': 'win-arm64',
  'x86_64-apple-darwin': 'osx-x64',
  'aarch64-apple-darwin': 'osx-arm64',
  'x86_64-unknown-linux-gnu': 'linux-x64',
  'aarch64-unknown-linux-gnu': 'linux-arm64',
};
const rid = process.env.RID || ridMap[triple];
if (!rid) {
  throw new Error(`No .NET RID mapping for target triple "${triple}". Set RID=<rid> to override.`);
}

const project = join(repoRoot, 'src', 'KRINT.API', 'KRINT.API.csproj');
const publishDir = join(repoRoot, 'src-tauri', 'binaries', `publish-${rid}`);
const binariesDir = join(repoRoot, 'src-tauri', 'binaries');
mkdirSync(binariesDir, { recursive: true });

console.log(`[publish-sidecar] publishing ${rid} (triple ${triple})`);
// NOTE: keep ICU/globalization (do NOT set InvariantGlobalization) — the MSSQL client needs it.
execSync(
  `dotnet publish "${project}" -c Release -r ${rid} --self-contained true ` +
    `/p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ` +
    `-o "${publishDir}"`,
  { stdio: 'inherit' },
);

const built = join(publishDir, `KRINT.API${ext}`);
if (!existsSync(built)) {
  throw new Error(`Expected published binary not found: ${built}`);
}

const target = join(binariesDir, `krint-api-${triple}${ext}`);
copyFileSync(built, target);
console.log(`[publish-sidecar] -> ${target}`);
