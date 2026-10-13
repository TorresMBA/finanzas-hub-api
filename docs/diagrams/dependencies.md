# Dependencias entre proyectos

```mermaid
flowchart LR
    subgraph Hosts
        API["PersonalFinance.Api<br/>controllers por módulo"]
        WRK["PersonalFinance.Worker<br/>jobs por módulo"]
    end
    INF["PersonalFinance.Infrastructure<br/>AppDbContext, pipeline, adaptadores"]
    subgraph Modules["Módulos"]
        MX["Modules.X<br/>Domain, Application, Contracts"]
        MY["Modules.Y<br/>Domain, Application, Contracts"]
    end
    subgraph BB["BuildingBlocks"]
        BA["Application"]
        BD["Domain"]
    end

    API --> INF
    WRK --> INF
    API -->|"comandos y consultas"| MX
    WRK -->|"comandos y consultas"| MX
    INF --> MX
    INF --> MY
    MX -->|"solo Y.Contracts"| MY
    MX --> BA
    MY --> BA
    BA --> BD
    MX -.->|"PROHIBIDO"| INF
    BA -.->|"PROHIBIDO"| MX
    API -.->|"PROHIBIDO: AppDbContext"| INF
```

`Infrastructure` referencia a todos los módulos porque implementa lo que piden (persistencia, auditoría, adaptadores). Es una capa exterior: ningún módulo ni BuildingBlock la referencia.

La flecha entre módulos es una referencia de proyecto completa; que solo se use el namespace `Contracts` lo comprueban los tests de arquitectura.
