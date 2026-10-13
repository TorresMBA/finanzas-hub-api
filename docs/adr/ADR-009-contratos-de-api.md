# ADR-009: Contratos de API

- Estado: sustituido en parte por [ADR-019](ADR-019-simplificacion-de-la-plataforma.md). Sigue vigente: el contrato es `openapi.v1.json`, los tipos se generan y el workspace va en la URL. Cambia: el request HTTP vive junto a su controller, en la API
- Fecha: 2026-10-12

## Contexto

El proyecto `Contracts` de V1 era global y solo tenía requests.

## Decisión

Los DTO HTTP viven junto a su endpoint; el contrato con el frontend es `openapi.v1.json` y los tipos se generan; workspace en la URL.

## Alternativas consideradas

Proyecto `Contracts` global; DTO compartidos entre módulos; workspace por cabecera.

## Consecuencias

Sin dependencia global. Un test protege el contrato.
