const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('campaignAPI', {
  load: () => ipcRenderer.invoke('campaign:load'),
  save: (state) => ipcRenderer.invoke('campaign:save', state),
  reset: () => ipcRenderer.invoke('campaign:reset'),
  exportCampaign: (state) => ipcRenderer.invoke('campaign:export', state),
  importCampaign: () => ipcRenderer.invoke('campaign:import'),
  openReference: (fileName) => ipcRenderer.invoke('library:open', fileName),
  openLibraryFolder: () => ipcRenderer.invoke('library:folder'),
  getMapsKey: () => ipcRenderer.invoke('maps-key:get'),
  setMapsKey: (key) => ipcRenderer.invoke('maps-key:set', key),
  openPlanningReference: (fileName) => ipcRenderer.invoke('planning:open-reference', fileName),
  getUnityStatus: () => ipcRenderer.invoke('unity:status'),
  launchUnityBattle: (state) => ipcRenderer.invoke('unity:launch', state),
  launchUnityOneStar: () => ipcRenderer.invoke('unity:launch-one-star'),
  resetUnityState: (state) => ipcRenderer.invoke('unity:reset', state),
  importUnityResult: (state) => ipcRenderer.invoke('unity:import-result', state)
});
