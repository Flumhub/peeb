using System.Globalization;
using System.Text.RegularExpressions;

namespace DiscordBot.Plugins.ReminderPlugin.Services
{
    public class TimeParser
    {
        private static readonly Dictionary<string, int> MonthNames = new()
        {
            {"january", 1}, {"jan", 1}, {"february", 2}, {"feb", 2}, {"march", 3}, {"mar", 3},
            {"april", 4}, {"apr", 4}, {"may", 5}, {"june", 6}, {"jun", 6}, {"july", 7}, {"jul", 7},
            {"august", 8}, {"aug", 8}, {"september", 9}, {"sep", 9}, {"october", 10}, {"oct", 10},
            {"november", 11}, {"nov", 11}, {"december", 12}, {"dec", 12}
        };

        private static readonly Dictionary<string, int> DayNames = new()
        {
            {"sunday", 0}, {"sun", 0}, {"monday", 1}, {"mon", 1}, {"tuesday", 2}, {"tue", 2},
            {"wednesday", 3}, {"wed", 3}, {"thursday", 4}, {"thu", 4}, {"friday", 5}, {"fri", 5},
            {"saturday", 6}, {"sat", 6}
        };

        public static (bool Success, DateTime? DateTime, string Error) ParseTime(string input)
        {
            var now = DateTime.Now;
            var normalized = input.ToLower().Trim();

            try
            {
                // Try relative time first (in X time)
                if (normalized.StartsWith("in "))
                {
                    return ParseRelativeTime(normalized.Substring(3), now);
                }

                // Try absolute time
                return ParseAbsoluteTime(normalized, now);
            }
            catch (Exception ex)
            {
                return (false, null, $"Error parsing time: {ex.Message}");
            }
        }

        private static (bool Success, DateTime? DateTime, string Error) ParseRelativeTime(string input, DateTime now)
        {
            var timeSpan = TimeSpan.Zero;
            var hasValidValue = false;

            // Match patterns like "2 hours", "30 minutes", "1 day 5 hours 30 minutes"
            var patterns = new Dictionary<string, Func<int, TimeSpan>>
            {
                {@"(\d+)\s*(?:days?|d)\b", days => TimeSpan.FromDays(days)},
                {@"(\d+)\s*(?:hours?|hrs?|h)\b", hours => TimeSpan.FromHours(hours)},
                {@"(\d+)\s*(?:minutes?|mins?|m)\b", minutes => TimeSpan.FromMinutes(minutes)},
                {@"(\d+)\s*(?:seconds?|secs?|s)\b", seconds => TimeSpan.FromSeconds(seconds)}
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(input, pattern.Key, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out int value))
                    {
                        timeSpan = timeSpan.Add(pattern.Value(value));
                        hasValidValue = true;
                    }
                }
            }

            if (!hasValidValue)
            {
                return (false, null, "No valid time values found. Use format like: 'in 2 hours 30 minutes'");
            }

            if (timeSpan.TotalSeconds < 1)
            {
                return (false, null, "Time must be at least 1 second");
            }

            if (timeSpan.TotalDays > 365)
            {
                return (false, null, "Cannot set reminders more than 1 year in the future");
            }

