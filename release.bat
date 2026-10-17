@echo off
chcp 65001
setlocal

set PLUGIN_NAME=COM3D2.HaloFollow.Plugin
set ASSEMBLY_NAME=COM3D2.HaloFollow.Plugin

call .\source\%PLUGIN_NAME%\build.bat release all
if %ERRORLEVEL% neq 0 (
    echo ビルドに失敗しました
    exit /b 1
)

for /f "tokens=*" %%i in ('powershell -NoProfile -Command "$content = Get-Content 'source/%PLUGIN_NAME%/PluginInfo.cs'; $version = [regex]::Match($content, 'PluginVersion = \""(.*?)\""').Groups[1].Value; echo $version"') do set VERSION=%%i
if "%VERSION%"=="" (
    echo PluginInfo.cs から PluginVersion を読み取れませんでした
    exit /b 1
)
echo VERSION: %VERSION%

if exist output rmdir /s /q output

rem ============ COM3D2 版 / COM3D2.5 版を同梱したパッケージ (MIE と同じ構成) ============
rem 中身を Sybaris\UnityInjector\ へコピーしてもらう。COM3D2.5 版は dll だけが違う別フォルダとして同梱する
md output\%PLUGIN_NAME%\UnityInjector
copy /y "UnityInjector\%ASSEMBLY_NAME%.dll" "output\%PLUGIN_NAME%\UnityInjector\" >nul
if %ERRORLEVEL% neq 0 (
    echo COM3D2 版 dll のコピーに失敗しました
    exit /b 1
)

md "output\%PLUGIN_NAME%\UnityInjector (COM3D2.5)"
copy /y "UnityInjector25\%ASSEMBLY_NAME%.dll" "output\%PLUGIN_NAME%\UnityInjector (COM3D2.5)\" >nul
if %ERRORLEVEL% neq 0 (
    echo COM3D2.5 版 dll のコピーに失敗しました
    exit /b 1
)

set README_TXT=output\%PLUGIN_NAME%\README.txt
echo このテキストはWeb上で見ることを推奨しています。 > %README_TXT%
echo https://github.com/kidonaru/%PLUGIN_NAME%/blob/main/README.md >> %README_TXT%
echo. >> %README_TXT%
echo. >> %README_TXT%
type README.md >> %README_TXT%

powershell -NoProfile Compress-Archive -Path "output\%PLUGIN_NAME%" -DestinationPath "output\%PLUGIN_NAME%-v%VERSION%.zip" -Force
if %ERRORLEVEL% neq 0 (
    echo zip の作成に失敗しました
    exit /b 1
)

rmdir /s /q output\%PLUGIN_NAME%

echo ビルドに成功しました
exit /b 0
