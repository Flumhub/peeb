using Discord;
using Discord.WebSocket;
using DiscordBot.Core.Interfaces;
using DiscordBot.Plugins.ReminderPlugin.Services;
using DiscordBot.Plugins.ReminderPlugin.Models;
using System.Text.RegularExpressions;

namespace DiscordBot.Plugins.ReminderPlugin.Commands
{
    public class ChannelReminderCommand : ICommandHandler
    {
        public string Command => "channelreminder";
        public string Description => "Set a recurring channel reminder (no ping)";
        public string Usage => "channelreminder every [interval] at [time] [timezone] [message]";

        private readonly ReminderService _reminderService;

        private static readonly Dictionary<string, TimeSpan> TimezoneOffsets = new(StringComparer.OrdinalIgnoreCase)
        {
            // UTC offsets
            {"UTC", TimeSpan.Zero},
            {"UTC+0", TimeSpan.Zero},
            {"UTC-0", TimeSpan.Zero},
            {"UTC+1", TimeSpan.FromHours(1)},
            {"UTC+2", TimeSpan.FromHours(2)},
            {"UTC+3", TimeSpan.FromHours(3)},
            {"UTC+4", TimeSpan.FromHours(4)},
            {"UTC+5", TimeSpan.FromHours(5)},
            {"UTC+6", TimeSpan.FromHours(6)},
            {"UTC+7", TimeSpan.FromHours(7)},
            {"UTC+8", TimeSpan.FromHours(8)},
            {"UTC+9", TimeSpan.FromHours(9)},
            {"UTC+10", TimeSpan.FromHours(10)},
            {"UTC+11", TimeSpan.FromHours(11)},
            {"UTC+12", TimeSpan.FromHours(12)},
            {"UTC-1", TimeSpan.FromHours(-1)},
            {"UTC-2", TimeSpan.FromHours(-2)},
            {"UTC-3", TimeSpan.FromHours(-3)},
            {"UTC-4", TimeSpan.FromHours(-4)},
            {"UTC-5", TimeSpan.FromHours(-5)},
            {"UTC-6", TimeSpan.FromHours(-6)},
            {"UTC-7", TimeSpan.FromHours(-7)},
            {"UTC-8", TimeSpan.FromHours(-8)},
            {"UTC-9", TimeSpan.FromHours(-9)},
            {"UTC-10", TimeSpan.FromHours(-10)},
            {"UTC-11", TimeSpan.FromHours(-11)},
            {"UTC-12", TimeSpan.FromHours(-12)},
            // Common timezone abbreviations
            {"PST", TimeSpan.FromHours(-8)},
            {"PDT", TimeSpan.FromHours(-7)},
            {"MST", TimeSpan.FromHours(-7)},
            {"MDT", TimeSpan.FromHours(-6)},
            {"CST", TimeSpan.FromHours(-6)},
            {"CDT", TimeSpan.FromHours(-5)},
            {"EST", TimeSpan.FromHours(-5)},
            {"EDT", TimeSpan.FromHours(-4)},
            {"GMT", TimeSpan.Zero},
            {"CET", TimeSpan.FromHours(1)},
            {"CEST", TimeSpan.FromHours(2)},
            {"JST", TimeSpan.FromHours(9)},
            {"KST", TimeSpan.FromHours(9)},
            {"AEST", TimeSpan.FromHours(10)},
            {"AEDT", TimeSpan.FromHours(11)},
        };

        public ChannelReminderCommand(ReminderService reminderService)
        {
            _reminderService = reminderService;
        }

