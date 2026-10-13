# Módulos

```mermaid
flowchart BT
    ID["Identity"]
    WS["Workspaces"]
    NT["Notifications"]
    CT["Categories"]
    LG["Ledger"]
    SC["Scheduling"]
    BD["Budgets"]
    GL["Goals"]
    SE["SharedExpenses"]
    IN["Integrations"]
    IV["Investments"]
    RP["Reporting"]

    WS --> ID
    NT --> ID
    CT --> WS
    LG --> CT
    LG --> NT
    SC --> LG
    SC --> NT
    SC --> WS
    SC --> CT
    BD --> LG
    BD --> CT
    BD --> NT
    GL --> LG
    GL --> WS
    GL --> SC
    GL --> NT
    SE --> LG
    SE --> WS
    SE --> NT
    IN --> LG
    IN --> CT
    IN --> NT
    IV --> LG
    IV --> SC
    IV --> NT
    RP --> LG
    RP --> BD
    RP --> GL
    RP --> SC
    RP --> IV
    RP --> CT
```

Una flecha significa "usa el `Contracts` de": para llamarlo o para reaccionar a uno de sus eventos. Tres flechas existen solo por eventos: `Categories → Workspaces` (sembrar categorías al completar el onboarding o al unirse un miembro), `Scheduling → Workspaces` (pausar reglas de un miembro expulsado) y `Scheduling → Categories` (avisar de reglas con una categoría archivada).

Este grafo es el que declara `ModuleBoundaryTests.AllowedDependencies`.
