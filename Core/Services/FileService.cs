using System.Text.Json;

namespace DiscordBot.Core.Services
{
    public class FileService
    {
        public async Task<T> LoadJsonAsync<T>(string filePath) where T : new()
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string json = await File.ReadAllTextAsync(filePath);
                    return JsonSerializer.Deserialize<T>(json) ?? new T();
                }
                return new T();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to load {filePath}: {ex.Message}");
                return new T();
            }
        }

        public async Task SaveJsonAsync<T>(string filePath, T data)
        {
            try
            {
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to save {filePath}: {ex.Message}");
            }
        }

        public async Task<byte[]> DownloadFileAsync(string url)
        {
            using var httpClient = new HttpClient();
            return await httpClient.GetByteArrayAsync(url);
        }
    }
}