using System.Globalization;
using System.Text;
using PersonalFinance.BuildingBlocks.Application.Events;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Notifications.Contracts;
using PersonalFinance.Modules.Notifications.Contracts.Events;

namespace PersonalFinance.Modules.Integrations.Application;

public enum ChatCommand
{
    Balance,
    Help
}

/// <summary>The commands a person can write by WhatsApp. They only read: nothing is registered by chat.</summary>
public static class ChatCommandParser
{
    /// <summary>Null when the text is not a command.</summary>
    public static ChatCommand? Parse(string text) =>
        text.Trim().Trim('.', '!', '?', '¿', '¡').ToLowerInvariant() switch
        {
            "saldo" or "saldos" or "balance" => ChatCommand.Balance,
            "ayuda" or "help" or "hola" or "menu" or "menú" => ChatCommand.Help,
            _ => null
        };
}

/// <summary>
/// Answers what a person wrote by WhatsApp. It runs as that person, in their personal workspace, so the balances
/// are theirs and nobody else's. The answer goes to the queue of messages, never straight out.
/// </summary>
internal sealed class AnswerChatCommand(ILedgerQueries ledger, IDirectMessenger messenger) : IDomainEventHandler<InboundMessageReceived>
{
    public const int MaxAccounts = 25;

    public const string Help = "Puedes escribirme:\n• *saldo*: lo que tiene cada una de tus cuentas.\n• *ayuda*: este mensaje.\nPor aquí solo se consulta; los movimientos se registran en la aplicación.";

    public async Task HandleAsync(InboundMessageReceived domainEvent, CancellationToken cancellationToken)
    {
        var answer = ChatCommandParser.Parse(domainEvent.Text) switch
        {
            ChatCommand.Balance => Balances(await ledger.GetAccountBalancesAsync(cancellationToken)),
            ChatCommand.Help => Help,
            _ => "No reconozco ese mensaje.\n\n" + Help
        };
        await messenger.ReplyByWhatsAppAsync(domainEvent.UserId, answer, cancellationToken);
    }

    /// <summary>The accounts of the person, without the funds of goals, which belong to the goal.</summary>
    public static string Balances(IReadOnlyList<LedgerAccountBalance> accounts)
    {
        var shown = accounts.Where(account => account.Type != LedgerAccountType.GoalFund).OrderBy(account => account.Currency).ThenBy(account => account.Name).ToList();
        if (shown.Count == 0)
        {
            return "Aún no tienes cuentas registradas.";
        }

        var text = new StringBuilder("Saldos de tus cuentas:");
        foreach (var account in shown.Take(MaxAccounts))
        {
            text.Append(CultureInfo.InvariantCulture, $"\n• {account.Name}: {account.Currency} {account.Balance:N2}");
        }

        if (shown.Count > MaxAccounts)
        {
            text.Append("\n… y más cuentas en la aplicación.");
        }

        return text.ToString();
    }
}