        public async Task<bool> HandleAsync(SocketMessage message, string[] args)
        {
            if (args.Length < 2 || !args[1].Equals("every", StringComparison.OrdinalIgnoreCase))
            {
                await message.Channel.SendMessageAsync($"Usage: `{Usage}`\n\n" +
                    "Examples:\n" +
                    "• `peeb channelreminder every day at 17:00 Check in!`\n" +
                    "• `peeb channelreminder every day at 09:00 UTC-8 Good morning!`\n" +
                    "• `peeb channelreminder every day at 14:30 PST Lunch time`\n" +
                    "• `peeb channelreminder every week on monday at 10:00 Weekly reset!`\n\n" +
                    "Supported timezones: UTC, UTC+1 to UTC+12, UTC-1 to UTC-12, PST, EST, CST, GMT, CET, JST, etc.");
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

            // Rejoin the full command after "channelreminder every"
            var fullInput = string.Join(" ", args.Skip(2));
            Console.WriteLine($"[DEBUG] ChannelReminder parsing: '{fullInput}'");
            
            var parseResult = ParseChannelReminderInput(fullInput);

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
                    await message.Channel.SendMessageAsync("❌ Channel reminders can only be set in server channels!");
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

                var tzInfo = parseResult.Timezone != null ? $" ({parseResult.Timezone})" : "";
                await message.Channel.SendMessageAsync(result + tzInfo);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to add channel reminder: {ex.Message}");
                await message.Channel.SendMessageAsync("❌ Failed to set channel reminder. Please try again.");
                return true;
            }
        }

        private (bool Success, DateTime? FirstTrigger, string Message, RecurrenceType RecurrenceType,
                 int Interval, List<DayOfWeek>? RecurringDays, int? MonthlyDay, WeekOfMonth? WeekOfMonth,
                 DayOfWeek? WeeklyDayOfWeek, string? Timezone, string Error) ParseChannelReminderInput(string input)
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
                return (false, null, "", RecurrenceType.None, 0, null, null, null, null, null,
                       "Must specify 'day', 'week', or 'month'. Example: 'every day at 17:00 message'");

            // Find "at" position and extract time + timezone + message
            var atIndex = lowerInput.IndexOf(" at ");
            if (atIndex == -1)
            {
                return (false, null, "", RecurrenceType.None, 0, null, null, null, null, null,
                       "Missing 'at' keyword. Example: 'every day at 17:00 message'");
            }

            // Everything after "at " is time + optional timezone + message
            var afterAt = input.Substring(atIndex + 4).Trim();
            
            // Extract time, timezone, and message
            var (timeStr, timezone, messageStr) = ExtractTimeTimezoneAndMessage(afterAt);
            
            if (string.IsNullOrEmpty(timeStr))
            {
                return (false, null, "", RecurrenceType.None, 0, null, null, null, null, null,
                       "Could not find time. Use 24-hour format like '17:00' or '09:30'");
            }

            Console.WriteLine($"[DEBUG] Extracted time: '{timeStr}', timezone: '{timezone}', message: '{messageStr}'");

            // Parse the time (24-hour format)
            var timeParsed = ParseTimeString(timeStr);
            if (!timeParsed.Success)
            {
                return (false, null, "", RecurrenceType.None, 0, null, null, null, null, null,
                       $"Could not parse time '{timeStr}'. Use 24-hour format like '17:00' or '09:30'.");
            }

            // Get interval
            int interval = 1;
            var intervalMatch = Regex.Match(lowerInput, @"^(\d+)\s+(?:days?|weeks?|months?)");
            if (intervalMatch.Success)
            {
                interval = int.Parse(intervalMatch.Groups[1].Value);
            }

            // Calculate first trigger with timezone adjustment
            var localTime = timeParsed.Time!.Value;
            var firstTrigger = CalculateFirstTriggerWithTimezone(localTime, timezone);

            // For weekly, parse days
            List<DayOfWeek>? recurringDays = null;
            if (recurrenceType == RecurrenceType.Weekly)
            {
                recurringDays = ParseWeekDays(lowerInput);
                if (recurringDays.Any())
                {
                    var targetDay = recurringDays.First();
                    var daysUntil = ((int)targetDay - (int)DateTime.Now.DayOfWeek + 7) % 7;
                    if (daysUntil == 0 && DateTime.Now.TimeOfDay >= (firstTrigger - DateTime.Today).Duration())
                        daysUntil = 7;
                    firstTrigger = DateTime.Today.AddDays(daysUntil).Add(firstTrigger.TimeOfDay);
                }
            }

