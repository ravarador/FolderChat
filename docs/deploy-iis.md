# Running FolderChat under IIS

FolderChat normally runs with `dotnet run` or by starting `FolderChat.Web.exe`. On a Windows server you can host it in IIS instead, so it starts with Windows and restarts if it stops. This guide sets it up on port 83; use any free port.

FolderChat has no sign-in yet. Out of the box it only answers requests addressed to `localhost`, so it stays usable only from the server itself. Read [Opening FolderChat to other devices](configuration.md#opening-folderchat-to-other-devices-not-recommended-yet) before changing that.

## What's different under IIS

- **IIS chooses the port.** The site's binding decides where FolderChat listens. `Urls` in `appsettings.json` is ignored, and so is `FolderChat:Api:Urls`: to serve the API on another port, add a second binding to the site instead.
- **FolderChat runs as the app pool's account**, `IIS AppPool\<pool name>`, not as you. By default it would keep its data in that account's app-data folder, which usually doesn't exist or can't be written. Give it a data folder of its own (step 3).
- **The encryption keys need a user profile.** On Windows, FolderChat protects its key ring with DPAPI for the account it runs as, which only works when the app pool loads that account's profile (step 4).
- **The folder window can't open.** IIS has no desktop, so **Choose folder…** asks for a path instead. Paste the folder's full path, and give the app pool's account read access to it (step 6).

## 1. Install the ASP.NET Core Hosting Bundle on the server

FolderChat targets .NET 10. Install IIS first, then the **ASP.NET Core 10 Runtime – Windows Hosting Bundle** from <https://dotnet.microsoft.com/download/dotnet/10.0>. It installs the .NET runtime and the ASP.NET Core Module that lets IIS start the app.

Restart IIS afterwards, from an elevated Command Prompt:

```bat
net stop was /y
net start w3svc
```

Check it worked:

```bat
dotnet --list-runtimes
```

The list should include `Microsoft.AspNetCore.App 10.0.x`.

## 2. Publish FolderChat

On a computer with the .NET 10 SDK, from the repository folder:

```bash
dotnet publish src/FolderChat.Web -c Release -o publish
```

Copy the contents of `publish` to the server, for example to `C:\inetpub\FolderChat`. The folder includes a `web.config` that tells IIS how to start `FolderChat.Web.exe`.

## 3. Make a data folder

FolderChat keeps its database, encryption keys and logs in one folder. Make one outside the site folder, so publishing again never touches it:

```bat
mkdir C:\FolderChatData
```

## 4. Create the app pool and the site

From an elevated Command Prompt:

```bat
cd /d %windir%\system32\inetsrv

appcmd add apppool /name:FolderChat /managedRuntimeVersion:"" /processModel.loadUserProfile:true /processModel.idleTimeout:00:00:00 /startMode:AlwaysRunning
appcmd set config -section:system.applicationHost/applicationPools /+"[name='FolderChat'].environmentVariables.[name='FolderChat__DataDirectory',value='C:\FolderChatData']" /commit:apphost

appcmd add site /name:FolderChat /physicalPath:C:\inetpub\FolderChat /bindings:http/*:83:
appcmd set app "FolderChat/" /applicationPool:FolderChat
```

What these settings do:

| Setting | Why |
|---|---|
| `managedRuntimeVersion:""` | **No Managed Code**. The ASP.NET Core Module runs the app, not the old .NET Framework. |
| `loadUserProfile:true` | Lets DPAPI protect FolderChat's encryption keys. |
| `idleTimeout:00:00:00`, `startMode:AlwaysRunning` | Keeps FolderChat running, so it keeps checking knowledge folders for changes every five minutes. |
| `FolderChat__DataDirectory` | Where the database, keys and logs go. Set on the app pool, so publishing again doesn't reset it. |

If you prefer IIS Manager, the same settings are under **Application Pools > FolderChat > Basic Settings** (.NET CLR version: **No Managed Code**) and **Advanced Settings** (**Load User Profile**, **Idle Time-out**, **Start Mode**).

If `appcmd add site` fails, or the site won't start, check that nothing else uses the port: `netstat -ano | findstr :83`.

## 5. Let the app pool write to the data folder

```bat
icacls C:\FolderChatData /grant "IIS AppPool\FolderChat:(OI)(CI)M"
```

Then open <http://localhost:83> on the server.

## 6. Let it read your knowledge folders

FolderChat reads knowledge folders as the app pool's account. Grant it read access to each one:

```bat
icacls D:\Manuals /grant "IIS AppPool\FolderChat:(OI)(CI)RX"
```

## Updating

Stop the app pool, copy the new `publish` folder over the old one, and start it again. Database changes are applied when FolderChat starts.

```bat
%windir%\system32\inetsrv\appcmd stop apppool /apppool.name:FolderChat
rem copy the new files into C:\inetpub\FolderChat
%windir%\system32\inetsrv\appcmd start apppool /apppool.name:FolderChat
```

## Moving data from your own computer

You can copy `folderchat.db` from your own `%LOCALAPPDATA%\FolderChat` into the server's data folder, and your conversations and folders come with it. Saved provider API keys don't: they're encrypted for your Windows account on your computer. Paste them again in **Settings** on the server. Don't copy the `keys` folder.

## When it doesn't start

**HTTP Error 500.30 - ASP.NET Core app failed to start** means IIS started FolderChat and it stopped while starting up. To see why:

1. **Event Viewer.** Open **Windows Logs > Application** and look for errors from **IIS AspNetCore Module V2** or **.NET Runtime**. The message usually includes the exception.
2. **FolderChat's own log**, in `C:\FolderChatData\logs`, if it got far enough to make the folder.
3. **The startup log.** In `C:\inetpub\FolderChat\web.config`, set `stdoutLogEnabled="true"` and `stdoutLogFile=".\logs\stdout"`, make a `logs` folder next to it, and grant the app pool write access to it (`icacls C:\inetpub\FolderChat\logs /grant "IIS AppPool\FolderChat:(OI)(CI)M"`). Load the page again and read the newest file in `logs`. Turn it off when you're done: the file isn't trimmed.
4. **Run it yourself.** In the site folder, run `FolderChat.Web.exe --FolderChat:DataDirectory=C:\FolderChatData`. Errors print in the console. If it starts for you but not under IIS, the cause is the app pool's account: check steps 4 and 5.

Common causes:

| Error | Fix |
|---|---|
| `UnauthorizedAccessException` or `Access to the path … is denied` | The app pool can't write to its data folder. Set `FolderChat__DataDirectory` (step 4) and grant access (step 5). |
| `SQLite Error 14: 'unable to open database file'` | The same: the data folder is missing or not writable. |
| A `DPAPI` or `CryptographicException` error | **Load User Profile** is off for the app pool (step 4). |
| HTTP Error 500.31, or "framework not found" | The .NET 10 runtime isn't installed. Install the Hosting Bundle (step 1). |
| HTTP Error 500.19 | The ASP.NET Core Module isn't installed. Install the Hosting Bundle (step 1) and restart IIS. |
| HTTP Error 400 Bad Request | You opened it by a name that isn't in `AllowedHosts`, such as the server's name. See [Configuration](configuration.md). |
