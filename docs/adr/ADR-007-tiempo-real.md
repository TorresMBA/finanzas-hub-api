# ADR-007: Tiempo real

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

Solo algunas pantallas lo justifican.

## Decisión

Un hub SignalR; notificaciones y señales `resource.changed` sin datos de negocio; el cliente relee por HTTP.

## Alternativas consideradas

Enviar datos por el socket; sondeo en todo; SSE.

## Consecuencias

La autorización de datos queda en HTTP. Azure SignalR solo al escalar.
