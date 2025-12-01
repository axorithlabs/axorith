@echo off
REM Get version from git tag for the Axorith project
REM Usage: init-version.bat

setlocal enabledelayedexpansion

for /f "tokens=*" %%a in ('git describe --tags --abbrev=0 2^>nul') do (
    set "gitVersion=%%a"
)

if not "!gitVersion!"=="" (
    REM Remove 'v' prefix if present
    set "cleanVersion=!gitVersion:v=!"
    echo Version from git tag: !cleanVersion!
    exit /b 0
) else (
    echo Warning: No git tags found, using default version 0.0.1-alpha
    exit /b 1
)

