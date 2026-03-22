import discord
from discord.ext import commands
import json
import os

def load_config():
    with open("config.json", "r") as f:
        return json.load(f)

config = load_config()
PREFIX = config["prefix"]

intents = discord.Intents.default()
intents.message_content = True
intents.guilds = True
intents.guild_messages = True

bot = commands.Bot(command_prefix=PREFIX, intents=intents, help_command=None)

@bot.event
async def on_ready():
    print(f"{bot.user} is connected and ready!")

async def main():
    token = os.environ.get("PEEB_TOKEN_DEV")
    if not token:
        raise ValueError("PEEB_TOKEN_DEV environment variable not set")
    async with bot:
        bot.load_extension("cogs.basic")
        bot.load_extension("cogs.reactions")
        bot.load_extension("cogs.reminders")
        await bot.start(token)

if __name__ == "__main__":
    import asyncio
    asyncio.run(main())