# MemSweep 构建脚本：用 .NET Framework 自带的 csc 直接编译成单个 exe
# 说明：本机 csc 只支持到 C# 5 语法，源码全部按 C# 5 编写，不要加 /langversion:latest

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out  = Join-Path $root "MemSweep.exe"

if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc" }

$src = @(Get-ChildItem -Path (Join-Path $root "src") -Filter *.cs -Recurse |
         Sort-Object FullName | ForEach-Object { $_.FullName })

Write-Host ("编译 " + $src.Count + " 个源文件 ...")

# 注意：不能把参数数组命名为 $args（PowerShell 自动变量），否则参数会被吞掉
# 注意：数组字面量里的 "前缀" + 路径 必须整体加括号，否则逗号优先级更高会被拆成两个元素
$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/debug-",
    "/codepage:65001",
    ("/win32icon:" + (Join-Path $root "assets\app.ico")),
    ("/win32manifest:" + (Join-Path $root "app.manifest")),
    ("/out:" + $out)
) + $src

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

$f = Get-Item $out
Write-Host ("完成：" + $f.FullName)
Write-Host ("体积：" + [math]::Round($f.Length / 1KB, 1) + " KB")
if ($f.Length -ge 200KB) { Write-Warning "体积超过 200 KB" }