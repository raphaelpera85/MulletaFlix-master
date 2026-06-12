export default function(view, params) {
    var pluginId = 'f956680c-9a06-4cac-93d2-b57cd6061756';
    var baseUrl = window.location.href.split('/web/')[0];

    function render(config) {
        view.querySelector('#m3uUrl').value = config.M3uUrl || '';
        view.querySelector('#epgUrl').value = config.EpgUrl || '';
        view.querySelector('#enableAutoEpg').checked = !!config.EnableAutoEpg;
        view.querySelector('#autoEpgLanguage').value = config.AutoEpgLanguage || 'pt';
        view.querySelector('#strmPath').value = config.StrmOutputPath || '';
        view.querySelector('#syncResult').style.display = config.LastSyncTime ? 'block' : 'none';
        view.querySelector('#canaisUrl').value = baseUrl + '/MidiaStorageOnline/m3u/canais';
        view.querySelector('#epgResult').style.display = (config.EpgUrl || config.EnableAutoEpg || config.EpgLastSyncTime) ? 'block' : 'none';
        view.querySelector('#epgGuideUrl').value = baseUrl + '/MidiaStorageOnline/epg/guide.xml';

        var lastSync = config.LastSyncTime ? new Date(config.LastSyncTime).toLocaleString() : 'nunca';
        var duration = config.LastSyncDurationSeconds ? ' | Duracao: ' + config.LastSyncDurationSeconds.toFixed(1) + 's' : '';
        var epgCoverage = (config.EpgCompatibleChannelCount || 0) + '/' + (config.TotalChannelCount || 0);
        var epgStatus = config.EpgUrl ? (' | EPG: ' + epgCoverage + ' canais com tvg-id') : (config.EnableAutoEpg ? (' | Auto EPG: ' + epgCoverage + ' canais') : '');
        view.querySelector('#syncStatus').textContent = 'Ultima sincronizacao: ' + lastSync + duration + ' | Arquivos: ' + (config.SyncedFileCount || 0) + epgStatus;
        view.querySelector('#epgStatus').textContent = config.EpgLastSyncTime ? ('Ultimo EPG: ' + new Date(config.EpgLastSyncTime).toLocaleString() + (config.EpgLastError ? ' | Erro: ' + config.EpgLastError : '')) : (config.EpgLastError || '');
        view.querySelector('#lastError').textContent = config.LastSyncError || '';
    }

    function loadConfig() {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            render(config);
            Dashboard.hideLoadingMsg();
        }, function (err) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert((err && (err.message || err.statusText)) || 'Erro ao carregar configuracao.');
        });
    }

    loadConfig();

    view.querySelector('.configForm').addEventListener('submit', function (e) {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        var config = {
            M3uUrl: view.querySelector('#m3uUrl').value,
            EpgUrl: view.querySelector('#epgUrl').value,
            EnableAutoEpg: view.querySelector('#enableAutoEpg').checked,
            AutoEpgLanguage: view.querySelector('#autoEpgLanguage').value,
            StrmOutputPath: view.querySelector('#strmPath').value
        };
        ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('MidiaStorageOnline/config'),
            data: JSON.stringify(config),
            contentType: 'application/json'
        }).then(function () {
            Dashboard.hideLoadingMsg();
            loadConfig();
        }).catch(function (err) {
            Dashboard.alert('Erro ao salvar: ' + (err && (err.message || err.statusText || JSON.stringify(err))));
            Dashboard.hideLoadingMsg();
        });
    });

    var browseButton = view.querySelector('#btnBrowseStrmPath');
    if (browseButton) {
        browseButton.addEventListener('click', function () {
            var picker = new Dashboard.DirectoryBrowser();
            picker.show({
                path: view.querySelector('#strmPath').value,
                validateWriteable: true,
                header: 'Selecionar pasta de saida',
                instruction: 'Escolha a pasta onde os arquivos .strm serao salvos.',
                callback: function (path) {
                    if (path) view.querySelector('#strmPath').value = path;
                    picker.close();
                }
            });
        });
    }

    view.querySelector('#btnSyncNow').addEventListener('click', function () {
        Dashboard.showLoadingMsg();
        view.querySelector('#btnSyncNow').disabled = true;
        view.querySelector('#lastError').textContent = '';
        ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('MidiaStorageOnline/sync')
        }).then(function (r) {
            Dashboard.hideLoadingMsg();
            view.querySelector('#btnSyncNow').disabled = false;
            Dashboard.alert(r.message || 'Sincronizacao concluida!');
            loadConfig();
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            view.querySelector('#btnSyncNow').disabled = false;
            var msg = err && (err.message || err.statusText || JSON.stringify(err)) || 'Erro na sincronizacao.';
            Dashboard.alert(msg);
            loadConfig();
        });
    });
}
