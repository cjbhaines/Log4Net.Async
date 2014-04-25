src\.nuget\NuGet.exe update -self

mkdir build

C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe src\Log4Net.Async.sln /t:Clean,Rebuild /p:Configuration=Release /fileLogger
src\.nuget\NuGet.exe pack src\Log4Net.Async\Log4Net.Async.nuspec -OutputDirectory build

pause