using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.BuildingBlocks.Application.Events;
using PersonalFinance.BuildingBlocks.Application.Messaging;

namespace PersonalFinance.Infrastructure.Messaging;

public static class HandlerRegistration
{
    /// <summary>
    /// Registers everything a module contributes: its handlers (see <see cref="AddHandlersFromAssembly"/>) and
    /// its application services. A service is any internal class of the module's Application layer that is not
    /// a handler; it is registered as itself and under the interfaces it implements, which is how a module
    /// provides the contracts other modules call.
    /// </summary>
    public static IServiceCollection AddModule(this IServiceCollection services, Assembly assembly)
    {
        services.AddHandlersFromAssembly(assembly);

        var applicationNamespace = assembly.GetName().Name + ".Application";
        foreach (var type in assembly.GetTypes().Where(type =>
                     type is { IsClass: true, IsAbstract: false, IsNested: false, IsNotPublic: true, IsGenericTypeDefinition: false }
                     && type.Namespace?.StartsWith(applicationNamespace, StringComparison.Ordinal) == true
                     && !type.GetInterfaces().Any(IsHandlerContract)))
        {
            services.AddScoped(type);
            foreach (var contract in type.GetInterfaces().Where(contract => contract.Namespace?.StartsWith("PersonalFinance.", StringComparison.Ordinal) == true))
            {
                services.AddScoped(contract, provider => provider.GetRequiredService(type));
            }
        }

        return services;
    }

    private static bool IsHandlerContract(Type contract) =>
        contract.IsGenericType
        && contract.GetGenericTypeDefinition() is var definition
        && (definition == typeof(ICommandHandler<,>) || definition == typeof(IQueryHandler<,>) || definition == typeof(IDomainEventHandler<>));

    /// <summary>
    /// Registers every command handler, query handler and domain event handler of an assembly.
    /// Command handlers are wrapped, outermost first, in logging, transaction and (when idempotent) idempotency.
    /// </summary>
    public static IServiceCollection AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var contract in type.GetInterfaces().Where(contract => contract.IsGenericType))
            {
                var definition = contract.GetGenericTypeDefinition();
                var arguments = contract.GetGenericArguments();
                if (definition == typeof(ICommandHandler<,>))
                {
                    AddCommandHandler(services, contract, type, arguments[0], arguments[1]);
                }
                else if (definition == typeof(IQueryHandler<,>))
                {
                    AddQueryHandler(services, contract, type, arguments[0], arguments[1]);
                }
                else if (definition == typeof(IDomainEventHandler<>))
                {
                    services.AddScoped(contract, type);
                }
            }
        }

        return services;
    }

    private static void AddCommandHandler(IServiceCollection services, Type contract, Type handlerType, Type commandType, Type responseType)
    {
        var typeArguments = new[] { commandType, responseType };
        var logging = typeof(LoggingCommandHandlerDecorator<,>).MakeGenericType(typeArguments);
        var transaction = typeof(TransactionCommandHandlerDecorator<,>).MakeGenericType(typeArguments);
        var idempotency = typeof(IIdempotentCommand).IsAssignableFrom(commandType)
            ? typeof(IdempotencyCommandHandlerDecorator<,>).MakeGenericType(typeArguments)
            : null;

        services.AddScoped(handlerType);
        services.AddScoped(contract, provider =>
        {
            var handler = provider.GetRequiredService(handlerType);
            if (idempotency is not null)
            {
                handler = ActivatorUtilities.CreateInstance(provider, idempotency, handler);
            }

            handler = ActivatorUtilities.CreateInstance(provider, transaction, handler);
            return ActivatorUtilities.CreateInstance(provider, logging, handler);
        });
    }

    private static void AddQueryHandler(IServiceCollection services, Type contract, Type handlerType, Type queryType, Type responseType)
    {
        var logging = typeof(LoggingQueryHandlerDecorator<,>).MakeGenericType(queryType, responseType);
        services.AddScoped(handlerType);
        services.AddScoped(contract, provider =>
            ActivatorUtilities.CreateInstance(provider, logging, provider.GetRequiredService(handlerType)));
    }
}
