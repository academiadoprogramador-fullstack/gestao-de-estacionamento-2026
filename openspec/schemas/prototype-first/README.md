# Schema `prototype-first`

OpenSpec workflow that extends the built-in `spec-driven` schema with a
dedicated **prototype** artifact in the propose flow:

```text
proposal → specs → prototype → design → tasks → apply
```

## Why it exists

The PRD and the design doc define behavior, but interface decisions stay
abstract until they become navigable screens. This schema requires changes with
visible impact to arrive at propose with self-contained HTML prototypes and
fictional data, so review happens before implementation, the design builds on
the prototype, and apply verifies parity with it.

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
- **Navigable:** in-memory JavaScript represents navigation, validation, and the
  empty/loading/error/success/no-permission states.
- **Traceable:** the top of the HTML references the covered
  requirements/scenarios.

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
validates it when the `openspec` CLI is available, and selects it in
`openspec/config.yaml`. Pass `--no-select` / `-NoSelect` to leave the config
unchanged, or `--dir <path>` / `-Dir <path>` to target a project from outside
its root.

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

