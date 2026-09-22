import React, { useEffect, useState } from 'react';

interface UpdateInfo {
  available: boolean;
  currentVersion?: string;
  latestVersion?: string;
  manifestUrl?: string;
  releaseNotes?: string;
  releaseUrl?: string;
}

declare global {
  interface Window {
    api?: {
      checkForUpdates: () => Promise<UpdateInfo>;
      getVersion: () => Promise<string>;
      startUpdater: (manifestUrl: string) => Promise<{ success: boolean; error?: string }>;
      onUpdateAvailable: (callback: (info: UpdateInfo) => void) => () => void;
    };
  }
}

export const UpdateNotification: React.FC = () => {
  const [updateInfo, setUpdateInfo] = useState<UpdateInfo | null>(null);
  const [showNotification, setShowNotification] = useState(false);
  const [updating, setUpdating] = useState(false);
  const [currentVersion, setCurrentVersion] = useState<string>('');

  useEffect(() => {
    // Get current version on mount
    const getVersion = async () => {
      if (window.api?.getVersion) {
        const version = await window.api.getVersion();
        setCurrentVersion(version);
      }
    };
    getVersion();

    // Listen for update notifications from main process
    if (window.api?.onUpdateAvailable) {
      const unsubscribe = window.api.onUpdateAvailable((info: UpdateInfo) => {
        setUpdateInfo(info);
        setShowNotification(true);
      });

      return () => {
        unsubscribe();
      };
    }
  }, []);

  const handleCheckForUpdates = async () => {
    if (window.api?.checkForUpdates) {
      const info = await window.api.checkForUpdates();
      setUpdateInfo(info);
      if (info.available) {
        setShowNotification(true);
      }
    }
  };

  const handleUpdate = async () => {
    if (updateInfo?.manifestUrl && window.api?.startUpdater) {
      setUpdating(true);
      const result = await window.api.startUpdater(updateInfo.manifestUrl);
      if (!result.success) {
        console.error('Failed to start updater:', result.error);
        setUpdating(false);
      }
    }
  };

  const handleDismiss = () => {
    setShowNotification(false);
  };

  if (!showNotification || !updateInfo?.available) {
    return (
      <button
        onClick={handleCheckForUpdates}
        className="fixed bottom-4 right-4 rounded-full bg-gray-100 p-2 text-gray-600 hover:bg-gray-200 transition-colors"
        title="Verificar atualizações"
      >
        <svg xmlns="http://www.w3.org/2000/svg" className="h-5 w-5" viewBox="0 0 20 20" fill="currentColor">
          <path fillRule="evenodd" d="M4 2a1 1 0 011 1v2.101a7.002 7.002 0 0111.601 2.566 1 1 0 11-1.885.666A5.002 5.002 0 005.999 7H9a1 1 0 010 2H4a1 1 0 01-1-1V3a1 1 0 011-1zm.008 9.057a1 1 0 011.276.61A5.002 5.002 0 0014.001 13H11a1 1 0 110-2h5a1 1 0 011 1v5a1 1 0 11-2 0v-2.101a7.002 7.002 0 01-11.601-2.566 1 1 0 01.61-1.276z" clipRule="evenodd" />
        </svg>
      </button>
    );
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="mx-4 w-full max-w-md rounded-lg bg-white shadow-xl">
        <div className="border-b border-gray-200 px-6 py-4">
          <h2 className="text-lg font-semibold text-gray-900">Atualização Disponível</h2>
        </div>
        <div className="px-6 py-4">
          <div className="mb-4 flex items-center gap-4">
            <div className="flex h-12 w-12 items-center justify-center rounded-full bg-blue-100">
              <svg xmlns="http://www.w3.org/2000/svg" className="h-6 w-6 text-blue-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
              </svg>
            </div>
            <div>
              <p className="text-sm text-gray-600">Versão atual: {currentVersion}</p>
              <p className="text-lg font-medium text-gray-900">Nova versão: {updateInfo.latestVersion}</p>
            </div>
          </div>
          
          {updateInfo.releaseNotes && (
            <div className="mb-4 rounded-lg bg-gray-50 p-3">
              <p className="mb-1 text-xs font-medium text-gray-700">Notas de versão:</p>
              <p className="text-sm text-gray-600 whitespace-pre-wrap">{updateInfo.releaseNotes}</p>
            </div>
          )}

          <p className="text-sm text-gray-600">
            Uma nova versão do SF Tecnologias está disponível. 
            Recomendamos atualizar para obter as últimas correções e melhorias.
          </p>
        </div>
        <div className="flex gap-3 border-t border-gray-200 px-6 py-4">
          <button
            onClick={handleDismiss}
            disabled={updating}
            className="flex-1 rounded-lg border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            Depois
          </button>
          <button
            onClick={handleUpdate}
            disabled={updating}
            className="flex-1 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {updating ? 'Atualizando...' : 'Atualizar Agora'}
          </button>
        </div>
      </div>
    </div>
  );
};

export default UpdateNotification;
