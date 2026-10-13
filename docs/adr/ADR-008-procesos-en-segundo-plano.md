# ADR-008: Procesos en segundo plano

- Estado: sustituido por [ADR-019](ADR-019-simplificacion-de-la-plataforma.md)
- Fecha: 2026-10-12

## Contexto

Outbox, webhooks, scheduler y envíos.

## Decisión

`BackgroundService` en el proceso de la API con PostgreSQL como cola; Worker como host opcional.

## Alternativas consideradas

Hangfire; Quartz; Functions; Service Bus.

## Consecuencias

Un contenedor. Todo proceso debe ser idempotente.
