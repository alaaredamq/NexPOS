# Electron Desktop App Setup Guide

This guide will help you convert your ASP.NET Core ERP application into a standalone Windows EXE file using Electron.

## Prerequisites

1. **Node.js and npm** - Download from [nodejs.org](https://nodejs.org/) (LTS version recommended)
2. **.NET 9.0 SDK** - Already installed (you're using it)
3. **Visual Studio or Rider** - For building the .NET app

## Step-by-Step Instructions

### Step 1: Install Node.js Dependencies

Open PowerShell in the project root directory and run:

```powershell
npm install
```

This will install:
- `electron` - The Electron framework
- `electron-builder` - For packaging the app into an EXE
- `cross-env` - For cross-platform environment variables

### Step 2: Build Your .NET Application

First, you need to publish your .NET application. Run this command in PowerShell:

```powershell
cd Web
dotnet publish -c Release -o ../publish/app
cd ..
```

This creates a published version of your app in the `publish/app` folder.

### Step 3: Test Electron in Development Mode

Before building the EXE, test that Electron works with your app:

```powershell
npm run dev
```

This will:
1. Start your .NET application
2. Wait for it to be ready on `http://localhost:5000`
3. Open an Electron window showing your app

**Note:** Make sure your database connection is working and the app can start successfully.

### Step 4: Update electron-main.js for Production (if needed)

If your published app structure is different, you may need to adjust the path in `electron-main.js` around line 30-40. The current setup looks for:
- Development: `Web/bin/Debug/net9.0/Web.exe`
- Production: `process.resourcesPath/app/Web.exe`

### Step 5: Build the EXE File

Once everything works in development, build the EXE:

#### Option A: Build Installer (Recommended)
```powershell
npm run build:win-installer
```

This creates an installer in the `dist` folder that users can run to install your app.

#### Option B: Build Portable EXE
```powershell
npm run build:win-portable
```

This creates a single portable EXE file that doesn't require installation.

#### Option C: Build Both
```powershell
npm run build:win
```

This creates both installer and portable versions.

### Step 6: Find Your EXE Files

After building, your EXE files will be in the `dist` folder:
- **Installer**: `dist/ERP System Setup 1.0.0.exe`
- **Portable**: `dist/ERP System-1.0.0-portable.exe`

## Important Notes

### Database Connection

The app uses SQLite - no separate database server needs to be installed on the target
machine. The `.db` file is created and migrated automatically on first launch, at
`%LocalAppData%\NexPOS\nexpos.db` for the logged-in Windows user. Useful for
support/troubleshooting: delete that file (with the app closed) to reset to a fresh,
freshly-seeded database.

### Port Configuration

The app runs on `http://localhost:5000`. If you need to change this:
1. Update `Web/appsettings.json` - change the `Urls` setting
2. Update `electron-main.js` - change the `APP_URL` constant (line 7)

### Adding an Icon

1. Create an icon file: `assets/icon.ico` (256x256 recommended)
2. The `package.json` already references it - just add the file

### Troubleshooting

#### Electron window shows "Cannot connect"
- Check that the .NET app started successfully
- Look at the console output for errors
- Verify the port in `appsettings.json` matches `APP_URL` in `electron-main.js`

#### .NET app won't start
- Check database connection
- Verify all dependencies are included in the publish folder
- Check the console output in Electron (View > Toggle Developer Tools)

#### Build fails
- Make sure all files are saved
- Close any running instances of the app
- Delete `dist` folder and try again
- Check that `dotnet publish` completed successfully

## Development Workflow

1. **Make changes to your .NET code**
2. **Rebuild**: `dotnet build` or use your IDE
3. **Test**: `npm run dev`
4. **When ready to distribute**: `npm run build:win-installer`

## Distribution

The installer EXE can be distributed to end users. They just need to:
1. Run the installer
2. Follow the installation wizard
3. Launch the app from the Start Menu or Desktop shortcut

The portable version can be run directly without installation - just double-click the EXE.

## Advanced Configuration

### Customize Build Settings

Edit `package.json` under the `"build"` section:
- Change `appId` - unique identifier for your app
- Change `productName` - the name shown to users
- Modify `win.icon` - path to your icon file
- Adjust `files` array - what gets included in the package

### Environment-Specific Settings

You can create different `appsettings.json` files for different environments and copy the appropriate one during the build process.

## Next Steps

1. Test the development mode: `npm run dev`
2. Fix any issues that come up
3. Build the installer: `npm run build:win-installer`
4. Test the installer on a clean machine
5. Distribute to your users!

---

**Need Help?** Check the Electron documentation: https://www.electronjs.org/docs