            return (true, now.Add(timeSpan), null);
        }

        private static (bool Success, DateTime? DateTime, string Error) ParseAbsoluteTime(string input, DateTime now)
        {
            // Handle special cases first
            if (input == "tomorrow")
            {
                return (true, now.Date.AddDays(1).AddHours(9), null); // Default to 9 AM
            }

            if (input == "today")
            {
                var todayTime = now.Date.AddHours(now.Hour + 1); // Next hour
                if (todayTime <= now) todayTime = todayTime.AddHours(1);
                return (true, todayTime, null);
            }

            // Try to parse various date/time formats
            var result = TryParseComplexDateTime(input, now);
            if (result.Success)
            {
                return result;
            }

            // Try standard DateTime parsing as fallback
            if (DateTime.TryParse(input, out DateTime parsed))
            {
                // If only date was provided, default to 9 AM
                if (parsed.TimeOfDay == TimeSpan.Zero)
                {
                    parsed = parsed.AddHours(9);
                }

                // If date is in the past, assume next year
                if (parsed < now)
                {
                    if (parsed.Date == now.Date)
                    {
                        // Same day but past time, move to next hour
                        parsed = now.AddHours(1);
                    }
                    else
                    {
                        parsed = parsed.AddYears(1);
                    }
                }

                return (true, parsed, null);
            }

            return (false, null, "Could not parse the date/time. Try formats like: 'tomorrow', 'Dec 25', 'tomorrow at 3pm', 'in 2 hours'");
        }

        private static (bool Success, DateTime? DateTime, string Error) TryParseComplexDateTime(string input, DateTime now)
        {
            var result = now;
            var dateSet = false;
            var timeSet = false;

            // Split on "at" to separate date and time parts
            var parts = input.Split(new[] { " at ", "@" }, StringSplitOptions.RemoveEmptyEntries);
            var datePart = parts[0].Trim();
            var timePart = parts.Length > 1 ? parts[1].Trim() : "";

            // Parse date part
            if (!string.IsNullOrEmpty(datePart))
            {
                var dateResult = ParseDatePart(datePart, now);
                if (dateResult.Success)
                {
                    result = dateResult.DateTime.Value;
                    dateSet = true;
                }
                else if (datePart != "at") // "at" might be part of time parsing
                {
                    return dateResult;
                }
            }

            // Parse time part
            if (!string.IsNullOrEmpty(timePart))
            {
                var timeResult = ParseTimePart(timePart);
                if (timeResult.Success)
                {
                    result = result.Date.Add(timeResult.TimeSpan.Value);
                    timeSet = true;
                }
                else
                {
                    return (false, null, timeResult.Error);
                }
            }
            else if (input.Contains("am") || input.Contains("pm") || Regex.IsMatch(input, @"\d+:\d+"))
            {
                // Try to parse the whole thing as time
                var timeResult = ParseTimePart(input);
                if (timeResult.Success)
                {
                    result = now.Date.Add(timeResult.TimeSpan.Value);
                    if (result <= now) result = result.AddDays(1);
                    timeSet = true;
                }
            }

            if (!dateSet && !timeSet)
            {
                return (false, null, "No valid date or time found");
            }

            // If only time was set and it's in the past, move to tomorrow
            if (timeSet && !dateSet && result <= now)
            {
                result = result.AddDays(1);
            }

            // If only date was set, default to 9 AM
            if (dateSet && !timeSet && result.TimeOfDay == TimeSpan.Zero)
            {
                result = result.AddHours(9);
            }

            return (true, result, null);
        }

        private static (bool Success, DateTime? DateTime, string Error) ParseDatePart(string datePart, DateTime now)
        {
            // Handle day names (next monday, friday, etc.)
            foreach (var day in DayNames)
            {
                if (datePart.Contains(day.Key))
                {
                    var daysUntil = ((day.Value - (int)now.DayOfWeek + 7) % 7);
                    if (daysUntil == 0) daysUntil = 7; // Next week if today
                    return (true, now.Date.AddDays(daysUntil), null);
                }
            }

            // Handle month day (Dec 25, December 25, 25 Dec, etc.)
            var monthDayPattern = @"(?:(\w+)\s+(\d+)|(\d+)\s+(\w+))";
            var match = Regex.Match(datePart, monthDayPattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string monthStr = match.Groups[1].Value + match.Groups[4].Value;
                string dayStr = match.Groups[2].Value + match.Groups[3].Value;

                if (MonthNames.TryGetValue(monthStr.ToLower(), out int month) && 
                    int.TryParse(dayStr, out int day))
                {
                    try
                    {
                        var year = now.Year;
                        var date = new DateTime(year, month, day);
                        
                        // If date is in the past, assume next year
                        if (date < now.Date)
                        {
                            date = date.AddYears(1);
                        }
                        
                        return (true, date, null);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return (false, null, $"Invalid date: {monthStr} {day}");
                    }
                }
            }

            // Handle numeric dates (12/25, 12-25, etc.)
            var numericPattern = @"(\d+)[\/\-](\d+)(?:[\/\-](\d+))?";
            match = Regex.Match(datePart, numericPattern);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int first) && 
                    int.TryParse(match.Groups[2].Value, out int second))
                {
                    var year = now.Year;
                    if (match.Groups[3].Success && int.TryParse(match.Groups[3].Value, out int third))
                    {
                        year = third < 100 ? 2000 + third : third;
                    }

                    try
                    {
                        var date = new DateTime(year, first, second);
                        if (date < now.Date) date = date.AddYears(1);
                        return (true, date, null);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return (false, null, $"Invalid date: {first}/{second}/{year}");
                    }
                }
            }

            return (false, null, $"Could not parse date part: {datePart}");
        }

        private static (bool Success, TimeSpan? TimeSpan, string Error) ParseTimePart(string timePart)
        {
            // Handle 12-hour format (3pm, 3:30pm, 3:30 PM, etc.)
            var twelveHourPattern = @"(\d+)(?::(\d+))?\s*(am|pm)";
            var match = Regex.Match(timePart, twelveHourPattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int hour))
                {
                    int.TryParse(match.Groups[2].Value, out int minute);
                    var isPM = match.Groups[3].Value.ToLower() == "pm";

                    if (hour == 12) hour = 0; // 12 AM = 0, 12 PM will be set to 12 below
                    if (isPM) hour += 12;

                    if (hour >= 0 && hour < 24 && minute >= 0 && minute < 60)
                    {
                        return (true, new TimeSpan(hour, minute, 0), null);
                    }
                }
            }

            // Handle 24-hour format (15:30, 3:30, etc.)
            var twentyFourHourPattern = @"(\d+):(\d+)(?::(\d+))?";
            match = Regex.Match(timePart, twentyFourHourPattern);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int hour) && 
                    int.TryParse(match.Groups[2].Value, out int minute))
                {
                    int.TryParse(match.Groups[3].Value, out int second);

                    if (hour >= 0 && hour < 24 && minute >= 0 && minute < 60 && second >= 0 && second < 60)
                    {
                        return (true, new TimeSpan(hour, minute, second), null);
                    }
                }
            }

            // Handle hour only (just "3", "15", etc.)
            if (int.TryParse(timePart, out int hourOnly))
            {
                if (hourOnly >= 0 && hourOnly < 24)
                {
                    return (true, new TimeSpan(hourOnly, 0, 0), null);
                }
            }

            return (false, null, $"Could not parse time: {timePart}");
        }
    }
}