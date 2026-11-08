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
    GL --> NT
    SE --> LG
    SE --> WS
    SE --> NT
    IN --> LG
    IN --> CT
    IN --> NT
    IV --> LG
    IV --> CT
    IV --> SC
    IV --> NT
    RP --> LG
    RP --> BD
    RP --> GL
    RP --> SC
    RP --> IV
    RP --> CT
    RP --> SE
```

Una flecha significa "usa el `Contracts` de": para llamarlo o para reaccionar a uno de sus eventos. Una flecha existe solo por un evento: `Categories → Workspaces` (sembrar categorías al completar el onboarding o al unirse un miembro). `Scheduling → Workspaces` sirve para recorrer los espacios en el ciclo del Worker, y `Scheduling → Categories` para leer y validar la categoría de una regla. Que Scheduling pause las reglas de un miembro expulsado, o avise de reglas con una categoría archivada, sigue pendiente ([status.md](../roadmap/status.md), fase 7).

Este grafo es el que declara `ModuleBoundaryTests.AllowedDependencies`.
