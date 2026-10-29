const { app, BrowserWindow, Menu, shell, dialog, ipcMain, screen } = require('electron');
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');

// Web.exe binds a fixed port, so a second copy can't start - focus the first window instead.
if (!app.requestSingleInstanceLock()) {
  app.quit();
}
app.on('second-instance', () => {
  if (mainWindow) {
    if (mainWindow.isMinimized()) mainWindow.restore();
    mainWindow.focus();
  }
});

// The web app refuses to start without Jwt:SecretKey. An installed copy has no user-secrets,
// so generate a random per-machine key on first launch and keep it in the user's app-data folder.
function getJwtSecretKey() {
  if (process.env.Jwt__SecretKey) return process.env.Jwt__SecretKey;
  const keyFile = path.join(app.getPath('userData'), 'jwt.key');
  try {
    const existing = fs.readFileSync(keyFile, 'utf8').trim();
    if (existing.length >= 32) return existing;
  } catch (_) { /* first launch */ }
  const key = crypto.randomBytes(48).toString('base64');
  fs.mkdirSync(path.dirname(keyFile), { recursive: true });
  fs.writeFileSync(keyFile, key, { encoding: 'utf8', mode: 0o600 });
  return key;
}

let mainWindow = null;
let dotnetProcess = null;
const APP_URL = 'http://localhost:5000';
// Set by the Visual Studio "Electron" profile: the web app is already running under the debugger
// (the value is its process id), so connect to it instead of starting Web.exe.
const EXTERNAL_SERVER_PID = Number(process.env.NEXPOS_EXTERNAL_SERVER) || 0;
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
      // Production: extraFiles copies publish/app to <install dir>/app, next to NexPos.exe.
      const prodPaths = [
        path.join(path.dirname(process.execPath), 'app', 'Web.exe'),
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
    // No shell: a cmd.exe wrapper breaks on install paths with spaces ("Program Files") and
    // kill() would only end cmd.exe, leaving Web.exe holding the port and the database.
    dotnetProcess = spawn(dotnetExePath, [], {
      cwd: workingDir || path.dirname(dotnetExePath),
      shell: false,
      windowsHide: true,
      stdio: 'pipe',
      env: Object.assign({}, process.env, {
        Jwt__SecretKey: getJwtSecretKey(),
        // The install dir may not be writable, so keep logs in the user's app-data folder.
        NEXPOS_LOG_DIR: process.env.NEXPOS_LOG_DIR || path.join(app.getPath('userData'), 'logs'),
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
  // Size the window to the usable screen area (minus taskbar). A fixed 1400x900 with a
  // 1024x768 minimum doesn't fit a 1366x768 display, or a larger one with Windows scaling
  // (125% leaves ~1093x614 DIPs), so the bottom/right of the app ended up off-screen.
  const { width: workW, height: workH } = screen.getPrimaryDisplay().workAreaSize;
  const fitsPreferred = workW >= 1400 && workH >= 900;

  mainWindow = new BrowserWindow({
    width: Math.min(1400, workW),
    height: Math.min(900, workH),
    minWidth: Math.min(900, workW),
    minHeight: Math.min(560, workH),
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      webSecurity: true,
      preload: path.join(__dirname, 'electron-preload.js')
    },
    icon: path.join(__dirname, 'assets', 'icon.png'), // Optional: add an icon
    show: false // Don't show until ready
  });

  // No File/Edit/View menu bar - it's a kiosk-style POS, not a document app.
  Menu.setApplicationMenu(null);

  setupWebContents(mainWindow.webContents);

  // Load the application
  mainWindow.loadURL(APP_URL);

  // Show window when ready
  mainWindow.once('ready-to-show', () => {
    // On small screens the preferred size doesn't fit, so use all the room there is.
    if (!fitsPreferred) mainWindow.maximize();
    mainWindow.show();
    mainWindow.focus();
  });

  // Handle window closed
  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

const isAppUrl = (url) => {
  try {
    return new URL(url).origin === APP_URL;
  } catch {
    return false;
  }
};

// UI zoom shared by every app window; chosen from the screen size on first use.
let uiZoom = null;

// The page is laid out for roughly this many CSS px of width: a smaller screen gets zoomed out toward it
// so more fits, but never below MIN_ZOOM (below that the text stops being readable at arm's length).
// It was 1800 / 0.65, which put a 1366x768 laptop at 76% - 11px body text and 9px captions. At 1500 / 0.8 the
// same laptop runs at 91%, text is ~20% larger, and the till and tables still fit (see css/pages/pos.css
// and css/site.css, which adapt to widths down to ~900 CSS px). Raise TARGET_CSS_WIDTH for denser, smaller UI.
const TARGET_CSS_WIDTH = 1500;
const MIN_ZOOM = 0.8;

// Shared setup for the main window and any popup windows the app opens (e.g. Add Product).
function setupWebContents(contents) {
  // Scale the UI down on smaller screens so more fits (cart rows, product tiles, table columns).
  // Re-applied after every load because navigation can reset it.
  if (uiZoom === null) {
    // workAreaSize is in DIPs, so Windows display scaling is already accounted for.
    // Aim for TARGET_CSS_WIDTH of width; never enlarge, never go below MIN_ZOOM.
    const { width } = screen.getPrimaryDisplay().workAreaSize;
    uiZoom = Math.min(1, Math.max(MIN_ZOOM, Math.round((width / TARGET_CSS_WIDTH) * 100) / 100));
  }
  contents.on('did-finish-load', () => contents.setZoomFactor(uiZoom));

  // Ctrl + / Ctrl - / Ctrl 0 to adjust the size (the removed menu carried these too).
  contents.on('before-input-event', (event, input) => {
    if (input.type !== 'keyDown' || !input.control) return;
    const key = input.key;
    if (key === '=' || key === '+') uiZoom = Math.min(1.5, uiZoom + 0.05);
    else if (key === '-') uiZoom = Math.max(0.5, uiZoom - 0.05);
    else if (key === '0') uiZoom = 1;
    else return;
    uiZoom = Math.round(uiZoom * 100) / 100;
    contents.setZoomFactor(uiZoom);
    event.preventDefault();
  });

  // The removed menu also carried the reload/DevTools shortcuts; keep those for development only.
  if (!app.isPackaged) {
    contents.on('before-input-event', (event, input) => {
      if (input.type !== 'keyDown') return;
      if (input.key === 'F12') {
        contents.toggleDevTools();
        event.preventDefault();
      } else if (input.control && input.key.toLowerCase() === 'r') {
        contents.reload();
        event.preventDefault();
      }
    });
  }

  // App pages opened with window.open get their own app window; external links go to the default browser.
  contents.setWindowOpenHandler(({ url }) => {
    if (isAppUrl(url)) {
      return {
        action: 'allow',
        overrideBrowserWindowOptions: {
          minWidth: 400,
          minHeight: 400,
          icon: path.join(__dirname, 'assets', 'icon.png'),
          webPreferences: {
            nodeIntegration: false,
            contextIsolation: true,
            webSecurity: true,
            preload: path.join(__dirname, 'electron-preload.js')
          }
        }
      };
    }
    shell.openExternal(url);
    return { action: 'deny' };
  });

  contents.on('did-create-window', (childWindow) => {
    setupWebContents(childWindow.webContents);
  });

  // Prevent navigation to external URLs
  contents.on('will-navigate', (event, navigationUrl) => {
    if (!isAppUrl(navigationUrl)) {
      event.preventDefault();
      shell.openExternal(navigationUrl);
    }
  });
}

// Barcode sticker printing: silent (no OS/Electron dialog), sent straight to the sticker
// printer chosen on this machine (or the system default if none is chosen) via a hidden
// BrowserWindow that loads the label HTML built by the web app. The web app remembers the
// chosen printer per machine in localStorage, so each till can use its own label printer.
const isAppPage = (event) => new URL(event.senderFrame.url).origin === APP_URL;

ipcMain.handle('sticker:printers', async (event) => {
  if (!isAppPage(event)) return [];
  const printers = await event.sender.getPrintersAsync();
  return printers.map((p) => ({ name: p.name, displayName: p.displayName, isDefault: p.isDefault }));
});

ipcMain.handle('sticker:print', async (event, { html, printerName, copies, widthMm, heightMm }) => {
  // Only the app's own pages may print.
  if (!isAppPage(event)) {
    return { success: false, failureReason: 'forbidden' };
  }
  // The renderer clamps these too; clamp again so a bad value can't hand the printer a huge
  // (or zero/negative) job or a nonsense page size.
  const copyCount = Math.min(100, Math.max(1, Math.floor(Number(copies) || 1)));
  const width = Math.min(150, Math.max(15, Number(widthMm) || 40));
  const height = Math.min(150, Math.max(10, Number(heightMm) || 25));
  const printWin = new BrowserWindow({ show: false, webPreferences: { javascript: false } });
  try {
    await printWin.loadURL('data:text/html;charset=UTF-8,' + encodeURIComponent(String(html)));
    // Race against a timeout so a stuck print job (e.g. a printer that's off/offline)
    // can't leave this hidden window - or the renderer's awaited IPC call - hanging forever.
    const result = await Promise.race([
      new Promise((resolve) => {
        printWin.webContents.print(
          {
            silent: true,
            printBackground: true,
            deviceName: printerName || undefined,
            copies: copyCount,
            margins: { marginType: 'none' },
            pageSize: { width: Math.round(width * 1000), height: Math.round(height * 1000) }, // microns
          },
          (success, failureReason) => resolve({ success, failureReason }),
        );
      }),
      // Bigger jobs take longer to hand off, so grow the ceiling with the copy count.
      new Promise((resolve) =>
        setTimeout(() => resolve({ success: false, failureReason: 'timeout' }), 15000 + copyCount * 500),
      ),
    ]);
    if (!result.success) console.error('Sticker print failed:', result.failureReason || '(no reason given)');
    return result;
  } finally {
    printWin.close();
  }
});

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
    if (EXTERNAL_SERVER_PID) {
      console.log('Using the web app started by Visual Studio...');
      await checkServerReady();
      // Quit when the debug session stops, rather than leaving a window on a dead server.
      setInterval(() => {
        try {
          process.kill(EXTERNAL_SERVER_PID, 0);
        } catch {
          app.quit();
        }
      }, 2000);
    } else {
      console.log('Starting .NET application...');
      await startDotNetApp();
    }
    await warmUp();
    console.log('Creating Electron window...');
    createWindow();
  } catch (error) {
    console.error('Failed to start application:', error);
    dialog.showErrorBox('NexPos failed to start',
      `${error.message}\n\nLogs: ${path.join(app.getPath('userData'), 'logs')}`);
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
