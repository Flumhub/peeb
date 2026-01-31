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
        
        // Recurring reminder properties
        public bool IsRecurring { get; set; } = false;
        public RecurrenceType RecurrenceType { get; set; } = RecurrenceType.None;
        public int RecurrenceInterval { get; set; } = 1; // Every X days/weeks/months
        public DateTime? RecurrenceEndDate { get; set; } // Optional end date
        public int TriggerCount { get; set; } = 0; // How many times it has triggered
        public int? MaxTriggers { get; set; } // Optional max triggers
        
        // For weekly reminders - which days of the week
        public List<DayOfWeek> RecurrenceDays { get; set; } = new();
        
        // For monthly reminders - which day of the month (1-31, or -1 for last day)
        public int? MonthlyDay { get; set; }
        
        // For monthly reminders - which week and day (e.g., "first Monday", "last Friday")
        public WeekOfMonth? WeekOfMonth { get; set; }
        public DayOfWeek? WeeklyDayOfWeek { get; set; }

        // Server reminder properties (no ping, rich embed)
        public bool IsServerReminder { get; set; } = false;
        public string? ImageUrl { get; set; }
    }

    public enum RecurrenceType
    {
        None,
        Daily,
        Weekly,
        Monthly
    }

    public enum WeekOfMonth
    {
        First = 1,
        Second = 2,
        Third = 3,
        Fourth = 4,
        Last = -1
    }

    public class ReminderData
    {
        public List<Reminder> Reminders { get; set; } = new();
    }
}
