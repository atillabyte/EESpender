# EESpender
A tool that automatically collects login rewards, and spends energy on the newest items in the Energy Shop.

[![GitHub issues](https://img.shields.io/github/issues/atillabyte/EESpender.svg)](https://github.com/atillabyte/EESpender/issues)
[![Version](https://img.shields.io/badge/version-2.0.6-blue.svg)](https://github.com/atillabyte/EESpender/releases)

# Using EESpender

All that is necessary is running the process with proper account parameters.

This can be done with Cron (Linux), or Task Scheduler (Windows).

# Examples
## crontab
```shell
0 */2 * * * cd /home/root/eespender/ && mono EESpender.exe "user@example.com" "password" 2>&1 & 
```

## crontab (multiple accounts)
```shell
0 */2 * * * cd /home/root/eespender/ && mono EESpender.exe "user1@example.com" "password" "user2@example.com" "password2" 2>&1 & 
```

# Authentication

This tool makes use of [Rabbit](https://decagon.github.io/Rabbit/) for Player.IO authentication.

The currently supported authentication types are as below,
- SimpleUser
- Facebook
- Kongregate
- ArmorGames
- MouseBreaker
