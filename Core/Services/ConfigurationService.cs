using Microsoft.Extensions.Configuration;

namespace DiscordBot.Core.Services
{
    public class ConfigurationService
    {
        private readonly IConfiguration _configuration;

        public ConfigurationService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string BotToken => _configuration["Discord:BotToken"] 
            ?? throw new InvalidOperationException("Bot token not found in configuration");

        public string CommandPrefix => _configuration["Discord:CommandPrefix"] ?? "peeb ";
    }
}