using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DiscordBot.Core;
using DiscordBot.Core.Services;
using DiscordBot.Plugins.BasicPlugin;
using DiscordBot.Plugins.ReactionPlugin;

namespace DiscordBot
{
    class Program
    {
        private ServiceProvider _services = null!;
        private DiscordSocketClient _client = null!;
        private PluginManager _pluginManager = null!;

        static void Main(string[] args)
            => new Program().RunBotAsync().GetAwaiter().GetResult();

        public async Task RunBotAsync()
        {
            // Setup dependency injection
            _services = ConfigureServices();
            
            // Get services
            var configService = _services.GetRequiredService<ConfigurationService>();
            _client = _services.GetRequiredService<DiscordSocketClient>();
            _pluginManager = _services.GetRequiredService<PluginManager>();

            // Register plugins
            _pluginManager.RegisterPlugin(_services.GetRequiredService<BasicPlugin>());
            _pluginManager.RegisterPlugin(_services.GetRequiredService<ReactionPlugin>());

            // Setup event handlers
            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.MessageReceived += MessageReceivedAsync;

            // Initialize plugins
            await _pluginManager.InitializeAllAsync();

            // Start bot
            await _client.LoginAsync(TokenType.Bot, configService.BotToken);
            await _client.StartAsync();

            // Keep running
            await Task.Delay(-1);
        }

        private ServiceProvider ConfigureServices()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds |
                                GatewayIntents.GuildMessages |
                                GatewayIntents.MessageContent
            };

            return new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton<ConfigurationService>()
                .AddSingleton<FileService>()
                .AddSingleton(new DiscordSocketClient(config))
                .AddSingleton<PluginManager>()
                .AddSingleton<BasicPlugin>()
                .AddSingleton<ReactionPlugin>()
                .BuildServiceProvider();
        }

        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        }

        private async Task ReadyAsync()
        {
            Console.WriteLine($"{_client.CurrentUser} is connected and ready!");
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            await _pluginManager.HandleMessageAsync(message);
        }
    }
}