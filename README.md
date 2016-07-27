# EESpender
A tool that automatically collects login rewards, and spends energy on the newest items in the Energy Shop.

# Using EESpender
All that is necessary is running the process with the given parameters, this can be done with cron (Linux), or Task Scheduler (Windows).
## (crontab) example.sh
```shell
#!/bin/bash
mono EESpender.exe "email@example.com" "password" &
```
