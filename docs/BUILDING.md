# Building & releasing

Everything builds with the standard .NET SDK (`.NET 10`). The CLI targets
`net10.0`; tests run on the same TFM. `dotnet build PoeRegex.slnx` builds all
projects, `dotnet test PoeRegex.slnx` runs the engine unit tests plus the
no-false-positive property tests over the shipped catalogs.

## Run from source (framework-dependent)

No special setup is needed beyond the .NET 10 SDK:

```sh
dotnet run --project src/PoeRegex.Cli -- --help
```

The engine reads its catalogs from embedded resources, so there is nothing to
fetch or point at — `data/catalogs/*.json` are compiled into the assembly.

## Publish a self-contained build

Self-contained builds bundle the .NET runtime, so the binary runs on machines
without an installed SDK. Publish output goes under `out/` (gitignored):

```sh
dotnet publish src/PoeRegex.Cli -c Release -r linux-x64 -o out/publish
./out/publish/PoeRegex.Cli --help
```

Any supported RID works (`linux-x64`, `win-x64`, `osx-arm64`, `osx-x64`, ...).
This form is still JIT-compiled and needs no native toolchain.

## Publish a Native AOT single-file binary

Native AOT compiles the whole program ahead of time into one native executable:
fast startup, no runtime files, smaller footprint.

```sh
dotnet publish src/PoeRegex.Cli -c Release -r linux-x64 \
  -p:PublishAot=true -p:StripSymbols=true -o out/aot
./out/aot/PoeRegex.Cli --help
```

### Native toolchain prerequisites (AOT only)

- **Linux (`linux-x64`)** — Native AOT links against the C runtime and zlib.
  On Debian/Ubuntu:
  ```sh
  sudo apt-get install -y clang zlib1g-dev
  ```
  The CI workflow (`linux-x64` leg) installs exactly these.
- **Windows (`win-x64`)** — Visual Studio 2022+ build tools (MSVC toolchain);
  included in the `windows-latest` runner image.
- **macOS (`osx-arm64` / `osx-x64`)** — Xcode Command Line Tools (clang);
  included in the `macos-latest` / `macos-26-intel` runner images.

### Runtime dependencies (all build forms)

- **`--clipboard`** copies the generated query to the system clipboard via
  [TextCopy](https://github.com/CopyText/TextCopy). On Linux it shells out to
  `xclip` or `xsel`; if neither is installed the flag prints a warning and the
  query is still printed to stdout. macOS uses `pbcopy`, Windows the native
  clipboard — neither needs extra setup.
- Everything else (catalogs, game rules) is self-contained in the binary.

The OpenTUI demo (`demos/opentui-tui/`) publishes the CLI with the same AOT
settings via `npm run build:cli` (outputs to the repo `out/` directory); see
its README for build/test/run instructions.

## Web workbench ("Renly's Recipes")

The Blazor WebAssembly app lives in `web/` and embeds the catalogs, so it needs
no data fetch for development:

```sh
dotnet run --project web/web.csproj
```

For the Cloudflare Pages build, `./build.sh` installs the .NET 10 SDK into the
gitignored `out/` directory, publishes the app, and stamps the commit into
`build-info.json`. See [`DEPLOYING-WEB.md`](DEPLOYING-WEB.md) and
[`../web/README.md`](../web/README.md).

## CI / GitHub Releases

`.github/workflows/release.yml` builds all four platforms, runs the test suite,
smoke-tests each native binary, and publishes artifacts. It runs on:

- `push` of a `v*` tag → builds everything and creates a GitHub Release with the
  binaries attached
- manual `workflow_dispatch` → builds without creating a release; the `rid`
  input targets a single platform (e.g. `linux-x64`) to validate cheaply before
  tagging

To cut a release:

```sh
git tag v0.1.0 && git push origin v0.1.0
```

Artifacts per platform are named `poe-regex-<rid>` (`poe-regex-win-x64.exe`,
`poe-regex-linux-x64`, `poe-regex-osx-arm64`, `poe-regex-osx-x64`) plus their
`.pdb`/`.dbg` symbol files.
