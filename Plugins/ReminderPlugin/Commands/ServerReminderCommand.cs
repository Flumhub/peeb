using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Plugins.ReminderPlugin.Services;
using DiscordBot.Plugins.ReminderPlugin.Models;
using System.Text.RegularExpressions;

namespace DiscordBot.Plugins.ReminderPlugin.Commands
{
    public class ServerReminderCommand : ICommandHandler
    {
        public string Command => "serverreminder";
        public string Description => "Set a recurring server-wide reminder";
        public string Usage => "serverreminder every [interval] at [time] [message]";

        private readonly ReminderService _reminderService;

        public ServerReminderCommand(ReminderService reminderService)
        {
            _reminderService = reminderService;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            if (args.Length < 2 || !args[1].Equals("every", StringComparison.OrdinalIgnoreCase))
            {
                await message.Channel.SendMessageAsync($"Usage: `{Usage}`\n\n" +
                    "Examples:\n" +
                    "• `peeb serverreminder every day at 9am Check in to HoYoLAB!`\n" +
                    "• `peeb serverreminder every day at 14:30 Lunch time reminder`\n" +
                    "• `peeb serverreminder every week on monday at 10am Weekly reset!`");
                return true;
            }

            // Get image URL if attached
            string? imageUrl = null;
            if (message.Attachments.Any())
            {
                var attachment = message.Attachments.First();
                if (attachment.ContentType?.StartsWith("image/") == true)
                {
                    imageUrl = attachment.Url;
                }
            }

            // Rejoin the full command after "serverreminder every"
            var fullInput = string.Join(" ", args.Skip(2));
            Console.WriteLine($"[DEBUG] ServerReminder parsing: '{fullInput}'");
            
            var parseResult = ParseServerReminderInput(fullInput);

            if (!parseResult.Success)
            {
                await message.Channel.SendMessageAsync($"❌ {parseResult.Error}");
                return true;
            }

            try
            {
                var guildChannel = message.Channel as SocketGuildChannel;
                if (guildChannel == null)
                {
                    await message.Channel.SendMessageAsync("❌ Server reminders can only be set in server channels!");
                    return true;
                }

                var result = await _reminderService.AddServerReminderAsync(
                    message.Author.Id,
                    message.Channel.Id,
                    guildChannel.Guild.Id,
                    parseResult.FirstTrigger!.Value,
                    parseResult.Message,
                    parseResult.RecurrenceType,
                    parseResult.Interval,
                    parseResult.RecurringDays,
                    parseResult.MonthlyDay,
                    parseResult.WeekOfMonth,
                    parseResult.WeeklyDayOfWeek,
                    imageUrl
                );

                await message.Channel.SendMessageAsync(result);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to add server reminder: {ex.Message}");
                await message.Channel.SendMessageAsync("❌ Failed to set server reminder. Please try again.");
                return true;
            }
        }

        private (bool Success, DateTime? FirstTrigger, string Message, RecurrenceType RecurrenceType,
                 int Interval, List<DayOfWeek>? RecurringDays, int? MonthlyDay, WeekOfMonth? WeekOfMonth,
                 DayOfWeek? WeeklyDayOfWeek, string Error) ParseServerReminderInput(string input)
        {
            var lowerInput = input.ToLower().Trim();

            // Determine recurrence type
            RecurrenceType recurrenceType;
            if (lowerInput.StartsWith("day") || Regex.IsMatch(lowerInput, @"^\d+\s+days?"))
                recurrenceType = RecurrenceType.Daily;
            else if (lowerInput.StartsWith("week") || Regex.IsMatch(lowerInput, @"^\d+\s+weeks?"))
                recurrenceType = RecurrenceType.Weekly;
            else if (lowerInput.StartsWith("month") || Regex.IsMatch(lowerInput, @"^\d+\s+months?"))
                recurrenceType = RecurrenceType.Monthly;
            else
                return (false, null, "", RecurrenceType.None, 0, null, null, null, null,
                       "Must specify 'day', 'week', or 'month'. Example: 'every day at 9am message'");

            // Find "at" position and extract time + message
            var atIndex = lowerInput.IndexOf(" at ");
            if (atIndex == -1)
            {
                return (false, null, "", RecurrenceType.None, 0, null, null, null, null,
                       "Missing 'at' keyword. Example: 'every day at 9am message'");
            }

            // Everything after "at " is time + message
            var afterAt = input.Substring(atIndex + 4).Trim();
            
            // Split into time and message
            var (timeStr, messageStr) = ExtractTimeAndMessage(afterAt);
            
            if (string.IsNullOrEmpty(timeStr))
            {
                return (false, null, "", RecurrenceType.None, 0, null, null, null, null,
                       "Could not find time. Example: 'every day at 9am message'");
            }

            Console.WriteLine($"[DEBUG] Extracted time: '{timeStr}', message: '{messageStr}'");

            // Parse the time
            var timeParsed = ParseTimeString(timeStr);
            if (!timeParsed.Success)
            {
                return (false, null, "", RecurrenceType.None, 0, null, null, null, null,
                       $"Could not parse time '{timeStr}'. Use formats like '9am', '2:30pm', or '14:30'.");
            }

            // Get interval
            int interval = 1;
            var intervalMatch = Regex.Match(lowerInput, @"^(\d+)\s+(?:days?|weeks?|months?)");
            if (intervalMatch.Success)
            {
                interval = int.Parse(intervalMatch.Groups[1].Value);
            }

            // Calculate first trigger
            var firstTrigger = DateTime.Today.Add(timeParsed.Time!.Value);
            if (firstTrigger <= DateTime.Now)
            {
                firstTrigger = firstTrigger.AddDays(1);
            }

            // For weekly, parse days
            List<DayOfWeek>? recurringDays = null;
            if (recurrenceType == RecurrenceType.Weekly)
            {
                recurringDays = ParseWeekDays(lowerInput);
                if (recurringDays.Any())
                {
                    var targetDay = recurringDays.First();
                    var daysUntil = ((int)targetDay - (int)DateTime.Now.DayOfWeek + 7) % 7;
                    if (daysUntil == 0 && firstTrigger.TimeOfDay <= DateTime.Now.TimeOfDay)
                        daysUntil = 7;
                    firstTrigger = DateTime.Today.AddDays(daysUntil).Add(timeParsed.Time!.Value);
                }
            }

            // For monthly, parse day specification
            int? monthlyDay = null;
            WeekOfMonth? weekOfMonth = null;
            DayOfWeek? weeklyDayOfWeek = null;
            if (recurrenceType == RecurrenceType.Monthly)
            {
                (monthlyDay, weekOfMonth, weeklyDayOfWeek) = ParseMonthlySpec(lowerInput);
                firstTrigger = CalculateFirstMonthlyTrigger(DateTime.Now, timeParsed.Time!.Value, monthlyDay, weekOfMonth, weeklyDayOfWeek);
            }

            var finalMessage = string.IsNullOrWhiteSpace(messageStr) ? "Server Reminder" : messageStr;

            return (true, firstTrigger, finalMessage, recurrenceType, interval, recurringDays, monthlyDay, weekOfMonth, weeklyDayOfWeek, "");
        }

