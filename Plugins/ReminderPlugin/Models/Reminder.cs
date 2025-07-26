namespace DiscordBot.Plugins.ReminderPlugin.Models
{
    public class Reminder
    {
        public string Id { get; set; } = string.Empty;
        public ulong UserId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong GuildId { get; set; }
        public DateTime TriggerTime { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsTriggered { get; set; }
    }

    public class ReminderData
    {
        public List<Reminder> Reminders { get; set; } = new();
    }
}