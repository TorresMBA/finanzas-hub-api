# ADR-002: Comunicación entre módulos

- Estado: sustituido en parte por [ADR-019](ADR-019-simplificacion-de-la-plataforma.md). Sigue vigente: llamadas síncronas al contrato e interfaces de extensión. Cambia: las reacciones son eventos de dominio en proceso, sin outbox
- Fecha: 2026-10-12

## Contexto

Hay operaciones que escriben en dos módulos y reacciones que pueden esperar.

## Decisión

Llamada síncrona al contrato para lecturas y escrituras atómicas hacia aguas arriba; eventos de integración por outbox para reacciones; interfaces de extensión para invertir dependencias.

## Alternativas consideradas

Solo eventos (consistencia eventual en todo); mediador en proceso; referencias directas.

## Consecuencias

Grafo acíclico. Atomicidad sin sagas. Los contratos pasan a ser API estable.
