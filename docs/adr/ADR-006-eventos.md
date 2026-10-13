# ADR-006: Eventos

- Estado: sustituido por [ADR-019](ADR-019-simplificacion-de-la-plataforma.md)
- Fecha: 2026-10-12

## Contexto

Se necesita reaccionar entre módulos de forma fiable.

## Decisión

Eventos de dominio internos y mínimos; eventos de integración tipados por outbox; inbox por consumidor; mapeador explícito.

## Alternativas consideradas

Publicar todo como integración; bus externo; sin eventos.

## Consecuencias

Entrega al menos una vez; consumidores idempotentes; latencia de segundos en reacciones.
