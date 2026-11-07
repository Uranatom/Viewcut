@echo off
setlocal
set "VIEWCUT_NODE=node"
where node >nul 2>&1
if errorlevel 1 set "VIEWCUT_NODE=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"
"%VIEWCUT_NODE%" "%~dp0server.mjs"
if errorlevel 1 (
  echo Install Node.js 22 or newer if it is not available.
  pause
)
endlocal
