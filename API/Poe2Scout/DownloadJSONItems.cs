using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NinjaPricer.API.Poe2Scout.Models;

namespace NinjaPricer.API.Poe2Scout;

public class DataDownloader 
{
    private const string BaseUrl = "https://poe2scout.com/api";

    private static string GetLink(string path, string league, int page)
    {
        return $"{BaseUrl}/{path}?league={league}&page={page}&perPage=250";
    }
    
    private int _updating;
    public double? DivineValue { get; set; }
    public CollectiveApiData CollectedData { get; set; }

    private class LeagueMetadata
    {
        public DateTime LastLoadTime { get; set; }
    }

    public Action<string> log { get; set; }
    public NinjaPricerSettings Settings { get; set; }
    public string DataDirectory { get; set; }

    public void StartDataReload(string league, bool forceRefresh)
    {
        StartDataReload(league, forceRefresh, CancellationToken.None);
    }

    public void StartDataReload(string league, bool forceRefresh, CancellationToken cancellationToken)
    {
        log?.Invoke($"Getting data for {league}");

        if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0)
        {
            log?.Invoke("Update is already in progress");
            return;
        }

        // Snapshot references to avoid races if the host disposes/clears during shutdown
        var logger = log;
        var settings = Settings;
        var dataDir = DataDirectory;

