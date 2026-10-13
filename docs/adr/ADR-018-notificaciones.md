# ADR-018: Notificaciones

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

Varios canales; no acoplar el dominio a WhatsApp.

## Decisión

Notifications como capacidad aguas arriba; `INotificationChannel` por canal; los módulos publican intenciones.

## Alternativas consideradas

Cada módulo envía por su cuenta; Notifications suscrito a todos los eventos.

## Consecuencias

Un solo lugar para preferencias, silencio y reintentos. WhatsApp requiere plantillas aprobadas.
