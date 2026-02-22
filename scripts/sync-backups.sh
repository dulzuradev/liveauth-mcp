#!/bin/bash
# Sync LiveAuth backups from server to local
rsync -avz liveauth@64.225.32.102:/opt/liveauth/backups/ /users/sydney/.openclaw/workspace/LiveAuth/backups/
