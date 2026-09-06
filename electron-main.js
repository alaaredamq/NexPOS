const { app, BrowserWindow, shell } = require('electron');
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');

let mainWindow = null;
let dotnetProcess = null;
const APP_URL = 'http://localhost:5000';
const MAX_RETRIES = 30;
const RETRY_DELAY = 1000; // 1 second

// Function to check if the server is ready
function checkServerReady(retries = 0) {
  return new Promise((resolve, reject) => {
    if (retries >= MAX_RETRIES) {
      reject(new Error('Server failed to start within timeout period'));
      return;
    }

    const http = require('http');
    const req = http.get(APP_URL, (res) => {
      if (res.statusCode === 200 || res.statusCode === 302) {
        console.log('Server is ready!');
        resolve(true);
      } else {
        setTimeout(() => checkServerReady(retries + 1).then(resolve).catch(reject), RETRY_DELAY);
      }
    });

    req.on('error', () => {
      setTimeout(() => checkServerReady(retries + 1).then(resolve).catch(reject), RETRY_DELAY);
    });

    req.setTimeout(500, () => {
      req.destroy();
      setTimeout(() => checkServerReady(retries + 1).then(resolve).catch(reject), RETRY_DELAY);
    });
  });
}

// Function to start the .NET application
function startDotNetApp() {
  return new Promise((resolve, reject) => {
    // Determine the path to the .NET executable
    const isDev = process.env.NODE_ENV === 'development' || !app.isPackaged;
    
    let dotnetExePath;
    let workingDir;
    
    if (isDev) {
      // Development: prefer an optimised published build, then Release, then Debug.
      const devPaths = [
        path.join(__dirname, 'publish', 'app', 'Web.exe'),
        path.join(__dirname, 'Web', 'bin', 'Release', 'net9.0', 'win-x64', 'Web.exe'),
        path.join(__dirname, 'Web', 'bin', 'Release', 'net9.0', 'Web.exe'),
        path.join(__dirname, 'Web', 'bin', 'Debug', 'net9.0', 'Web.exe')
      ];
      
      for (const devPath of devPaths) {
        if (fs.existsSync(devPath)) {
          dotnetExePath = devPath;
          workingDir = path.dirname(devPath);
          break;
        }
      }
      
      // If no exe found, try using dotnet CLI
      if (!dotnetExePath) {
        const projectPath = path.join(__dirname, 'Web', 'Web.csproj');
        if (fs.existsSync(projectPath)) {
          console.log('Using dotnet CLI to run the project...');
          dotnetProcess = spawn('dotnet', ['run', '--project', projectPath], {
            cwd: __dirname,
            shell: true,
            stdio: 'pipe'
          });
          setTimeout(() => {
            checkServerReady()
              .then(() => resolve())
              .catch((error) => {
                console.error('Server check failed:', error);
                reject(error);
              });
          }, 2000);
          return;
        } else {
          reject(new Error(`Could not find Web.exe or Web.csproj. Checked paths: ${devPaths.join(', ')}`));
          return;
        }
      }
    } else {
      // Production: Look in resources/app (electron-builder default)
      const prodPaths = [
        path.join(process.resourcesPath, 'app', 'Web.exe'),
        path.join(__dirname, 'app', 'Web.exe'),
        path.join(__dirname, 'Web.exe')
      ];
      
      for (const prodPath of prodPaths) {
        if (fs.existsSync(prodPath)) {
          dotnetExePath = prodPath;
          workingDir = path.dirname(prodPath);
          break;
        }
      }
      
      if (!dotnetExePath) {
        reject(new Error(`Could not find Web.exe in production. Checked: ${prodPaths.join(', ')}`));
        return;
      }
    }

    console.log(`Starting .NET app from: ${dotnetExePath}`);
    dotnetProcess = spawn(dotnetExePath, [], {
      cwd: workingDir || path.dirname(dotnetExePath),
      shell: true,
      stdio: 'pipe',
      env: Object.assign({}, process.env, {
        // Packaged / published runs use the Production pipeline (no DeveloperExceptionPage,
        // Serilog at Warning). Debug dev runs keep whatever the profile sets.
        ASPNETCORE_ENVIRONMENT: dotnetExePath.includes(`${path.sep}Debug${path.sep}`) ? (process.env.ASPNETCORE_ENVIRONMENT || 'Development') : 'Production',
        DOTNET_TieredPGO: '1',
        DOTNET_TieredCompilation: '1'
      })
    });

    // Log output for debugging
    if (dotnetProcess.stdout) {
      dotnetProcess.stdout.on('data', (data) => {
        console.log(`[.NET] ${data.toString()}`);
      });
    }

    if (dotnetProcess.stderr) {
      dotnetProcess.stderr.on('data', (data) => {
        console.error(`[.NET Error] ${data.toString()}`);
      });
    }

    dotnetProcess.on('error', (error) => {
      console.error('Failed to start .NET process:', error);
      reject(error);
    });

    dotnetProcess.on('exit', (code) => {
      console.log(`.NET process exited with code ${code}`);
      if (code !== 0 && code !== null) {
        console.error('Application exited unexpectedly');
      }
    });

    // Start polling immediately — checkServerReady already retries every second.
    checkServerReady()
      .then(() => resolve())
      .catch((error) => {
        console.error('Server check failed:', error);
        reject(error);
      });
  });
}