            // For monthly, parse day specification
            int? monthlyDay = null;
            WeekOfMonth? weekOfMonth = null;
            DayOfWeek? weeklyDayOfWeek = null;
            if (recurrenceType == RecurrenceType.Monthly)
            {
                (monthlyDay, weekOfMonth, weeklyDayOfWeek) = ParseMonthlySpec(lowerInput);
                firstTrigger = CalculateFirstMonthlyTrigger(DateTime.Now, firstTrigger.TimeOfDay, monthlyDay, weekOfMonth, weeklyDayOfWeek);
            }

            var finalMessage = string.IsNullOrWhiteSpace(messageStr) ? "Channel Reminder" : messageStr;

            return (true, firstTrigger, finalMessage, recurrenceType, interval, recurringDays, monthlyDay, weekOfMonth, weeklyDayOfWeek, timezone, "");
        }

        private (string TimeStr, string? Timezone, string Message) ExtractTimeTimezoneAndMessage(string input)
        {
            // Pattern: TIME [TIMEZONE] MESSAGE
            // Time is 24-hour format: HH:MM
            var match = Regex.Match(input, @"^(\d{1,2}:\d{2})\s+(?:(UTC[+-]?\d{0,2}|[A-Z]{2,4})\s+)?(.*)$", RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                var time = match.Groups[1].Value;
                var tz = match.Groups[2].Success && !string.IsNullOrEmpty(match.Groups[2].Value) ? match.Groups[2].Value : null;
                var msg = match.Groups[3].Value.Trim();
                return (time, tz, msg);
            }

            // Fallback: just try to get time
            var simpleParts = input.Split(' ', 2);
            if (simpleParts.Length >= 1)
            {
                return (simpleParts[0], null, simpleParts.Length > 1 ? simpleParts[1] : "");
            }

            return ("", null, input);
        }

        private DateTime CalculateFirstTriggerWithTimezone(TimeSpan localTime, string? timezone)
        {
            DateTime firstTrigger;

            if (timezone != null && TimezoneOffsets.TryGetValue(timezone.ToUpper(), out var offset))
            {
                // Convert from specified timezone to server local time
                var serverOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
                var adjustment = serverOffset - offset;
                
                var adjustedTime = localTime.Add(adjustment);
                
                // Handle day wrap
                if (adjustedTime.TotalHours >= 24)
                {
                    adjustedTime = adjustedTime.Subtract(TimeSpan.FromHours(24));
                    firstTrigger = DateTime.Today.AddDays(1).Add(adjustedTime);
                }
                else if (adjustedTime.TotalHours < 0)
                {
                    adjustedTime = adjustedTime.Add(TimeSpan.FromHours(24));
                    firstTrigger = DateTime.Today.AddDays(-1).Add(adjustedTime);
                }
                else
                {
                    firstTrigger = DateTime.Today.Add(adjustedTime);
                }
            }
            else
            {
                // No timezone specified, use server local time
                firstTrigger = DateTime.Today.Add(localTime);
            }

            // If time has passed today, schedule for tomorrow
            if (firstTrigger <= DateTime.Now)
            {
                firstTrigger = firstTrigger.AddDays(1);
            }

            return firstTrigger;
        }

        private (bool Success, TimeSpan? Time) ParseTimeString(string timeStr)
        {
            timeStr = timeStr.Trim();
            
            Console.WriteLine($"[DEBUG] ParseTimeString input: '{timeStr}'");

            // 24-hour format: 14:30, 9:00, 17:00
            var match24 = Regex.Match(timeStr, @"^(\d{1,2}):(\d{2})$");
            if (match24.Success)
            {
                int hour = int.Parse(match24.Groups[1].Value);
                int minute = int.Parse(match24.Groups[2].Value);
                
                Console.WriteLine($"[DEBUG] 24-hour parsed: hour={hour}, minute={minute}");
                
                if (hour >= 0 && hour < 24 && minute >= 0 && minute < 60)
                    return (true, new TimeSpan(hour, minute, 0));
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