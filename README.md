# Header Echo App

A lightweight ASP.NET Core 10 app that echoes all HTTP request headers back as a web page. It is designed to validate that **Microsoft Entra Application Proxy** correctly injects HTTP headers derived from Entra ID token claims — including custom claims injected by a [Custom Claims Provider (CCP)](https://learn.microsoft.com/entra/identity-platform/custom-claims-provider-overview).

The app highlights specific identity headers (UPN, Object ID, and a CCP-injected `favoriteColor` claim) in the UI so you can quickly confirm the end-to-end flow is working.

> **Security note:** This app trusts all incoming headers unconditionally. It **must only be reachable from the App Proxy connector host**. Use a Windows Firewall rule or IIS IP address restriction to enforce this — never expose this app directly to the internet.

---

## How it works

```
User → Entra ID (auth + CCP enrichment) → App Proxy (header injection) → IIS → This App
```

1. The user authenticates with Entra ID.
2. A Custom Claims Provider Azure Function adds a `favoriteColor` claim to the token.
3. App Proxy (header-based SSO) maps token claims to HTTP headers and forwards the request to IIS.
4. This app renders all received headers, highlighting the identity-related ones.

---

## Prerequisites

### On the Windows Server (IIS host)

| Requirement | Notes |
|---|---|
| Windows Server 2016 or later (or Windows 10/11) | IIS must be enabled |
| IIS 10 or later | With the **ASP.NET Core Module (ANCM)** |
| **.NET 10 Hosting Bundle** | Installs the runtime **and** the IIS ANCM module |

> **Critical:** You must install the **.NET 10 Hosting Bundle** — not just the Runtime or SDK. The Hosting Bundle is the only installer that registers the ASP.NET Core Module with IIS.
>
> Download: https://dotnet.microsoft.com/download/dotnet/10.0  
> Look for: **"Hosting Bundle"** under the Windows column.

### On the build machine

| Requirement | Notes |
|---|---|
| .NET 10 SDK | https://dotnet.microsoft.com/download/dotnet/10.0 |
| Git | To clone the repo |

---

## Deployment steps

### 1. Install IIS (if not already enabled)

In an elevated PowerShell session:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-HttpErrors, IIS-HttpLogging, IIS-RequestFiltering, IIS-StaticContent, IIS-DefaultDocument, IIS-ApplicationDevelopment, IIS-ASPNET45, IIS-NetFxExtensibility45, IIS-ISAPIExtensions, IIS-ISAPIFilter -All
```

Or via **Server Manager → Add Roles and Features → Web Server (IIS)**.

### 2. Install the .NET 10 Hosting Bundle

Download `dotnet-hosting-10.x.x-win.exe` from https://dotnet.microsoft.com/download/dotnet/10.0 and run it on the server. After installation, run:

```powershell
iisreset
```

Verify ANCM is registered:

```powershell
Get-WebConfiguration "system.webServer/globalModules/*" | Where-Object { $_.name -like "AspNetCore*" }
```

You should see `AspNetCoreModuleV2` in the output.

### 3. Clone and publish the app

On your **build machine** (or directly on the server if the SDK is installed):

```powershell
git clone https://github.com/JeffBley/header-based-app.git
cd header-based-app
dotnet publish -c Release -o publish
```

This produces a self-contained set of files in the `publish\` folder.

### 4. Copy files to the server

Copy the contents of the `publish\` folder to the server. A common location:

```
C:\inetpub\HeaderEchoApp\publish\
```

### 5. Create the IIS app pool

In an elevated PowerShell session on the server:

```powershell
Import-Module WebAdministration

# Create the app pool — must use "No Managed Code" for ASP.NET Core
New-WebAppPool -Name "HeaderEchoApp"
Set-ItemProperty "IIS:\AppPools\HeaderEchoApp" -Name managedRuntimeVersion -Value ""
```

### 6. Create the IIS site

```powershell
# Replace the port and physical path as needed
New-Website -Name "HeaderEchoApp" `
            -PhysicalPath "C:\inetpub\HeaderEchoApp\publish" `
            -ApplicationPool "HeaderEchoApp" `
            -Port 8080
```

To use port 80 with a specific hostname (e.g. for App Proxy):

```powershell
New-Website -Name "HeaderEchoApp" `
            -PhysicalPath "C:\inetpub\HeaderEchoApp\publish" `
            -ApplicationPool "HeaderEchoApp" `
            -Port 80 `
            -HostHeader "headerapp.yourdomain.com"
```

### 7. Set folder permissions

Grant the app pool identity read access to the publish folder:

```powershell
$acl = Get-Acl "C:\inetpub\HeaderEchoApp\publish"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "IIS AppPool\HeaderEchoApp", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl "C:\inetpub\HeaderEchoApp\publish" $acl
```

### 8. Test the site

```powershell
Invoke-WebRequest http://localhost:8080 -UseBasicParsing | Select-Object StatusCode
```

Expected: `StatusCode: 200`

Browse to the URL — you should see a page listing all HTTP headers received by the app.

---

## Entra Application Proxy configuration

Once the app is accessible on the internal network:

1. In the **Entra admin center**, go to **Enterprise Applications → New application → On-premises application**.
2. Set the **Internal URL** to `http://<server-hostname>:8080` (or port 80 with host header).
3. Set **Pre-authentication** to `Microsoft Entra ID`.
4. Under **Single sign-on → Header-based**, add header mappings:

   | Header name | Source | Claim |
   |---|---|---|
   | `X-MS-CLIENT-PRINCIPAL-NAME` | Attribute | `user.userprincipalname` |
   | `X-MS-CLIENT-PRINCIPAL-ID` | Attribute | `user.objectid` |
   | `X-Favorite-Color` | Attribute | `customClaim.favoriteColor` |

5. Assign users/groups to the application.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| HTTP 500.19 | ANCM not registered in IIS | Install/reinstall the .NET 10 **Hosting Bundle** and run `iisreset` |
| HTTP 500.30 | App fails to start | Check `Event Viewer → Application` for ANCM errors; verify `publish\` folder contains `web.config` |
| HTTP 403.14 | Wrong physical path | Ensure IIS site physical path points to the `publish\` subfolder, not the parent |
| Headers show "not present" | App Proxy SSO not configured | Verify header mappings are saved in App Proxy SSO settings; check connector health |
| App pool stops immediately | App pool set to managed runtime | Set `managedRuntimeVersion` to `""` (No Managed Code) |

---

## Project structure

```
├── HeaderEchoApp.csproj   # Project file (.NET 10, IIS in-process hosting)
├── Program.cs             # Entire application — single GET / endpoint
├── appsettings.json       # Default ASP.NET Core config (logging, etc.)
└── web.config             # IIS AspNetCoreModuleV2 configuration
```
