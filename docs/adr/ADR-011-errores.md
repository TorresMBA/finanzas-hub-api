# ADR-011: Errores

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

V1 mapeaba toda `DomainException` a 409.

## Decisión

`Result<T>` con `ErrorType`; traducción única a `ProblemDetails`; códigos estables; excepciones solo para lo inesperado.

## Alternativas consideradas

Excepciones por regla de negocio; códigos HTTP ad hoc.

## Consecuencias

Portar dominio de V1 implica convertir `throw` en `Result`.
