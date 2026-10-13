# ADR-015: Autenticación

- Estado: aceptado
- Fecha: 2026-10-12

## Contexto

SPA con datos financieros.

## Decisión

ASP.NET Core Identity con cookie HttpOnly bajo el mismo sitio y antifalsificación.

## Alternativas consideradas

Bearer con refresh; proveedor externo OIDC.

## Consecuencias

Exige servir SPA y API bajo el mismo sitio. OIDC queda abierto.