        if (string.IsNullOrWhiteSpace(league) || settings == null || string.IsNullOrWhiteSpace(dataDir))
        {
            logger?.Invoke("Data reload aborted: invalid configuration or missing settings.");
            Interlocked.Exchange(ref _updating, 0);
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                logger?.Invoke("Gathering Data from Poe.Ninja.");

                var newData = new CollectiveApiData();
                var tryWebFirst = forceRefresh;
                var metadataPath = Path.Join(dataDir, league, "meta.json");
                if (!tryWebFirst && settings.DataSourceSettings.AutoReload)
                {
                    tryWebFirst = await IsLocalCacheStale(metadataPath, cancellationToken).ConfigureAwait(false);
                }

                newData.Currency    = (await LoadData<Currency.Item, Currency.RootObject>("Currency.json",   "items/currency/currency", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))    ?? new();
                newData.Breach      = (await LoadData<Currency.Item, Currency.RootObject>("Breach.json",     "items/currency/breachcatalyst", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false)) ?? new();
                newData.Weapons     = (await LoadData<Unique.Item,   Unique.RootObject>(  "Weapons.json",    "items/unique/weapon", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))     ?? new();
                newData.Armour      = (await LoadData<Unique.Item,   Unique.RootObject>(  "Armour.json",     "items/unique/armour", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))      ?? new();
                newData.Accessories = (await LoadData<Unique.Item,   Unique.RootObject>(  "Accessories.json","items/unique/accessory", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))  ?? new();
                newData.Delirium    = (await LoadData<Currency.Item, Currency.RootObject>("Delirium.json",   "items/currency/delirium", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))   ?? new();
                newData.Essences    = (await LoadData<Currency.Item, Currency.RootObject>("Essences.json",   "items/currency/essences", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))   ?? new();
                newData.Runes       = (await LoadData<Currency.Item, Currency.RootObject>("Runes.json",      "items/currency/runes", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))      ?? new();
                newData.Ritual      = (await LoadData<Currency.Item, Currency.RootObject>("Ritual.json",     "items/currency/ritual", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))     ?? new();
                newData.Ultimatums  = (await LoadData<Currency.Item, Currency.RootObject>("Ultimatum.json",  "items/currency/ultimatum", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))  ?? new();
                newData.Fragments   = (await LoadData<Currency.Item, Currency.RootObject>("Fragments.json",  "items/currency/fragments", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))  ?? new();
                newData.Talismans   = (await LoadData<Currency.Item, Currency.RootObject>("Talismans.json",  "items/currency/talismans", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))  ?? new();
                newData.Expeditions = (await LoadData<Currency.Item, Currency.RootObject>("Expedition.json", "items/currency/expedition", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false)) ?? new();
                newData.Waystones   = (await LoadData<Currency.Item, Currency.RootObject>("Waystones.json",  "items/currency/waystones", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))  ?? new();
                newData.VaultKeys   = (await LoadData<Currency.Item, Currency.RootObject>("VaultKeys.json",  "items/currency/vaultkeys", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))  ?? new();
                newData.Abyss       = (await LoadData<Currency.Item, Currency.RootObject>("Abyss.json",      "items/currency/abyss", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))      ?? new();
                newData.UncutGems   = (await LoadData<Currency.Item, Currency.RootObject>("UncutGems.json",  "items/currency/uncutgems", league, tryWebFirst, settings, logger, dataDir, cancellationToken).ConfigureAwait(false))  ?? new();

                if (cancellationToken.IsCancellationRequested)
                    return;

                new FileInfo(metadataPath).Directory?.Create();
                await File.WriteAllTextAsync(metadataPath, JsonConvert.SerializeObject(new LeagueMetadata { LastLoadTime = DateTime.UtcNow }), cancellationToken).ConfigureAwait(false);

                logger?.Invoke("Finished Gathering Data from Poe.Ninja.");
                CollectedData = newData;
                DivineValue = newData.Currency?.Find(x => x.text == "Divine Orb")?.currentPrice;
                logger?.Invoke("Updated CollectedData.");
            }
            catch (OperationCanceledException)
            {
                logger?.Invoke("Data reload cancelled.");
            }
            catch (Exception ex)
            {
                // Swallow and log to avoid unobserved task exceptions
                logger?.Invoke($"Data reload failed: {ex}");
            }
            finally
            {
                Interlocked.Exchange(ref _updating, 0);
            }
        }, cancellationToken);
    }


    private async Task<bool> IsLocalCacheStale(string metadataPath)
    {
        return await IsLocalCacheStale(metadataPath, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<bool> IsLocalCacheStale(string metadataPath, CancellationToken token)
    {
        if (!File.Exists(metadataPath))
        {
            return true;
        }

        try
        {
            var metadata = JsonConvert.DeserializeObject<LeagueMetadata>(await File.ReadAllTextAsync(metadataPath, token).ConfigureAwait(false));
            return DateTime.UtcNow - metadata.LastLoadTime > TimeSpan.FromMinutes(Settings.DataSourceSettings.ReloadPeriod);
        }
        catch (Exception ex)
        {
            if (Settings?.DebugSettings?.EnableDebugLogging == true)
            {
                log?.Invoke($"Metadata loading failed: {ex}");
            }

            return true;
        }
    }

    private async Task<List<TItem>> LoadData<TItem, TPaged>(string fileName, string category, string league, bool tryWebFirst) where TPaged : class, IPaged<TItem>
    {
        return await LoadData<TItem, TPaged>(fileName, category, league, tryWebFirst, Settings, log, DataDirectory, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<List<TItem>> LoadData<TItem, TPaged>(string fileName, string category, string league, bool tryWebFirst, NinjaPricerSettings settings, Action<string> logger, string dataDir, CancellationToken token) where TPaged : class, IPaged<TItem>
    {
        var backupFile = Path.Join(dataDir, league, fileName);
        if (tryWebFirst)
        {
            if (await LoadPagedDataFromWeb<TItem, TPaged>(fileName, category, league, backupFile, settings, logger, token).ConfigureAwait(false) is { } data)
            {
                return data;
            }
        }

        if (await LoadDataFromBackup<TItem>(fileName, backupFile, settings, logger, token).ConfigureAwait(false) is { } data2)
        {
            return data2;
        }

        if (!tryWebFirst)
        {
            return await LoadPagedDataFromWeb<TItem, TPaged>(fileName, category, league, backupFile, settings, logger, token).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<List<T>> LoadDataFromBackup<T>(string fileName, string backupFile)
    {
        return await LoadDataFromBackup<T>(fileName, backupFile, Settings, log, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<List<T>> LoadDataFromBackup<T>(string fileName, string backupFile, NinjaPricerSettings settings, Action<string> logger, CancellationToken token)
    {
        if (File.Exists(backupFile))
        {
            try
            {
                var data = JsonConvert.DeserializeObject<List<T>>(await File.ReadAllTextAsync(backupFile, token).ConfigureAwait(false));
                return data;
            }
            catch (Exception backupEx)
            {
                if (settings?.DebugSettings?.EnableDebugLogging == true)
                {
                    logger?.Invoke($"{fileName} backup data load failed: {backupEx}");
                }
            }
        }
        else if (settings?.DebugSettings?.EnableDebugLogging == true)
        {
            logger?.Invoke($"No backup for {fileName}");
        }

        return null;
    }

    private async Task<List<TItem>> LoadPagedDataFromWeb<TItem, TPaged>(string fileName, string url, string league, string backupFile) where TPaged: class, IPaged<TItem>
    {
        return await LoadPagedDataFromWeb<TItem, TPaged>(fileName, url, league, backupFile, Settings, log, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<List<TItem>> LoadPagedDataFromWeb<TItem, TPaged>(string fileName, string url, string league, string backupFile, NinjaPricerSettings settings, Action<string> logger, CancellationToken token) where TPaged: class, IPaged<TItem>
    {
        try
        {
            var items = new List<TItem>();
            var page = 1;
            TPaged container = null;
            do
            {
                if (token.IsCancellationRequested)
                    break;

                if (settings?.DebugSettings?.EnableDebugLogging == true)
                {
                    logger?.Invoke($"Downloading {fileName} ({page}/{container?.pages.ToString() ?? "?"})");
                }

                var json = await Utils.DownloadFromUrl(GetLink(url, league, page)).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    // Stop if web returned nothing (shutdown or network issue)
                    break;
                }

                container = JsonConvert.DeserializeObject<TPaged>(json);
                if (container == null)
                {
                    break;
                }

                items.AddRange(container.items ?? Array.Empty<TItem>());
                page++;
            } while (container.currentPage < container.pages);

            if (settings?.DebugSettings?.EnableDebugLogging == true)
            {
                logger?.Invoke($"{fileName} downloaded");
            }

            try
            {
                new FileInfo(backupFile).Directory?.Create();
                await File.WriteAllTextAsync(backupFile, JsonConvert.SerializeObject(items, Formatting.Indented), token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var errorPath = backupFile + ".error";
                new FileInfo(errorPath).Directory?.Create();
                await File.WriteAllTextAsync(errorPath, ex.ToString(), token).ConfigureAwait(false);
                if (settings?.DebugSettings?.EnableDebugLogging == true)
                {
                    logger?.Invoke($"{fileName} save failed: {ex}");
                }
            }

            return items;
        }
        catch (OperationCanceledException)
        {
            logger?.Invoke($"{fileName} download cancelled");
            return null;
        }
        catch (Exception ex)
        {
            if (settings?.DebugSettings?.EnableDebugLogging == true)
            {
                logger?.Invoke($"{fileName} fresh data download failed: {ex}");
            }

            return null;
        }
    }
}