# Schema `prototype-first`

OpenSpec workflow that extends the built-in `spec-driven` schema with a
dedicated **prototype** artifact in the propose flow:

```text
proposal → specs → prototype → design → tasks → apply
```

## Why it exists

The PRD and the design doc define behavior, but interface decisions stay
abstract until they become navigable screens. This schema requires changes with
visible impact to arrive at propose with self-contained HTML prototypes derived
from the proposal and specs, so review happens before implementation, the design
builds on the prototype, and apply verifies parity with it.

Changes with no interface impact (backend, infrastructure, tooling, or
documentation) do not create HTML prototypes. They record an explicit
exemption instead, so `design` and `tasks` can still advance.

## Prototype exemption

`design` and `tasks` require the `prototype` artifact, so a change without
interface impact cannot simply omit it. In that case, create
`prototypes/EXEMPT.md` stating the reason:

```md
# Prototype exemption

Reason: <one or two sentences explaining why the change has no user-visible
interface impact.>
```

The exemption counts as a completed prototype artifact: it satisfies the
dependencies and keeps the decision traceable in the change directory. Never
create it for a change with interface impact, and never combine it with HTML
prototypes.

## `prototype` artifact

- Generates `prototypes/**/*.html`, or a single `prototypes/EXEMPT.md` when the
  change has no interface impact.
- Requires `proposal` and `specs`.
- Is created before `design`, which requires and references it; `tasks` depends
  on both.
- **Self-contained:** CSS, JavaScript, icons, fonts, and images live in the file
  itself; it opens over `file://`, with no build, CDN, network, backend, or
  storage.
- **Fictional data:** no real plate, person, document, photo, or amount.
- **Language:** visible text follows the language of the change request or the
  language the user explicitly asks for; `<html lang>` matches it. Code,
  comments, and identifiers stay in English.
- **Grounded:** every screen, control, field, and state traces to a requirement
  or scenario in the delta specs. Reused chrome is presentational only, and a
  missing decision becomes a question or an `[unspecified]` placeholder, never
  an invented feature.
- **Styling:** an explicit library and version from the request wins; otherwise
  the project's existing styling is followed, falling back to plain inline CSS.
  Only styling layers are supported, and the library is vendored into the file
  with no CDN, so `file://` keeps working.
- **Navigable:** in-memory JavaScript represents the interactions the specs
  define, plus only the states a scenario requires or the app already uses.
- **Traceable:** the header maps each covered scenario to the screens,
  controls, and states it drives, and lists reused chrome and open questions.

The starting template is in [`templates/prototype.html`](templates/prototype.html).

## Install and usage

Install it into an OpenSpec project with the repository installer:

```sh
curl -fsSL https://raw.githubusercontent.com/tiagosantini/openspec-schemas/main/scripts/install.sh | sh
```

On Windows / PowerShell:

```powershell
irm https://raw.githubusercontent.com/tiagosantini/openspec-schemas/main/scripts/install.ps1 | iex
```

The installer copies the schema into `openspec/schemas/prototype-first`,
validates it when the `openspec` CLI is available, selects it in
`openspec/config.yaml`, and records the version in
`openspec/schemas/prototype-first/.install.json`. It installs the latest
release by default; `--ref <tag>` / `-Ref <tag>` pins a specific one.

To check for updates and apply them:

```sh
curl -fsSL https://raw.githubusercontent.com/tiagosantini/openspec-schemas/main/scripts/install.sh | sh -s -- --check
curl -fsSL https://raw.githubusercontent.com/tiagosantini/openspec-schemas/main/scripts/install.sh | sh -s -- --upgrade
```

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/tiagosantini/openspec-schemas/main/scripts/install.ps1))) -Check
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/tiagosantini/openspec-schemas/main/scripts/install.ps1))) -Upgrade
```

`--check` exits `0` when current, `1` when an update is available, and `2` when
the latest version cannot be determined. The upgrade refuses to overwrite files
changed locally; `--force` / `-Force` overwrites them after keeping a copy under
`openspec/.schema-backups/`.

Pass `--no-select` / `-NoSelect` to leave the config unchanged, or
`--dir <path>` / `-Dir <path>` to target a project from outside its root.

To install manually, copy `schemas/prototype-first/` into your project's
`openspec/schemas/` directory and select it in `openspec/config.yaml`. To
validate it:

```sh
openspec schema validate prototype-first
```

To inspect an artifact's instructions within a change:

```sh
openspec instructions prototype --change "<change-name>" --json
```

