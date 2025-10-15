@echo off
if "%~2"=="" ( echo Usage: convert.bat ^<input^> ^<output wav^> & exit /b 1 )
ffmpeg -y -hide_banner -loglevel error -i "%~1" -ac 1 -ar 48000 -vn -map_metadata -1 -sample_fmt s16 "%~2"
if errorlevel 1 ( echo Conversion failed. & exit /b 1 ) else ( echo Converted -> %~2 )