        private (string TimeStr, string Message) ExtractTimeAndMessage(string input)
        {
            // Try to match common time patterns at the start
            var patterns = new[]
            {
                @"^(\d{1,2}:\d{2}\s*(?:am|pm))\s+(.*)$",   // 2:30pm message
                @"^(\d{1,2}\s*(?:am|pm))\s+(.*)$",          // 2pm message
                @"^(\d{1,2}:\d{2})\s+(.*)$",                // 14:30 message
                @"^(\d{1,2})\s+(.*)$"                       // 14 message (hour only)
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
                }
            }

            // Fallback: take first word as time
            var parts = input.Split(' ', 2);
            return (parts[0], parts.Length > 1 ? parts[1] : "");
        }

        private (bool Success, TimeSpan? Time) ParseTimeString(string timeStr)
        {
            timeStr = timeStr.Trim().ToLower().Replace(" ", "");
            
            Console.WriteLine($"[DEBUG] ParseTimeString input: '{timeStr}'");

            // 12-hour format: 2pm, 2:30pm
            var match12 = Regex.Match(timeStr, @"^(\d{1,2})(?::(\d{2}))?(am|pm)$", RegexOptions.IgnoreCase);
            if (match12.Success)
            {
                int hour = int.Parse(match12.Groups[1].Value);
                int minute = match12.Groups[2].Success && !string.IsNullOrEmpty(match12.Groups[2].Value) 
                    ? int.Parse(match12.Groups[2].Value) : 0;
                bool isPm = match12.Groups[3].Value.ToLower() == "pm";
                
                if (hour == 12) hour = 0;
                if (isPm) hour += 12;
                
                Console.WriteLine($"[DEBUG] 12-hour parsed: hour={hour}, minute={minute}");
                
                if (hour >= 0 && hour < 24 && minute >= 0 && minute < 60)
                    return (true, new TimeSpan(hour, minute, 0));
            }
            
            // 24-hour format: 14:30
            var match24 = Regex.Match(timeStr, @"^(\d{1,2}):(\d{2})$");
            if (match24.Success)
            {
                int hour = int.Parse(match24.Groups[1].Value);
                int minute = int.Parse(match24.Groups[2].Value);
                
                Console.WriteLine($"[DEBUG] 24-hour parsed: hour={hour}, minute={minute}");
                
                if (hour >= 0 && hour < 24 && minute >= 0 && minute < 60)
                    return (true, new TimeSpan(hour, minute, 0));
            }
            
            // Hour only: 9, 14
            if (int.TryParse(timeStr, out int hourOnly) && hourOnly >= 0 && hourOnly < 24)
            {
                Console.WriteLine($"[DEBUG] Hour-only parsed: hour={hourOnly}");
                return (true, new TimeSpan(hourOnly, 0, 0));
            }
            
            Console.WriteLine($"[DEBUG] Failed to parse time: '{timeStr}'");
            return (false, null);
        }

