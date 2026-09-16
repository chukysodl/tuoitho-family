$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& dotnet build (Join-Path $root 'src\TuoiTho.BrowserHost\TuoiTho.BrowserHost.csproj') --configuration Release
if($LASTEXITCODE -ne 0){throw 'Không thể build TuoiTho.BrowserHost.'}
$extension=Join-Path $root 'browser-extension'
$host=Join-Path $root 'src\TuoiTho.BrowserHost\bin\Release\net10.0-windows\TuoiTho.BrowserHost.exe'
if(-not(Test-Path $host)){throw 'Không tìm thấy BrowserHost sau build.'}
$id=$env:TUOITHO_EXTENSION_ID
if([string]::IsNullOrWhiteSpace($id)){Write-Host 'CHƯA ĐĂNG KÝ: sau khi Load unpacked, đặt TUOITHO_EXTENSION_ID thành Extension ID rồi chạy lại script để khóa Native Host theo extension đó.';return}
$dir=Join-Path $env:LOCALAPPDATA 'TuoiTho\NativeMessaging';New-Item -ItemType Directory -Force $dir|Out-Null
$manifest=@{name='com.tuoitho.browserhost';description='TuoiTho local browser control';path=$host;type='stdio';allowed_origins=@("chrome-extension://$id/")} | ConvertTo-Json -Depth 3
$chrome=Join-Path $dir 'com.tuoitho.browserhost.chrome.json';$edge=Join-Path $dir 'com.tuoitho.browserhost.edge.json';Set-Content $chrome $manifest -Encoding utf8;Set-Content $edge $manifest -Encoding utf8
reg add 'HKCU\Software\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' /ve /t REG_SZ /d $chrome /f | Out-Null
reg add 'HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' /ve /t REG_SZ /d $edge /f | Out-Null
Write-Host "ĐÃ ĐĂNG KÝ Native Host cho Chrome và Edge.`nThư mục extension: $extension`nMở chrome://extensions hoặc edge://extensions → Developer mode → Load unpacked → chọn thư mục trên."