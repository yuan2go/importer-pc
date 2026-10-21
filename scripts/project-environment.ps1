# Dot-source before this project's Windows builds and verification. No machine/user
# environment variables are changed; other projects retain their own configuration.
$taskDevelopmentCache='D:\DevCache\importer-pc'
foreach($taskCacheFolder in @('temp','dotnet-home','nuget-http')) {
    New-Item -ItemType Directory -Path (Join-Path $taskDevelopmentCache $taskCacheFolder) -Force | Out-Null
}
$env:TEMP=Join-Path $taskDevelopmentCache 'temp'
$env:TMP=$env:TEMP
$env:DOTNET_CLI_HOME=Join-Path $taskDevelopmentCache 'dotnet-home'
$env:NUGET_HTTP_CACHE_PATH=Join-Path $taskDevelopmentCache 'nuget-http'
$env:NUGET_PACKAGES='D:\DevCache\nuget\packages'
$env:PYTHONUTF8='1'
if(Test-Path -LiteralPath 'D:\tools\dotnet\dotnet.exe') {
    $env:DOTNET_ROOT='D:\tools\dotnet'
    if(($env:PATH -split ';') -notcontains $env:DOTNET_ROOT){$env:PATH=$env:DOTNET_ROOT+';'+$env:PATH}
}
