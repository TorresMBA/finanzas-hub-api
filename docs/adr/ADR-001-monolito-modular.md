# ADR-001: Monolito modular

- Estado: sustituido en parte por [ADR-019](ADR-019-simplificacion-de-la-plataforma.md). Sigue vigente: un monolito de 12 módulos y un desplegable de API. Cambia: un proyecto por módulo y fronteras vigiladas por tests
- Fecha: 2026-10-12

## Contexto

~10 usuarios, un desarrollador, necesidad de crecer sin reescribir. V1 era modular solo en carpetas.

## Decisión

12 módulos; dos proyectos por módulo (`Modules.X` interno y `Modules.X.Contracts` público); un desplegable.

## Alternativas consideradas

Microservicios; 4 proyectos por módulo; 1 proyecto por módulo.

## Consecuencias

31 proyectos. Frontera entre módulos por compilación; capas internas por tests de arquitectura.
