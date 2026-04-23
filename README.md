# QEA Review Hub

AI-augmented agent review tool for contact-center managers, built on Microsoft Dynamics 365 Customer Service. Managers conduct structured coaching sessions, collect AI-generated insights, and produce personalised improvement plans — all within a single embedded web resource.

## Prerequisites

| Requirement | Notes |
|---|---|
| Dynamics 365 Customer Service | With Omnichannel and Quality Evaluation (QEA) enabled |
| Power Platform Environment | With Dataverse and Power Automate |
| Microsoft Copilot Studio | Three agents must be active (see [Copilot Agents](docs/COPILOT-AGENTS.md)) |
| .NET 4.6.2 SDK | For building the plugin assembly |
| Plugin Registration Tool | For registering Custom APIs |

## Repository structure

```
├── solution/                    Power Platform solution ZIPs (managed + unmanaged)
├── src/                         C# Custom API plugins
├── webresources/
│   ├── html/                    Main web resource HTML (single-file SPA)
│   └── data/                    RESX localization files (en-US, Hebrew)
├── docs/                        Technical documentation
└── .gitignore
```

## Documentation

| Document | Description |
|---|---|
| [Architecture](docs/ARCHITECTURE.md) | System overview, component map, end-to-end flow |
| [Data Model](docs/DATA-MODEL.md) | Custom tables, fields, relationships, option sets |
| [Flows](docs/FLOWS.md) | Power Automate flows — triggers, steps, connections |
| [Plugins](docs/PLUGINS.md) | C# Custom API plugins — purpose, API contract, registration |
| [Copilot Agents](docs/COPILOT-AGENTS.md) | Copilot Studio agents — inputs, outputs, usage |
| [Web Resource](docs/WEB-RESOURCE.md) | HTML SPA — session phases, localization |
| [Deployment](docs/DEPLOYMENT.md) | Step-by-step guide for deploying to a fresh environment |

## Solution versions

| File | Type | Version |
|---|---|---|
| `QEAReviewHub_1_0_0_0.zip` | Unmanaged | 1.0.0.0 |
| `QEAReviewHub_1_0_0_0_managed.zip` | Managed | 1.0.0.0 |

Use **unmanaged** in development environments. Use **managed** in production.
