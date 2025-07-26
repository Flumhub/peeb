using Discord.WebSocket;
using DiscordBot.Core.Interfaces;

namespace DiscordBot.Core
{
    public class PluginManager
    {
        private readonly List<IPlugin> _plugins = new();
        private readonly DiscordSocketClient _client;

        public PluginManager(DiscordSocketClient client)
        {
            _client = client;
        }

        public void RegisterPlugin(IPlugin plugin)
        {
            _plugins.Add(plugin);
        }

        public async Task InitializeAllAsync()
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    await plugin.InitializeAsync(_client);
                    Console.WriteLine($"[INFO] Plugin '{plugin.Name}' initialized successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to initialize plugin '{plugin.Name}': {ex.Message}");
                }
            }
        }

        public async Task<bool> HandleMessageAsync(SocketMessage message)
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    if (await plugin.HandleMessageAsync(message))
                        return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Plugin '{plugin.Name}' error: {ex.Message}");
                }
            }
            return false;
        }

        public async Task CleanupAllAsync()
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    await plugin.CleanupAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to cleanup plugin '{plugin.Name}': {ex.Message}");
                }
            }
        }

        public IEnumerable<IPlugin> GetPlugins() => _plugins.AsReadOnly();
    }
}