// Create the main window
function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1400,
    height: 900,
    minWidth: 1024,
    minHeight: 768,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      webSecurity: true
    },
    icon: path.join(__dirname, 'assets', 'icon.png'), // Optional: add an icon
    show: false // Don't show until ready
  });

  // Load the application
  mainWindow.loadURL(APP_URL);

  // Show window when ready
  mainWindow.once('ready-to-show', () => {
    mainWindow.show();
    mainWindow.focus();
  });

  // Handle external links - open in default browser
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url);
    return { action: 'deny' };
  });

  // Prevent navigation to external URLs
  mainWindow.webContents.on('will-navigate', (event, navigationUrl) => {
    const parsedUrl = new URL(navigationUrl);
    if (parsedUrl.origin !== APP_URL) {
      event.preventDefault();
      shell.openExternal(navigationUrl);
    }
  });

  // Handle window closed
  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

// Fire a cheap request so EF builds its model and warms the connection pool before the
// first real page is requested. Non-fatal.
function warmUp() {
  return new Promise((resolve) => {
    const http = require('http');
    let pending = 2;
    const done = () => { if (--pending <= 0) resolve(); };
    // Health endpoint warms the pipeline; the login page warms Razor view compilation.
    ['/api/Health', '/en/Account/Login'].forEach((p) => {
      const req = http.get(`${APP_URL}${p}`, (res) => { res.resume(); res.on('end', done); });
      req.on('error', done);
      req.setTimeout(4000, () => { req.destroy(); done(); });
    });
  });
}

// App event handlers
app.whenReady().then(async () => {
  try {
    console.log('Starting .NET application...');
    await startDotNetApp();
    await warmUp();
    console.log('Creating Electron window...');
    createWindow();
  } catch (error) {
    console.error('Failed to start application:', error);
    app.quit();
  }
});

app.on('window-all-closed', () => {
  // On Windows and Linux, quit when all windows are closed
  // On macOS, keep the app running even when all windows are closed
  if (process.platform !== 'darwin') {
    // Kill the .NET process
    if (dotnetProcess) {
      console.log('Terminating .NET process...');
      dotnetProcess.kill();
      dotnetProcess = null;
    }
    app.quit();
  }
});

app.on('activate', () => {
  // On macOS, re-create window when dock icon is clicked
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindow();
  }
});

// Cleanup on quit
app.on('before-quit', () => {
  if (dotnetProcess) {
    console.log('Terminating .NET process...');
    dotnetProcess.kill();
    dotnetProcess = null;
  }
});

// Handle uncaught exceptions
process.on('uncaughtException', (error) => {
  console.error('Uncaught Exception:', error);
});
