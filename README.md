# EESpender
A tool that automatically collects login rewards, and spends energy on the newest items in the Energy Shop.

# Using EESpender

All that is necessary is running the process with the given parameters.

This can be done with cron (Linux), or Task Scheduler (Windows).
## crontab
```shell
0 */2 * * * cd /home/root/eespender/ && mono EESpender.exe "email@example.com" "password" 2>&1 & 
```
