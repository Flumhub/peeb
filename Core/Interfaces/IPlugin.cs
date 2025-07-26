using Discord.WebSocket;

namespace DiscordBot.Core.Interfaces
{
    public interface IPlugin
    {
        string Name { get; }
        string Description { get; }
        Task InitializeAsync(DiscordSocketClient client);
        Task<bool> HandleMessageAsync(SocketMessage message);
        Task CleanupAsync();
    }
}