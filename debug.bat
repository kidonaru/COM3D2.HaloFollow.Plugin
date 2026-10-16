@echo off
chcp 65001
setlocal

call .\source\COM3D2.HaloFollow.Plugin\build.bat debug
if %ERRORLEVEL% neq 0 (
    echo ビルドに失敗しました
    exit /b 1
)

echo ビルドに成功しました
exit /b 0
