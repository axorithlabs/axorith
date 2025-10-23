@echo off
echo Packaging Axorith Firefox extension...

REM Define the name of the output zip file
set ZIP_FILE=firefox.zip

REM Change to the directory where the script is located
cd /d "%~dp0"

REM Check if an old zip file exists and delete it
if exist "%ZIP_FILE%" (
    echo Deleting old %ZIP_FILE%...
    del "%ZIP_FILE%"
)

REM Package the necessary files into the zip file using a more compatible PowerShell command.
REM This method gets all files, filters out the target zip file, and then pipes the list
REM to Compress-Archive. This works on older versions of PowerShell included with Windows.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '.' | Where-Object { $_.Name -ne '%ZIP_FILE%' } | Compress-Archive -DestinationPath '%ZIP_FILE%' -Force"

echo.
echo Extension packaged successfully as %ZIP_FILE%
echo You can now install this file in Firefox via about:addons.

REM Pause to see the output, can be removed if not needed
pause
