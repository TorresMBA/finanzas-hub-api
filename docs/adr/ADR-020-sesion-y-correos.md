# ADR-020: Sesión, CSRF y correos

- Estado: aceptado
- Fecha: 2026-10-13
- Completa a: ADR-015

## Contexto

El ADR-015 decidió ASP.NET Core Identity con una cookie de sesión y "antifalsificación", sin concretar cómo. Al implementar la fase 1 hubo que decidir tres cosas: cómo se usa Identity dentro de un módulo que no puede depender de ASP.NET, cómo se protege la cookie frente a CSRF, y cómo se envían los correos sin que un caso de uso hable con un servidor de correo.

## Decisión

**Identity.** Se usa su almacenamiento estándar sobre el `AppDbContext` único (`AddEntityFrameworkStores`), sin escribir ningún store. `User` hereda de `IdentityUser<Guid>` y vive en el dominio del módulo, y los handlers usan `UserManager`. Los tests de arquitectura permiten al módulo Identity, y solo a él, el namespace `Microsoft.AspNetCore.Identity`; el resto de ASP.NET le sigue prohibido.

**Sesión.** El módulo valida las credenciales y devuelve de quién es la sesión; la cookie la crea el controller. La cookie lleva solo el id del usuario.

**CSRF.** Cookie `SameSite=Lax` y, además, rechazo de toda petición que cambia estado cuyo `Origin` no sea la propia API o un frontend permitido. Sin tokens antifalsificación.

**Correos.** `IEmailSender` no envía: encola en `platform.outgoing_emails` dentro de la transacción del comando. Un job del Worker envía con reintentos y borra el cuerpo al terminar. Sin servidor SMTP configurado escribe archivos `.eml`.

**Inicio de sesión externo.** Solo correo y contraseña. Google y Microsoft quedan en el backlog (OQ-25); la tabla `user_logins` de Identity ya existe.

## Alternativas consideradas

- Un store propio de Identity, o esconder `UserManager` tras una interfaz del módulo: más código para no ganar nada, y lo contrario de "no escribir la autenticación desde cero".
- Tokens antifalsificación (`X-XSRF-TOKEN`): protección equivalente para una API que solo acepta JSON, a cambio de un viaje extra para obtener el token y de llevarlo en cada test.
- Enviar el correo dentro del caso de uso: una caída del servidor de correo haría fallar el registro, y un comando revertido podría haber enviado ya el correo.

## Consecuencias

- El dominio de Identity conoce un tipo de ASP.NET. Es una excepción acotada y probada.
- Una petición sin `Origin` pasa la comprobación. Los navegadores lo envían siempre en las peticiones que cambian estado entre sitios, que es el caso que se defiende.
- Los correos necesitan el Worker en marcha. Si no lo está, esperan en la cola.
- El cuerpo de un correo pendiente, con su enlace de un solo uso, está en la base hasta que se envía.
