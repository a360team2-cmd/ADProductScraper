# Visual Studio backend + VS Code frontend

## Requirements

- Visual Studio with **ASP.NET and web development** workload and .NET 9 SDK support. For Visual Studio 2022, use 17.12 or newer with a compatible .NET SDK (later SDK feature bands can require newer Visual Studio).
- VS Code.
- Node.js 22.12+ (22.x) with npm.

Microsoft compatibility reference: https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/9.0/version-requirements

## 1. Backend — Visual Studio

1. ZIP ko **new folder** mein extract karein.
2. Agar purana `Run.cmd` server chal raha ho to uski window mein Ctrl+C karein. Port 5080 par ek waqt mein ek backend chalega.
3. Visual Studio → **Open a project or solution** → `backend/AD Product Scraper.sln` select karein.
4. `ADProductScraper.Api` project par right-click → **Set as Startup Project**, agar zaroorat ho.
5. Toolbar mein **ADProductScraper.Api** launch profile select karein; IIS Express ki zaroorat nahin.
6. **F5** ya green Run button dabayein. Pehli baar NuGet packages restore honge.
7. `http://localhost:5080/swagger` khulega. Yahan API endpoints test karein. Health check `/api/health` par bhi available hai.

Backend code breakpoints ab Visual Studio mein kaam karenge.

## 2. Frontend — VS Code

1. VS Code → **File → Open Folder** → extracted project ka **frontend** folder select karein.
2. **Terminal → New Terminal** kholein. Terminal `frontend` folder mein hona chahiye.
3. Pehli baar:

```powershell
npm.cmd ci
```

4. Har baar frontend run karne ke liye:

```powershell
ng serve
```

5. `http://localhost:4200` kholein. Frontend changes save karne par browser automatically update hoga.
6. Optional: npm start chalne ke baad VS Code mein F5 se included Edge debugger profile launch kar sakte hain.

`npm.cmd` Windows PowerShell execution-policy errors se bachata hai; Command Prompt mein `npm ci` / `npm start` bhi theek hain.

## Connection

| Component | Address |
|---|---|
| Angular website | http://localhost:4200 |
| .NET API | http://localhost:5080 |
| API health check | http://localhost:5080/api/health |

Angular `/api` requests automatically backend port 5080 par proxy karta hai. API URLs code mein manually change ya browser CORS disable karne ki zaroorat nahin. Backend aur frontend dono running hon.

## First check

**Try a demo** → 6 products / 2 pages → **Download Excel**.

## Common problems

- **Port 5080 already in use:** old Run.cmd/previous backend ko Ctrl+C ya Visual Studio Stop se band karein.
- **Port 4200 already in use:** purana Angular terminal Ctrl+C se band karein; doosra port accept karne ke bajaye 4200 free karein.
- **502 / ECONNREFUSED / cannot load history:** backend F5 se start karein, health URL check karein.
- **SDK not supported:** Visual Studio aur uska .NET SDK compatible version par update karein; runtime alone source build ke liye kaafi nahin.
- **Old small text still showing:** new ZIP ke frontend folder se npm start karein, port **4200** kholein aur Ctrl+F5 karein. Old backend/runtime folder source changes automatically nahin uthata.

Developer workflow ke liye Run.cmd ya Start.cmd ki zaroorat nahin. They remain available only for the packaged, single-server mode.

