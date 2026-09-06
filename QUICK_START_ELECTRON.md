# Quick Start: Build EXE from Your ERP App

## 🚀 Fast Track (3 Commands)

```powershell
# 1. Install dependencies (first time only)
npm install

# 2. Build everything (publishes .NET app + creates EXE)
.\build-electron.ps1 win-installer

# 3. Find your EXE in the 'dist' folder!
```

## 📋 Manual Steps

### First Time Setup
```powershell
npm install
```

### Every Time You Build

**Option 1: Use the build script (Recommended)**
```powershell
.\build-electron.ps1 win-installer
```

**Option 2: Manual steps**
```powershell
# Publish .NET app
dotnet publish Web\Web.csproj -c Release -o publish\app

# Build Electron package
npm run build:win-installer
```

## 🎯 Build Options

- `win-installer` - Creates installer EXE (recommended for distribution)
- `win-portable` - Creates single portable EXE (no installation needed)
- `win` - Creates both installer and portable

## 📁 Output Location

All EXE files are created in: **`dist`** folder

## ⚠️ Important Notes

1. **Database**: Make sure SQL Server is accessible on target machines
2. **Port**: App runs on `http://localhost:5000` (change in `appsettings.json` if needed)
3. **Testing**: Run `npm run dev` to test before building

## 🐛 Troubleshooting

**"Cannot find Web.exe"**
- Run `dotnet publish Web\Web.csproj -c Release -o publish\app` first

**"npm command not found"**
- Install Node.js from [nodejs.org](https://nodejs.org/)

**Build fails**
- Close any running instances of the app
- Delete `dist` and `publish` folders
- Try again

---

**Full Guide**: See `ELECTRON_SETUP_GUIDE.md` for detailed instructions.
