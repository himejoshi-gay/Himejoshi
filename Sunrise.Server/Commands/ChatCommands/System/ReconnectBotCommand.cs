using Sunrise.Server.Attributes;
using Sunrise.Server.Commands;
using Sunrise.Server.Repositories;
using Sunrise.Shared.Application;
using Sunrise.Shared.Database;
using Sunrise.Shared.Enums.Users;
using Sunrise.Shared.Objects;
using Sunrise.Shared.Objects.Sessions;
using Sunrise.Shared.Repositories;

namespace Sunrise.Server.Commands.ChatCommands.System;

[ChatCommand("reconnectbot", requiredPrivileges: UserPrivilege.SuperUser)]
public class ReconnectBotCommand : IChatCommand
{
    public async Task Handle(Session session, ChatChannel? channel, string[]? args)
    {
        using var scope = ServicesProviderHolder.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<DatabaseService>();
        var sessions = ServicesProviderHolder.GetRequiredService<SessionRepository>();

        var botUser = await database.Users.GetServerBot();

        if (botUser == null)
        {
            ChatCommandRepository.SendMessage(session, "Server bot account was not found in the database.");
            return;
        }

        var existingBotSession = sessions.GetSession(userId: botUser.Id);

        if (existingBotSession != null)
        {
            await sessions.RemoveSession(existingBotSession);
        }

        await sessions.AddBotToSession();

        ChatCommandRepository.SendMessage(session, $"Server bot session restarted for {botUser.Username}.");
    }
}
