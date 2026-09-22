
const { contextBridge, ipcRenderer } = require('electron')

// Securely expose API to renderer
contextBridge.exposeInMainWorld('api', {
  // HTTP request method - delegates to main process for security
  request: async (options) => {
    return ipcRenderer.invoke('http-request', options);
  },
  // Login
  login: async (empresaCodigo, senha) => {
    const response = await ipcRenderer.invoke('http-request', {
      method: 'POST',
      url: '/api/auth/login',
      data: { empresaCodigo, senha }
    });
    if (response.status === 200 && response.data?.accessToken) {
      await ipcRenderer.invoke('set-token', response.data.accessToken);
      return response.data;
    }
    throw new Error(response.data?.error || 'Credenciais inválidas');
  },
  // Logout
  logout: async () => {
    await ipcRenderer.invoke('remove-token');
    return { success: true };
  },
  // Get token (for internal use)
  getToken: async () => {
    return await ipcRenderer.invoke('get-token');
  },
  // Notify main process of successful login (resize window)
  loginSuccess: async () => {
    return await ipcRenderer.invoke('login-success');
  },
  // Start the updater
  startUpdater: async (manifestUrl) => {
    return await ipcRenderer.invoke('start-updater', manifestUrl);
  },
  // Check for updates
  checkForUpdates: async () => {
    return await ipcRenderer.invoke('check-for-updates');
  },
  // Get current version
  getVersion: async () => {
    return await ipcRenderer.invoke('get-version');
  },
  // Listen for update notifications
  onUpdateAvailable: (callback) => {
    const handler = (_event, ...args) => callback(...args);
    ipcRenderer.on('update-available', handler);
    return () => ipcRenderer.removeListener('update-available', handler);
  }
});

// Expose electron utilities
contextBridge.exposeInMainWorld('electronAPI', {
  sendMessage: (channel, data) => {
    const validChannels = ['toMain'];
    if (validChannels.includes(channel)) {
      ipcRenderer.send(channel, data);
    }
  },
  onMessage: (channel, func) => {
    const validChannels = ['fromMain'];
    if (validChannels.includes(channel)) {
      const subscription = (_event, ...args) => func(...args);
      ipcRenderer.on(channel, subscription);
      return () => ipcRenderer.removeListener(channel, subscription);
    }
    return () => {};
  }
});
