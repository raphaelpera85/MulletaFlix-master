export default function(view, params) {
    var pluginId = 'f956680c-9a06-4cac-93d2-b57cd6061756';
    var baseUrl = window.location.href.split('/web/')[0];

    function render(config) {
        view.querySelector('#m3uUrl').value = config.M3uUrl || '';
        view.querySelector('#epgUrl').value = config.EpgUrl || '';
        view.querySelector('#strmPath').value = config.StrmOutputPath || '';

        if (config.CanaisM3uContent) {
            view.querySelector('#syncResult').style.display = 'block';
            view.querySelector('#canaisUrl').value = baseUrl + '/MidiaStorageOnline/m3u/canais';
        } else {
            view.querySelector('#syncResult').style.display = 'none';
        }

        if (config.EpgUrl || config.EpgLastSyncTime) {
            view.querySelector('#epgResult').style.display = 'block';
            view.querySelector('#epgGuideUrl').value = baseUrl + '/MidiaStorageOnline/epg/guide.xml';
        } else {
            view.querySelector('#epgResult').style.display = 'none';
        }

        var lastSync = config.LastSyncTime ? new Date(config.LastSyncTime).toLocaleString() : 'nunca';
        var duration = config.LastSyncDurationSeconds ? ' | Duracao: ' + config.LastSyncDurationSeconds.toFixed(1) + 's' : '';
        var epgCoverage = (config.EpgCompatibleChannelCount || 0) + '/' + (config.TotalChannelCount || 0);
        var epgStatus = config.EpgUrl ? (' | EPG: ' + epgCoverage + ' canais com tvg-id') : '';
        view.querySelector('#syncStatus').textContent = 'Ultima sincronizacao: ' + lastSync + duration + ' | Arquivos: ' + (config.SyncedFileCount || 0) + epgStatus;
        view.querySelector('#epgStatus').textContent = config.EpgLastSyncTime ? ('Ultimo EPG: ' + new Date(config.EpgLastSyncTime).toLocaleString() + (config.EpgLastError ? ' | Erro: ' + config.EpgLastError : '')) : (config.EpgLastError || '');
        view.querySelector('#lastError').textContent = config.LastSyncError || '';
    }

    function browseStrmPath() {
        var picker = new Dashboard.DirectoryBrowser();

        picker.show({
            path: view.querySelector('#strmPath').value,
            validateWriteable: true,
            header: 'Selecionar pasta de saida',
            instruction: 'Escolha a pasta onde os arquivos .strm serao salvos.',
            callback: function (path) {
                if (path) {
                    view.querySelector('#strmPath').value = path;
                }

                picker.close();
            }
        });
    }

    view.addEventListener('pageshow', function () {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            render(config);
            Dashboard.hideLoadingMsg();
        });
    });

    view.querySelector('.configForm').addEventListener('submit', function (e) {
        e.preventDefault();
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config.M3uUrl = view.querySelector('#m3uUrl').value;
            config.EpgUrl = view.querySelector('#epgUrl').value;
            config.StrmOutputPath = view.querySelector('#strmPath').value;
            ApiClient.updatePluginConfiguration(pluginId, config).then(function () {
                Dashboard.processPluginConfigurationUpdateResult();
            });
        });
    });

    var browseButton = view.querySelector('#btnBrowseStrmPath');
    if (browseButton) {
        browseButton.addEventListener('click', browseStrmPath);
    }

    view.querySelector('#btnSyncNow').addEventListener('click', function () {
        Dashboard.showLoadingMsg();
        view.querySelector('#btnSyncNow').disabled = true;
        view.querySelector('#lastError').textContent = '';
        ApiClient.ajax({
            type: 'POST', url: ApiClient.getUrl('MidiaStorageOnline/sync')
        }).then(function (r) {
            Dashboard.hideLoadingMsg();
            view.querySelector('#btnSyncNow').disabled = false;
            Dashboard.alert(r.message || 'Sincronizacao concluida!');
            Dashboard.processPluginConfigurationUpdateResult();
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            view.querySelector('#btnSyncNow').disabled = false;
            var msg = err && (err.message || err.statusText || JSON.stringify(err)) || 'Erro na sincronizacao.';
            Dashboard.alert(msg);
            Dashboard.processPluginConfigurationUpdateResult();
        });
    });
}
