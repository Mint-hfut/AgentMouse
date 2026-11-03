@echo off
setlocal
where node >nul 2>nul
if errorlevel 1 (
  echo AgentMouse requires Node.js 18+ x64. Install it from https://nodejs.org and try again.
  pause
  exit /b 1
)
node "%~dp0install.mjs" %*
set "agentmouse_exit=%errorlevel%"
if "%~1"=="" pause
exit /b %agentmouse_exit%