        private List<DayOfWeek> ParseWeekDays(string input)
        {
            var days = new List<DayOfWeek>();
            var dayMap = new Dictionary<string, DayOfWeek>
            {
                {"sunday", DayOfWeek.Sunday}, {"sun", DayOfWeek.Sunday},
                {"monday", DayOfWeek.Monday}, {"mon", DayOfWeek.Monday},
                {"tuesday", DayOfWeek.Tuesday}, {"tue", DayOfWeek.Tuesday}, {"tues", DayOfWeek.Tuesday},
                {"wednesday", DayOfWeek.Wednesday}, {"wed", DayOfWeek.Wednesday},
                {"thursday", DayOfWeek.Thursday}, {"thu", DayOfWeek.Thursday}, {"thurs", DayOfWeek.Thursday},
                {"friday", DayOfWeek.Friday}, {"fri", DayOfWeek.Friday},
                {"saturday", DayOfWeek.Saturday}, {"sat", DayOfWeek.Saturday}
            };

            foreach (var kvp in dayMap)
            {
                if (input.Contains(kvp.Key) && !days.Contains(kvp.Value))
                {
                    days.Add(kvp.Value);
                }
            }

            if (!days.Any())
            {
                days.Add(DateTime.Now.DayOfWeek);
            }

            return days;
        }

        private (int? MonthlyDay, WeekOfMonth? WeekOfMonth, DayOfWeek? WeeklyDayOfWeek) ParseMonthlySpec(string input)
        {
            if (input.Contains("last day"))
                return (-1, null, null);

            var dayMatch = Regex.Match(input, @"on\s+(?:the\s+)?(\d+)");
            if (dayMatch.Success)
                return (int.Parse(dayMatch.Groups[1].Value), null, null);

            var weekDayMatch = Regex.Match(input, @"(first|second|third|fourth|last)\s+(sun|mon|tue|wed|thu|fri|sat)\w*");
            if (weekDayMatch.Success)
            {
                var week = weekDayMatch.Groups[1].Value switch
                {
                    "first" => WeekOfMonth.First,
                    "second" => WeekOfMonth.Second,
                    "third" => WeekOfMonth.Third,
                    "fourth" => WeekOfMonth.Fourth,
                    "last" => WeekOfMonth.Last,
                    _ => WeekOfMonth.First
                };

                var dayMap = new Dictionary<string, DayOfWeek>
                {
                    {"sun", DayOfWeek.Sunday}, {"mon", DayOfWeek.Monday}, {"tue", DayOfWeek.Tuesday},
                    {"wed", DayOfWeek.Wednesday}, {"thu", DayOfWeek.Thursday}, {"fri", DayOfWeek.Friday},
                    {"sat", DayOfWeek.Saturday}
                };

                var dayPrefix = weekDayMatch.Groups[2].Value;
                if (dayMap.TryGetValue(dayPrefix, out var dow))
                    return (null, week, dow);
            }

            return (1, null, null); // Default to 1st of month
        }

        private DateTime CalculateFirstMonthlyTrigger(DateTime now, TimeSpan targetTime, int? monthlyDay, WeekOfMonth? weekOfMonth, DayOfWeek? weeklyDayOfWeek)
        {
            for (int monthOffset = 0; monthOffset < 2; monthOffset++)
            {
                var targetDate = now.AddMonths(monthOffset);
                var year = targetDate.Year;
                var month = targetDate.Month;

                DateTime candidate;

                if (monthlyDay.HasValue)
                {
                    var day = monthlyDay.Value;
                    if (day == -1)
                        day = DateTime.DaysInMonth(year, month);
                    else if (day > DateTime.DaysInMonth(year, month))
                        day = DateTime.DaysInMonth(year, month);

                    candidate = new DateTime(year, month, day).Add(targetTime);
                }
                else if (weekOfMonth.HasValue && weeklyDayOfWeek.HasValue)
                {
                    candidate = FindWeekDayInMonth(year, month, weekOfMonth.Value, weeklyDayOfWeek.Value, targetTime);
                }
                else
                {
                    candidate = new DateTime(year, month, 1).Add(targetTime);
                }

                if (candidate > now)
                    return candidate;
            }

            var nextMonth = now.AddMonths(1);
            return new DateTime(nextMonth.Year, nextMonth.Month, 1).Add(targetTime);
        }

        private DateTime FindWeekDayInMonth(int year, int month, WeekOfMonth weekOfMonth, DayOfWeek dayOfWeek, TimeSpan time)
        {
            if (weekOfMonth == WeekOfMonth.Last)
            {
                var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                while (lastDay.DayOfWeek != dayOfWeek)
                    lastDay = lastDay.AddDays(-1);
                return lastDay.Add(time);
            }
            else
            {
                var firstDay = new DateTime(year, month, 1);
                var daysToTarget = ((int)dayOfWeek - (int)firstDay.DayOfWeek + 7) % 7;
                var targetDate = firstDay.AddDays(daysToTarget + (((int)weekOfMonth - 1) * 7));

                if (targetDate.Month != month)
                    targetDate = targetDate.AddDays(-7);

                return targetDate.Add(time);
            }
        }
    }
}