@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\package.ps1" -Browser Firefox
