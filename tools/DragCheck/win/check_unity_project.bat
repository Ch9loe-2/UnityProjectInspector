@echo off
rem ============================================================
rem UnityProjectInspector 一键检查（Windows 小白版）
rem
rem 用法：把 Unity 工程文件夹 或 工程 .zip 压缩包
rem       直接拖到这个 .bat 文件上，松开即可。
rem 流程：解压 → 校验 → 自动生成检查方案 → 检查
rem       → 自动打开 HTML 体检报告
rem ============================================================
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ==========================================
echo  UnityProjectInspector 一键检查（Windows）
echo ==========================================

set "INPUT=%~1"
if "%INPUT%"=="" (
  echo 用法：把 Unity 工程文件夹或 .zip 压缩包拖到这个 bat 上
  pause
  exit /b 2
)

set "WORKDIR=%TEMP%\uip_check_%RANDOM%"
mkdir "%WORKDIR%" 2>nul
set "PROJECT="

rem ---------- 1. 解压 / 定位工程目录 ----------
if exist "%INPUT%\" (
  set "PROJECT=%INPUT%"
) else (
  echo [1/4] 正在解压 zip...
  powershell -NoProfile -Command "try { Expand-Archive -Force -LiteralPath '%INPUT%' -DestinationPath '%WORKDIR%' -ErrorAction Stop; exit 0 } catch { Write-Error $_; exit 1 }" >nul 2>&1
  if errorlevel 1 (
    echo 解压失败：%INPUT%
    pause
    exit /b 2
  )
  rem 兼容 zip 内多套一层目录的情况
  set "PROJECT="
  for /d %%D in ("%WORKDIR%\*") do (
    if exist "%%D\Assets" if exist "%%D\ProjectSettings" if exist "%%D\Packages" set "PROJECT=%%D"
  )
  if not defined PROJECT (
    if exist "%WORKDIR%\Assets" set "PROJECT=%WORKDIR%"
  )
)

rem ---------- 2. 校验是否是有效 Unity 工程 ----------
if not defined PROJECT (
  echo 不是有效 Unity 工程（缺少 Assets / ProjectSettings / Packages）
  pause
  exit /b 2
)
if not exist "%PROJECT%\Assets" (
  echo 不是有效 Unity 工程（缺少 Assets 目录）
  pause
  exit /b 2
)

rem ---------- 3. 自动生成检查方案 ----------
echo [2/4] 自动生成检查方案...
set "ASSIGNMENT=%WORKDIR%\auto-assignment.json"
"%~dp0unityprojectinspector.exe" inspect --project "%PROJECT%" --generate-assignment "%ASSIGNMENT%" > "%WORKDIR%\gen.out" 2>&1
if errorlevel 1 (
  echo 自动生成检查方案失败
  type "%WORKDIR%\gen.out"
  pause
  exit /b 2
)

rem ---------- 4. 运行检查，生成健康报告 ----------
echo [3/4] 正在检查（大工程可能需要几秒到几十秒）...
set "REPORT=%TEMP%\Unity工程体检报告_%RANDOM%.html"
"%~dp0unityprojectinspector.exe" inspect --project "%PROJECT%" --assignment "%ASSIGNMENT%" --health-report "%REPORT%" --format chinese > "%WORKDIR%\cli.out" 2>&1
set "CLI_EXIT=%errorlevel%"
if %CLI_EXIT% GEQ 3 (
  echo 检查工具运行失败（exit %CLI_EXIT%）
  type "%WORKDIR%\cli.out"
  pause
  exit /b %CLI_EXIT%
)
type "%WORKDIR%\cli.out"

if not exist "%REPORT%" (
  echo 生成报告失败
  pause
  exit /b 2
)

echo [4/4] 正在打开体检报告...
start "" "%REPORT%"
echo 检查完成，报告已打开。
pause
exit /b %CLI_EXIT%
