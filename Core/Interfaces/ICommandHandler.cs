using Discord.WebSocket;

namespace DiscordBot.Core.Interfaces
{
    public interface ICommandHandler
    {
        string Command { get; }
        string Description { get; }
        string Usage { get; }
        Task<bool> HandleAsync(SocketMessage message, string[] args);
    }
}