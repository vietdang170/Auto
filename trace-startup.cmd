@echo off
rem Opens an isolated copy of V-TOOL that measures its own startup and writes startup-trace.txt.
cd /d "%~dp0"
echo Bam mo V-TOOL.exe luc %TIME%>> startup-trace.txt
start "" /wait V-TOOL.exe /trace
