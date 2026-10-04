@echo off
rem Zips the project for submission into Hakutaku-submission.zip, next to this
rem folder. Leaves out .git and everything .gitignore lists (node_modules, bin/obj,
rem build output, IDE settings, .env secrets). It zips the files as they are on
rem disk right now, so uncommitted changes are included. Needs Git installed.
setlocal
cd /d "%~dp0"

where git >nul 2>&1
if errorlevel 1 (
    echo Git is not installed. It is needed to work out which files to leave out.
    goto :fail
)
git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo This folder is not a Git repository, so there is no .gitignore to go by.
    goto :fail
)

for %%I in ("%~dp0..") do set "OUT=%%~fI\Hakutaku-submission.zip"

rem Stage everything on disk into a throwaway index and zip the tree it describes.
rem It starts as a copy of the real index so the executable bit on the .sh files
rem survives. The real index, what you have staged, is never touched.
for /f "delims=" %%P in ('git rev-parse --git-path index') do set "REAL_INDEX=%%~fP"
set "GIT_INDEX_FILE=%TEMP%\hakutaku-submission-index"
if exist "%REAL_INDEX%" copy /y "%REAL_INDEX%" "%GIT_INDEX_FILE%" >nul
git -c core.safecrlf=false add -A
if errorlevel 1 goto :git_failed
set "TREE="
for /f %%T in ('git write-tree') do set "TREE=%%T"
del "%GIT_INDEX_FILE%" 2>nul
set "GIT_INDEX_FILE="
if not defined TREE goto :git_failed

rem --worktree-attributes applies .gitattributes from disk, which keeps start.sh
rem LF and the .bat files CRLF in the zip even though .gitattributes itself is left
rem out. Git's own files are no use without the repository, and neither is this.
git archive --format=zip --prefix=Hakutaku/ --worktree-attributes -o "%OUT%" %TREE% . ":(exclude,glob)**/.gitignore" ":(exclude,glob)**/.gitattributes" ":(exclude)zip-submission.bat"
if errorlevel 1 goto :git_failed

echo.
echo   Done: %OUT%
echo.
explorer /select,"%OUT%"
pause
exit /b 0

:git_failed
echo.
echo Git could not build the zip, see the error above.
:fail
echo.
pause
exit /b 1
